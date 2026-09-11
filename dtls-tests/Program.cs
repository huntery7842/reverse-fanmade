using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using ReVerse.Capture.Signaling;

await DtlsTests.Run();

internal static class DtlsTests
{
    private static readonly byte[] Identity = Encoding.ASCII.GetBytes("loopback-session-identity");
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    internal static async Task Run()
    {
        await Echo(CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256, IPAddress.Loopback);
        await Echo(CipherSuite.TLS_PSK_WITH_AES_128_CCM_8, IPAddress.Loopback);
        if (Socket.OSSupportsIPv6)
            await Echo(CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256, IPAddress.IPv6Loopback);
        await Reject("wrong PSK", Identity, RandomNumberGenerator.GetBytes(32), expectLookup: true);
        await Reject("unknown identity", Encoding.ASCII.GetBytes("unknown"), Key, expectLookup: true);
        await Reject("40-byte identity", new byte[40], Key, expectLookup: false);
        await Reject("non-ASCII identity", [0x80], Key, expectLookup: false);
        await CookieAndBounds();
        await BindAndShutdown();
        await CallbackFailure();
        await BoundedShutdown();
        await CancellationCallbackFailure();
        QueueSemantics();
        Console.WriteLine("PASS: all DTLS loopback and resource-bound tests");
    }

    private static async Task Echo(int cipherSuite, IPAddress address)
    {
        var stages = new ConcurrentQueue<string>();
        byte[] identity = Enumerable.Repeat((byte)'a', 39).ToArray();
        byte[] originalKey = Key.ToArray();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = new DtlsUdpHost(address, 0, 2, 3,
            candidate => candidate.AsSpan().SequenceEqual(identity) ? Key : null,
            (authenticated, transport, token) =>
            {
                Check(authenticated.AsSpan().SequenceEqual(identity), "authenticated identity differs");
                byte[] buffer = new byte[transport.GetReceiveLimit()];
                int count = transport.Receive(buffer.AsSpan(), 2000);
                Check(count == 4, "server did not receive application datagram");
                transport.Send(buffer.AsSpan(0, count));
                received.TrySetResult();
            }, stages.Enqueue);
        Check(host.LocalEndPoint is null && !host.IsRunning, "host advertised before bind");
        await host.StartAsync();
        Check(host.IsRunning && host.LocalEndPoint is { Port: > 0 }, "host did not bind before StartAsync completed");
        using var udp = new ClientUdp(host.LocalEndPoint!);
        var tls = new TestClient(identity, Key, cipherSuite);
        DtlsTransport client = new DtlsClientProtocol().Connect(tls, udp);
        client.Send(new byte[] { 1, 2, 3, 4 }.AsSpan());
        byte[] echo = new byte[client.GetReceiveLimit()];
        Check(client.Receive(echo.AsSpan(), 2000) == 4 && echo.AsSpan(0, 4).SequenceEqual(new byte[] { 1, 2, 3, 4 }),
            "echo payload mismatch");
        await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(tls.NegotiatedCipher == cipherSuite, "unexpected negotiated cipher");
        Check(tls.NegotiatedVersion?.Equals(ProtocolVersion.DTLSv12) == true, "unexpected negotiated version");
        Check(Key.AsSpan().SequenceEqual(originalKey), "server modified directory-owned PSK");
        Check(udp.CookieReplies >= 1, "handshake did not use stateless cookie");
        client.Close();
        await host.StopAsync();
        Check(stages.Contains("authenticated"), "authentication stage missing");
        Check(!string.Join('|', stages).Contains(Encoding.ASCII.GetString(identity), StringComparison.Ordinal), "identity logged");
        Console.WriteLine($"PASS: real DTLS 1.2 echo, cipher 0x{cipherSuite:x4}, {address.AddressFamily}, 39-byte identity, span I/O");
    }

