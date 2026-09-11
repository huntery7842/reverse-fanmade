using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using Org.BouncyCastle.Tls;
using ReVerse.Capture.Capturing;

namespace ReVerse.Capture.Signaling;


public sealed class SignalingProvider(SignalingOptions options, SignalingDirectory directory,
    RequestLog log, ILogger<SignalingProvider> logger) : BackgroundService
{
    private DtlsUdpHost? host;
    private long hostEvents;
    private long droppedAuditRecords;
    private readonly Channel<object> audits = Channel.CreateBounded<object>(256);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled) { logger.LogInformation("Signaling provider disabled"); return; }
        host = new DtlsUdpHost(IPAddress.Parse(options.BindAddress), options.Port, options.MaxConnections,
            options.HandshakeTimeoutSeconds, directory.LookupPsk, Connected, HostStage);
        await host.StartAsync(cancellationToken);
        directory.SetListening(true);
        logger.LogWarning("FIELD TEST signaling UDP bound at {Bind}:{Port}; advertised {PublicHost}:{PublicPort}; experimental replies {Experimental}. HTTP tunnels do not carry UDP. Gameplay unverified.",
            options.BindAddress, options.Port, options.PublicHost, options.PublicPort, options.ExperimentalReplies);
        if (options.ExperimentalReplies)
            logger.LogWarning("UNVERIFIED reply settings: type19={Reply19}, type21={Reply21}, registration fieldB={FieldB}. These values are experiment inputs, not recovered semantics.",
                options.Reply19, options.Reply21, options.RegistrationFieldBIsPeerNumber ? "peer-number" : options.RegistrationFieldB.ToString());
        if (options.NegativeControlReply is { } negativeReply)
            logger.LogWarning("NEGATIVE CONTROL: reply {Reply} will be zero; startup/registration will not complete. All new associations on this provider use this test mode.", negativeReply);
        if (options.PeerDiagnostics)
            logger.LogWarning("Copy-only peer diagnostics enabled. Observed records do not prove client acceptance or gameplay readiness; raw payloads and identity values are not logged.");
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (host is null) return;
        var writer = WriteAudit();
        try { await await Task.WhenAny(host.Completion, writer).WaitAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            directory.SetListening(false);
            try { await host.StopAsync(); }
            finally { audits.Writer.TryComplete(); }
            await writer;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        directory.SetListening(false);
        if (host is not null) await host.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        directory.SetListening(false);
        if (host is not null) host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.Dispose();
    }

    private void HostStage(string stage)
    {
        var number = Interlocked.Increment(ref hostEvents);

        if (number <= 64 || number % 100 == 0)
            Audit("transport", null, null, null, new { stage, eventCount = number });
    }

    private void Connected(byte[] identity, DtlsTransport transport, CancellationToken stopping)
    {
        string connection = Guid.NewGuid().ToString("N")[..12];
        var peer = directory.Attach(identity, connection);
        if (peer is null) { Audit("association_rejected", connection, null, null, new { reason = "expired_or_duplicate_identity" }); return; }
        long received = 0, sent = 0, forwarded = 0;
        long payloadRecords = 0, secondaryPayloads = 0;
        var peerSampling = new PeerDiagnosticSampling();
        var wire = new WireConnection(limits: new WireLimits { MaxPacketBytes = 4096 }, retryAfter: TimeSpan.FromMilliseconds(500));
        var conversation = new SignalingConversation(options, directory, peer, connection, stage =>
        {
            if (stage == "peer_payload_forwarded")
            {
                if (++forwarded > 16 && forwarded % 100 != 0) return;
            }
            Audit(stage, connection, peer.Session, peer.Account, new { forwarded });
        }, payload =>
        {
            payloadRecords++;
            if (payload.SecondaryLength > 0) secondaryPayloads++;
            if (payload.PeerPrimary is { } primary && peerSampling.Observe(primary))
                Audit("peer_primary_observed", connection, peer.Session, peer.Account,
                    new { payloadRecords, payload.Outcome, primary });

            if (payload.Outcome != "queued" || payloadRecords <= 32 || payloadRecords % 100 == 0
                || payload.SecondaryLength > 0 && secondaryPayloads <= 8)
                Audit("peer_payload_observed", connection, peer.Session, peer.Account,
                    new { payloadRecords, secondaryPayloads, payload });
        });
        var buffer = new byte[transport.GetReceiveLimit()];
        long lastPacket = Stopwatch.GetTimestamp();
        long lastSummary = lastPacket;
        void Send(IEnumerable<WireDatagram> datagrams)
        {
            foreach (var packet in datagrams)
            {
                if (packet.Bytes.Length > transport.GetSendLimit()) throw new WireFormatException("Encoded packet exceeds DTLS send limit.");
                if (packet.Attempt > 20) throw new WireFormatException("Reliable packet retry budget exhausted.");
                transport.Send(packet.Bytes.Span);
                sent++;
                if (sent <= 32 || packet.IsRetransmission && packet.Attempt is 2 or 5 or 10)
                    Audit("packet_sent", connection, peer.Session, peer.Account,
                        new { packet.Sequence, bytes = packet.Bytes.Length, packet.Attempt, wire.RetainedPacketCount });
            }
        }
        try
        {
            Audit("dtls_authenticated", connection, peer.Session, peer.Account, new { peer.Number });
            while (!stopping.IsCancellationRequested && directory.IsAttached(peer.Session, peer.Account, connection))
            {
                int length = transport.Receive(buffer, 0, buffer.Length, 100);
                if (length > 0)
                {
                    received++;
                    var result = wire.Receive(buffer.AsMemory(0, length));
                    directory.Touch(peer.Session, peer.Account, connection);
                    lastPacket = Stopwatch.GetTimestamp();
                    if (received <= 64)
                        Audit("packet_received", connection, peer.Session, peer.Account, new
                        {
                            bytes = length, result.Packet.Class, result.Packet.Sequence, result.IsDuplicate,
                            types = result.Packet.Contents.Select(c => c.Type).ToArray(),
                            fragments = result.Packet.Contents.OfType<WireFragment>().Select(f => new { f.Descriptor, f.Offset, length = f.Data.Length, f.IsFinal }).ToArray(),
                            applications = result.Applications.Select(a => new { a.Family, a.Operation, a.Qualifier, a.Routing, a.Auxiliary, bytes = a.Body.Length }).ToArray()
                        });
                    Send(result.Outgoing);
                    foreach (var item in result.Contents.Where(c => c is WireControl or WireSettings))
                    {
                        var replies = conversation.Control(item);
                        if (replies.Count != 0)
                            Send(wire.SendContent(replies, item.Type == 3 ? (byte)0x80 : item.Type is 0x12 or 0x14 ? (byte)0x81 : (byte)0x40));
                    }
                    foreach (var application in result.Applications)
                        conversation.Application(application.Family, application.Operation, application.Qualifier, application.Body);
                }
                if (Stopwatch.GetElapsedTime(lastPacket) > TimeSpan.FromSeconds(options.IdleTimeoutSeconds))
                    throw new WireFormatException("Authenticated connection idle deadline exceeded.");
                if (wire.IsBound)
                {
                    int drained = 0;
                    while (drained++ < 32 && directory.Dequeue(peer.Session, peer.Account, connection) is { } item)
                        Send(wire.SendApplication(new(item.Family, item.Operation, item.Qualifier, 0, 0, item.Body), item.Descriptor));
                    Send(wire.Tick());
                }
                if (Stopwatch.GetElapsedTime(lastSummary) > TimeSpan.FromSeconds(5))
                {
                    Audit("traffic_summary", connection, peer.Session, peer.Account,
                        new { received, sent, forwarded, payloadRecords, secondaryPayloads, wire.RetainedPacketCount, wire.BufferedFragmentCount, wire.BufferedBytes });
                    lastSummary = Stopwatch.GetTimestamp();
                }
            }
        }
        catch (WireFormatException exception)
        {

            Audit("protocol_rejected", connection, peer.Session, peer.Account, new { reason = exception.Message, received, sent });
        }
        catch (IOException exception)
        {
            Audit("dtls_closed", connection, peer.Session, peer.Account, new { reason = exception.GetType().Name, received, sent });
        }
        finally
        {
            directory.Detach(peer.Session, peer.Account, connection);
            Audit("association_closed", connection, peer.Session, peer.Account, new { received, sent, forwarded, payloadRecords, secondaryPayloads });
        }
    }

    private void Audit(string stage, string? connection, string? session, string? account, object details)
    {

        if (!audits.Writer.TryWrite(new { type = "signaling", timestampUtc = DateTimeOffset.UtcNow,
            stage, connection, session, account, details, droppedAuditRecords = Interlocked.Read(ref droppedAuditRecords) }))
            Interlocked.Increment(ref droppedAuditRecords);
    }

    private async Task WriteAudit()
    {
        await foreach (var record in audits.Reader.ReadAllAsync()) await log.WriteAsync(record);
    }
}