    private static async Task Reject(string label, byte[] identity, byte[] key, bool expectLookup)
    {
        int lookups = 0;
        int authenticated = 0;
        await using var host = new DtlsUdpHost(IPAddress.Loopback, 0, 2, 1,
            candidate =>
            {
                Interlocked.Increment(ref lookups);
                return candidate.AsSpan().SequenceEqual(Identity) ? Key : null;
            }, (_, _, _) => Interlocked.Increment(ref authenticated));
        await host.StartAsync();
        using var udp = new ClientUdp(host.LocalEndPoint!);
        bool rejected = false;
        var elapsed = Stopwatch.StartNew();
        try
        {
            new DtlsClientProtocol().Connect(new TestClient(identity, key, CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256), udp).Close();
        }
        catch (IOException)
        {
            rejected = true;
        }
        Check(rejected && authenticated == 0, label + " reached authenticated handler");
        Check(expectLookup ? lookups == 1 : lookups == 0, label + " lookup validation failed");
        Check(elapsed.Elapsed < TimeSpan.FromSeconds(5), label + " rejection was unbounded");
        await host.StopAsync();
        Console.WriteLine("PASS: rejection of " + label);
    }

    private static async Task CookieAndBounds()
    {
        var stages = new ConcurrentQueue<string>();
        int lookupCount = 0;
        await using var host = new DtlsUdpHost(IPAddress.Loopback, 0, 1, 1,
            _ => { Interlocked.Increment(ref lookupCount); return Key; }, (_, _, _) => { }, stages.Enqueue,
            queueCapacity: 2, maxDatagramBytes: 2048);
        await host.StartAsync();


        using var capture = new ClientUdp(host.LocalEndPoint!, captureOnly: true);
        try
        {
            new DtlsClientProtocol().Connect(new TestClient(Identity, Key, CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256), capture);
            throw new InvalidOperationException("capture unexpectedly completed handshake");
        }
        catch (CaptureCompleteException) { }
        catch (TlsFatalAlert error) when (error.InnerException is CaptureCompleteException) { }
        byte[] hello = capture.FirstSent ?? throw new InvalidOperationException("missing captured ClientHello");

        using var first = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        first.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        first.Connect(host.LocalEndPoint!);
        first.ReceiveTimeout = 1500;
        first.Send(hello);
        byte[] cookieReply = new byte[2048];
        int cookieLength = first.Receive(cookieReply);
        Check(cookieLength <= hello.Length && cookieReply[13] == 3, "cookie reply amplified or is not HelloVerifyRequest");
        await Task.Delay(150);
        Check(host.ConnectionCount == 0 && lookupCount == 0 && !stages.Contains("handshake-started"),
            "unverified ClientHello allocated a connection/lookup");


        using var stalled = new ClientUdp(host.LocalEndPoint!, stallAfterCookie: true);
        try
        {
            new DtlsClientProtocol().Connect(new TestClient(Identity, Key, CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256), stalled);
        }
        catch (CaptureCompleteException) { }
        catch (TlsFatalAlert error) when (error.InnerException is CaptureCompleteException) { }
        await WaitUntil(() => host.ConnectionCount == 1, TimeSpan.FromSeconds(1));
        for (int i = 0; i < 200; ++i) first.Send(hello);
        Check(host.ConnectionCount == 1, "connection cap exceeded");
        await WaitUntil(() => host.ConnectionCount == 0, TimeSpan.FromSeconds(3));
        Check(lookupCount == 0, "stalled ClientHello reached PSK lookup");
        first.Send(stalled.CookieHello ?? throw new InvalidOperationException("missing cookie-bearing ClientHello"));
        int replayLength = first.Receive(cookieReply);
        Check(replayLength > 13 && cookieReply[13] == 3, "cookie replay from different endpoint bypassed verification");
        await Task.Delay(150);
        Check(host.ConnectionCount == 0, "endpoint-mismatched cookie allocated connection");
        await host.StopAsync();
        Console.WriteLine("PASS: cookie before allocation, no amplification, endpoint replay rejection, connection cap, stalled-handshake expiry");
    }

    private static async Task BindAndShutdown()
    {
        using var occupied = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        occupied.ExclusiveAddressUse = true;
        occupied.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int occupiedPort = ((IPEndPoint)occupied.LocalEndPoint!).Port;
        await using var cannotBind = new DtlsUdpHost(IPAddress.Loopback, occupiedPort, 1, 2, _ => Key, (_, _, _) => { });
        bool failed = false;
        try { await cannotBind.StartAsync(); }
        catch (DtlsHostException) { failed = true; }
        Check(failed && cannotBind.LocalEndPoint is null && !cannotBind.IsRunning, "bind failure advertised readiness");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new DtlsUdpHost(IPAddress.Loopback, 0, 1, 2, _ => Key,
            (_, transport, _) =>
            {
                entered.TrySetResult();
                transport.Receive(new byte[transport.GetReceiveLimit()].AsSpan(), 0);
            });
        await host.StartAsync();
        IPEndPoint bound = host.LocalEndPoint!;
        using var udp = new ClientUdp(bound);
        var client = new DtlsClientProtocol().Connect(new TestClient(Identity, Key, CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256), udp);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var elapsed = Stopwatch.StartNew();
        await host.StopAsync();
        Check(elapsed.Elapsed < TimeSpan.FromSeconds(2) && host.LocalEndPoint is null, "stop did not unblock infinite Receive");
        using var rebound = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        rebound.ExclusiveAddressUse = true;
        rebound.Bind(bound);
        client.Close();
        await host.DisposeAsync();
        Console.WriteLine("PASS: bind failure is not ready; stop wakes infinite Receive and releases Windows UDP port");
    }

    private static async Task CallbackFailure()
    {
        var host = new DtlsUdpHost(IPAddress.Loopback, 0, 1, 1,
            _ => throw new InvalidOperationException("sensitive-callback-text"), (_, _, _) => { });
        await host.StartAsync();
        using var udp = new ClientUdp(host.LocalEndPoint!);
        try { new DtlsClientProtocol().Connect(new TestClient(Identity, Key, CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256), udp); }
        catch (IOException) { }
        bool surfaced = false;
        try { await host.Completion.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (DtlsHostException error)
        {
            Check(!error.ToString().Contains("sensitive-callback-text", StringComparison.Ordinal), "callback secret escaped");
            surfaced = true;
        }
        Check(surfaced && !host.IsRunning, "lookup exception was swallowed");
        try { await host.StopAsync(); }
        catch (DtlsHostException) { }
        Console.WriteLine("PASS: callback failure surfaces safely through Completion");
    }

    private static async Task BoundedShutdown()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new DtlsUdpHost(IPAddress.Loopback, 0, 1, 1, _ => Key,
            (_, _, _) =>
            {
                entered.TrySetResult();
                release.Wait();
                exited.TrySetResult();
            }, shutdownTimeoutSeconds: 1);
        await host.StartAsync();
        IPEndPoint endpoint = host.LocalEndPoint!;
        using var udp = new ClientUdp(endpoint);
        var client = new DtlsClientProtocol().Connect(new TestClient(Identity, Key, CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256), udp);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        bool deadline = false;
        var elapsed = Stopwatch.StartNew();
        try { await host.StopAsync(); }
        catch (DtlsHostException) { deadline = true; }
        finally { release.Set(); }
        Check(deadline && elapsed.Elapsed < TimeSpan.FromSeconds(3), "uncooperative callback blocked stop indefinitely");
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var rebound = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        rebound.ExclusiveAddressUse = true;
        rebound.Bind(endpoint);
        client.Close();
        Console.WriteLine("PASS: uncooperative callback has a bounded shutdown deadline and socket is released");
    }

    private static async Task CancellationCallbackFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new DtlsUdpHost(IPAddress.Loopback, 0, 1, 1, _ => Key,
            (_, transport, token) =>
            {
                token.Register(() => throw new InvalidOperationException("sensitive-cancellation-text"));
                entered.TrySetResult();
                transport.Receive(new byte[transport.GetReceiveLimit()].AsSpan(), 0);
            });
        await host.StartAsync();
        using var udp = new ClientUdp(host.LocalEndPoint!);
        var client = new DtlsClientProtocol().Connect(new TestClient(Identity, Key, CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256), udp);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        bool surfaced = false;
        try { await host.StopAsync(); }
        catch (DtlsHostException error)
        {
            Check(!error.ToString().Contains("sensitive-cancellation-text", StringComparison.Ordinal), "cancellation secret escaped");
            surfaced = true;
        }
        Check(surfaced, "cancellation callback failure was swallowed");
        client.Close();
        Console.WriteLine("PASS: cancellation callback failure surfaces safely");
    }

    private static void QueueSemantics()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var queue = new DtlsDatagramTransport(socket, new IPEndPoint(IPAddress.Loopback, 9), 2, 512);
        Check(queue.TryEnqueue([1]) && queue.TryEnqueue([2]) && !queue.TryEnqueue([3]), "queue overflow not dropped");
        Check(!queue.TryEnqueue(new byte[513]), "oversized datagram accepted");
        Span<byte> buffer = stackalloc byte[10];
        Check(queue.Receive(buffer, 20) == 1 && buffer[0] == 1, "queue order changed");
        Check(queue.Receive(buffer, 20) == 1 && buffer[0] == 2, "queue order changed");
        Check(queue.Receive(buffer, 20) == -1, "Receive timeout is not -1");
        queue.Close();
        bool closed = false;
        try { queue.Receive(buffer, 0); }
        catch (IOException) { closed = true; }
        Check(closed && !queue.TryEnqueue([1]), "closed queue accepted I/O");
        Console.WriteLine("PASS: queue cap, datagram size cap, FIFO, finite wait, close semantics");
    }

    private static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (!predicate())
        {
            if (elapsed.Elapsed > timeout) throw new InvalidOperationException("condition deadline exceeded");
            await Task.Delay(20);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class TestClient(byte[] identity, byte[] key, int cipherSuite)
    : PskTlsClient(new BcTlsCrypto(), identity.ToArray(), key.ToArray())
{
    internal int NegotiatedCipher { get; private set; }
    internal ProtocolVersion? NegotiatedVersion { get; private set; }
    protected override ProtocolVersion[] GetSupportedVersions() => ProtocolVersion.DTLSv12.Only();
    protected override int[] GetSupportedCipherSuites() => [cipherSuite];
    public override int GetHandshakeTimeoutMillis() => 2500;
    public override void NotifyHandshakeComplete()
    {
        base.NotifyHandshakeComplete();
        NegotiatedCipher = m_context.SecurityParameters.CipherSuite;
        NegotiatedVersion = m_context.ServerVersion;
    }
}

internal sealed class CaptureCompleteException : Exception;

internal sealed class ClientUdp : DatagramTransport, IDisposable
{
    private readonly Socket socket;
    private readonly bool captureOnly;
    private readonly bool stallAfterCookie;
    private bool preserveSocket;
    internal int CookieReplies { get; private set; }
    internal byte[]? FirstSent { get; private set; }
    internal byte[]? CookieHello { get; private set; }

    internal ClientUdp(IPEndPoint server, bool captureOnly = false, bool stallAfterCookie = false)
    {
        this.captureOnly = captureOnly;
        this.stallAfterCookie = stallAfterCookie;
        socket = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(server.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Loopback : IPAddress.IPv6Loopback, 0));
        socket.Connect(server);
    }

    public int GetReceiveLimit() => 2048;
    public int GetSendLimit() => 2048;
    public int Receive(byte[] buf, int off, int len, int waitMillis) => Receive(buf.AsSpan(off, len), waitMillis);
    public int Receive(Span<byte> buffer, int waitMillis)
    {
        if (preserveSocket) throw new CaptureCompleteException();
        socket.ReceiveTimeout = waitMillis;
        try
        {
            int length = socket.Receive(buffer);
            if (length > 13 && buffer[0] == 22 && buffer[13] == 3) ++CookieReplies;
            return length;
        }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.TimedOut) { return -1; }
        catch (SocketException) { throw new IOException("Loopback UDP receive failed."); }
    }
    public void Send(byte[] buf, int off, int len) => Send(buf.AsSpan(off, len));
    public void Send(ReadOnlySpan<byte> buffer)
    {
        FirstSent ??= buffer.ToArray();
        if (captureOnly) throw new CaptureCompleteException();
        if (preserveSocket) return;
        socket.Send(buffer);
        if (stallAfterCookie && CookieReplies > 0)
        {
            CookieHello = buffer.ToArray();
            preserveSocket = true;
        }
    }
    public void Close() { if (!preserveSocket) socket.Dispose(); }
    public void Dispose() => socket.Dispose();
}
