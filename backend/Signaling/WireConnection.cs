using System.Security.Cryptography;

namespace ReVerse.Capture.Signaling;

public sealed record WireDatagram(ulong Sequence, ReadOnlyMemory<byte> Bytes, int Attempt = 1)
{
    public bool IsRetransmission => Attempt > 1;
}

public sealed record WireReceiveResult(
    WirePacket Packet, bool IsDuplicate, IReadOnlyList<WireApplication> Applications,
    IReadOnlyList<ulong> AcknowledgedSequences, IReadOnlyList<WireDatagram> Outgoing)
{

    public IReadOnlyList<WireContent> Contents => IsDuplicate ? Array.Empty<WireContent>() : Packet.Contents;
}







public sealed class WireConnection
{
    private readonly WireLimits _limits;
    private readonly Action<ReadOnlyMemory<byte>>? _send;
    private readonly TimeProvider _time;
    private readonly TimeSpan _retryAfter;
    private SortedDictionary<ulong, byte[]> _received = new();
    private Dictionary<int, ChannelState> _channels = new();
    private readonly Dictionary<int, ulong> _outgoingOffsets = new();
    private readonly SortedDictionary<ulong, Retained> _retained = new();
    private ulong _receiveFloor;
    private ulong _nextSequence = 1;
    private bool _ackPending;
    private int _retainedBytes;

    public string? LocalId { get; private set; }
    public string? RemoteId { get; private set; }
    public bool IsBound => LocalId is not null;
    public int RetainedPacketCount => _retained.Count;
    public int RetainedBytes => _retainedBytes;
    public int BufferedFragmentCount => _channels.Values.Sum(c => c.Pending.Count);
    public int BufferedBytes => _channels.Values.Sum(c => c.PendingBytes);
    public int RememberedFragmentCount => _channels.Values.Sum(c => c.RememberedCount);
    public long RememberedFragmentBytes => _channels.Values.Sum(c => c.RememberedBytes);
    public bool AcknowledgmentPending => _ackPending;
    public ulong NextSequence => _nextSequence;

    public WireConnection(Action<ReadOnlyMemory<byte>>? send = null, WireLimits? limits = null,
        TimeProvider? timeProvider = null, TimeSpan? retryAfter = null)
    {
        _limits = limits ?? new();
        _limits.Validate();
        _send = send;
        _time = timeProvider ?? TimeProvider.System;
        _retryAfter = retryAfter ?? TimeSpan.FromSeconds(1);
        if (_retryAfter <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryAfter));
    }

    public ulong GetOutgoingOffset(int channel = 1) => _outgoingOffsets.GetValueOrDefault(channel);

    public ulong GetIncomingOffset(int channel = 1) => _channels.TryGetValue(channel, out var state) ? state.NextOffset : 0;

    public WireReceiveResult Receive(ReadOnlyMemory<byte> datagram)
    {
        if (datagram.Length > _limits.MaxPacketBytes) throw new WireFormatException("Packet exceeds byte limit.");

        var bytes = datagram.ToArray();
        var packet = WirePacket.Parse(bytes, _limits);
        ValidateBinding(packet);
        bool eliciting = packet.Contents.Any(c => c is not WireAcknowledgment and not WirePadding);
        byte[] digest = SHA256.HashData(bytes);
        bool duplicate = packet.Sequence < _receiveFloor;
        if (_received.TryGetValue(packet.Sequence, out var previous))
        {
            if (!digest.AsSpan().SequenceEqual(previous)) throw new WireFormatException("Conflicting duplicate packet sequence.");
            duplicate = true;
        }
        if (duplicate)
        {
            _ackPending |= eliciting;
            return new(packet, true, Array.Empty<WireApplication>(), Array.Empty<ulong>(),
                EmitPendingAck(packet.IsExtended ? (byte)0x80 : (byte)0x40));
        }


        var acknowledged = new SortedSet<ulong>();
        foreach (var ack in packet.Contents.OfType<WireAcknowledgment>())
        {
            ack.Validate(_limits);


            if (_nextSequence == 1 && ack.Ranges.Count == 1 && ack.Ranges[0] == new WireAckRange(0, 0))
                continue;
            if (ack.Ranges[0].High >= _nextSequence || _nextSequence == 1)
                throw new WireFormatException("ACK covers a sequence that has never been sent.");
            foreach (ulong sequence in _retained.Keys)
                if (ack.Ranges.Any(r => sequence >= r.Low && sequence <= r.High)) acknowledged.Add(sequence);
        }
        var channels = _channels.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
        var applications = new List<WireApplication>();
        foreach (var fragment in packet.Contents.OfType<WireFragment>())
        {
            ValidateChannel(fragment.Channel);
            if (!channels.TryGetValue(fragment.Channel, out var channel))
                channels.Add(fragment.Channel, channel = new());
            channel.Accept(fragment, applications, _limits);
        }

        if (applications.Count > _limits.MaxApplicationRecords) throw new WireFormatException("Too many delivered applications.");

        var received = new SortedDictionary<ulong, byte[]>(_received) { [packet.Sequence] = digest };
        ulong floor = _receiveFloor;
        while (received.Count > _limits.MaxReceivedPackets)
        {
            ulong oldest = received.First().Key;
            received.Remove(oldest);
            floor = oldest + 1;
        }

        string local = LocalId ?? packet.SecondId!;
        string remote = RemoteId ?? packet.FirstId;
        WireDatagram? response = null;
        if (_ackPending || eliciting)
            response = BuildPacket([BuildAcknowledgment(received, 0)], packet.IsExtended ? (byte)0x80 : (byte)0x40,
                _nextSequence, local, remote);

        LocalId = local;
        RemoteId = remote;
        _channels = channels;
        _received = received;
        _receiveFloor = floor;
        foreach (ulong sequence in acknowledged)
        {
            _retainedBytes -= _retained[sequence].Bytes.Length;
            _retained.Remove(sequence);
        }
        var outgoing = new List<WireDatagram>();
        if (response is not null)
        {
            _nextSequence++;
            _ackPending = false;
            outgoing.Add(response);
            Emit(response);
        }
        return new(packet, false, applications.AsReadOnly(), acknowledged.ToArray(), outgoing.AsReadOnly());
    }

    private void ValidateBinding(WirePacket packet)
    {
        if (!IsBound)
        {
            if (!packet.IsExtended) throw new WireFormatException("First packet requires extended connection identifiers.");
            if (packet.FirstId.Length == 0 || packet.SecondId!.Length == 0)
                throw new WireFormatException("Cannot bind empty connection identifiers.");
            return;
        }
        if (!string.Equals(packet.FirstId, RemoteId, StringComparison.OrdinalIgnoreCase) ||
            (packet.IsExtended && !string.Equals(packet.SecondId, LocalId, StringComparison.OrdinalIgnoreCase)))
            throw new WireFormatException("Packet does not match this connection's directional identifiers.");
    }


    public IReadOnlyList<WireDatagram> SendContent(byte type, ReadOnlyMemory<byte> payload, byte packetClass = 0x40)
    {
        var writer = new WireWriter(_limits.MaxPacketBytes);
        writer.WriteByte(type);
        writer.WriteBytes(payload.Span);
        var reader = new WireReader(writer.ToArray(), _limits.MaxPacketBytes);
        var content = WireContentCodec.Read(reader, _limits);

        if (type == 3)
        {
            var padding = reader.ReadBytes(reader.Remaining);
            if (padding.Span.IndexOfAnyExcept((byte)0) >= 0) throw new WireFormatException("Probe padding must be zero.");
            return SendContent(padding.Length == 0 ? [content] : [content, new WirePadding(padding.Length)], packetClass);
        }
        reader.RequireEnd();
        return SendContent([content], packetClass);
    }

    public IReadOnlyList<WireDatagram> SendContent(WireContent content, byte packetClass = 0x40) => SendContent([content], packetClass);

    public IReadOnlyList<WireDatagram> SendContent(IReadOnlyList<WireContent> contents, byte packetClass = 0x40)
    {
        EnsureBound();
        if (contents.Any(c => c is WireFragment)) throw new WireFormatException("Use SendApplication to maintain outgoing stream offsets.");
        var items = WithPendingAck(contents);
        var datagram = BuildPacket(items, packetClass, _nextSequence, LocalId!, RemoteId!);
        bool retain = contents.Any(c => c is not WireAcknowledgment and not WirePadding);
        CheckRetention(retain ? 1 : 0, retain ? datagram.Bytes.Length : 0);
        CommitSends([datagram], retain);
        return new[] { datagram };
    }






    public IReadOnlyList<WireDatagram> SendApplication(WireApplication application, byte descriptor = 0x1c,
        byte packetClass = 0x40, int fragmentBodyBytes = 0x514)
    {
        EnsureBound();
        int channel = descriptor >> 4;
        ValidateChannel(channel);
        bool retain = (descriptor & 8) != 0;
        if (fragmentBodyBytes < 1 || fragmentBodyBytes > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(fragmentBodyBytes));
        byte[] bytes = application.Encode();
        if (bytes.Length > _limits.MaxBufferedBytes) throw new WireFormatException("Application exceeds assembly byte limit.");
        int firstCapacity = fragmentBodyBytes + WireApplication.HeaderLength;
        if (retain && channel != 1 && bytes.Length > firstCapacity)
            throw new WireFormatException("Only channel 1 supports fragmented messages.");
        var datagrams = new List<WireDatagram>();
        ulong offset = GetOutgoingOffset(channel);
        int position = 0;
        long totalBytes = 0;
        while (position < bytes.Length)
        {
            if (datagrams.Count >= _limits.MaxFragments) throw new WireFormatException("Send exceeds fragment cap.");
            int length = retain ? Math.Min(bytes.Length - position, position == 0 ? firstCapacity : fragmentBodyBytes) : bytes.Length;
            if (offset > WireReader.MaxCompact - (ulong)length) throw new WireFormatException("Outgoing stream offset overflows.");
            bool final = position + length == bytes.Length;
            var fragment = new WireFragment(final ? (byte)0x0f : (byte)0x0d, descriptor, offset, bytes.AsMemory(position, length));
            IReadOnlyList<WireContent> contents = datagrams.Count == 0 ? WithPendingAck([fragment]) : [fragment];
            var datagram = BuildPacket(contents, packetClass, _nextSequence + (ulong)datagrams.Count, LocalId!, RemoteId!);
            totalBytes += datagram.Bytes.Length;
            datagrams.Add(datagram);
            position += length;
            offset += (ulong)length;
        }
        CheckRetention(retain ? datagrams.Count : 0, retain ? totalBytes : 0);
        _outgoingOffsets[channel] = offset;
        CommitSends(datagrams, retain);
        return datagrams.AsReadOnly();
    }

    public IReadOnlyList<WireDatagram> Tick()
    {
        var result = new List<WireDatagram>();
        result.AddRange(EmitPendingAck());
        result.AddRange(GetRetransmissions());
        return result.AsReadOnly();
    }


    public IReadOnlyList<WireDatagram> GetRetransmissions()
    {
        long now = _time.GetTimestamp();
        var result = new List<WireDatagram>();
        foreach (var retained in _retained.Values)
        {
            if (_time.GetElapsedTime(retained.LastSent, now) < _retryAfter) continue;
            retained.LastSent = now;
            if (retained.Attempt < int.MaxValue) retained.Attempt++;
            result.Add(new(retained.Sequence, retained.Bytes.ToArray(), retained.Attempt));
        }
        foreach (var datagram in result) Emit(datagram);
        return result.AsReadOnly();
    }


    public WireDatagram? CreateAcknowledgment(uint delay = 0, byte packetClass = 0x40)
    {
        if (_received.Count == 0) return null;
        EnsureBound();
        var result = BuildPacket([BuildAcknowledgment(_received, delay)], packetClass, _nextSequence, LocalId!, RemoteId!);
        _nextSequence++;
        _ackPending = false;
        Emit(result);
        return result;
    }

    private IReadOnlyList<WireDatagram> EmitPendingAck(byte packetClass = 0x40)
    {
        if (!_ackPending) return Array.Empty<WireDatagram>();
        var ack = CreateAcknowledgment(packetClass: packetClass);
        return ack is null ? Array.Empty<WireDatagram>() : new[] { ack };
    }

    private WireAcknowledgment BuildAcknowledgment(SortedDictionary<ulong, byte[]> history, uint delay)
    {
        var ranges = new List<WireAckRange>();
        foreach (ulong sequence in history.Keys.Reverse())
        {
            if (ranges.Count > 0 && ranges[^1].Low > 0 && sequence == ranges[^1].Low - 1)
                ranges[^1] = ranges[^1] with { Low = sequence };
            else
            {
                if (ranges.Count == _limits.MaxAckRanges) break;
                ranges.Add(new(sequence, sequence));
            }
        }
        return new(ranges.AsReadOnly(), delay);
    }

    private IReadOnlyList<WireContent> WithPendingAck(IReadOnlyList<WireContent> contents) =>
        _ackPending && _received.Count > 0 ? new WireContent[] { BuildAcknowledgment(_received, 0) }.Concat(contents).ToArray() : contents;

    private WireDatagram BuildPacket(IReadOnlyList<WireContent> contents, byte packetClass, ulong sequence, string local, string remote)
    {
        if (sequence > WireReader.MaxCompact) throw new WireFormatException("Outgoing sequence exhausted.");
        var packet = new WirePacket(packetClass, local, (packetClass & 0x80) != 0 ? remote : null, sequence, contents);
        return new(sequence, packet.Encode(_limits));
    }

    private void CheckRetention(int count, long bytes)
    {
        if (count > _limits.MaxRetainedPackets - _retained.Count || bytes > _limits.MaxRetainedBytes - _retainedBytes)
            throw new WireFormatException("Retained-send capacity exceeded; wait for ACKs.");
    }

    private void CommitSends(IReadOnlyList<WireDatagram> datagrams, bool retain)
    {
        long now = _time.GetTimestamp();
        foreach (var datagram in datagrams)
        {
            if (retain)
            {
                var bytes = datagram.Bytes.ToArray();
                _retained.Add(datagram.Sequence, new(datagram.Sequence, bytes, now));
                _retainedBytes += bytes.Length;
            }
            _nextSequence++;
        }
        _ackPending = false;
        foreach (var datagram in datagrams) Emit(datagram);
    }

    private void Emit(WireDatagram datagram) => _send?.Invoke(datagram.Bytes);
    private void EnsureBound()
    {
        if (!IsBound) throw new WireFormatException("Receive an authenticated extended header before sending.");
    }
    private static void ValidateChannel(int channel)
    {

        if (channel is < 1 or > 4) throw new WireFormatException("Unsupported fragment channel.");
    }

    private sealed class Retained(ulong sequence, byte[] bytes, long lastSent)
    {
        public ulong Sequence { get; } = sequence;
        public byte[] Bytes { get; } = bytes;
        public long LastSent { get; set; } = lastSent;
        public int Attempt { get; set; } = 1;
    }

    private sealed class ChannelState
    {
        public ulong NextOffset { get; private set; }
        public SortedDictionary<ulong, WireFragment> Pending { get; private init; } = new();
        private SortedDictionary<ulong, WireFragment> History { get; init; } = new();
        public int PendingBytes { get; private set; }
        private long HistoryBytes { get; set; }
        private ulong UnorderedFloor { get; set; }
        public int RememberedCount => History.Count;
        public long RememberedBytes => HistoryBytes;

        public ChannelState Clone() => new()
        {
            NextOffset = NextOffset, PendingBytes = PendingBytes, HistoryBytes = HistoryBytes, UnorderedFloor = UnorderedFloor,
            Pending = new(Pending), History = new(History)
        };

        public void Accept(WireFragment fragment, List<WireApplication> applications, WireLimits limits)
        {
            if (fragment.Data.Length == 0 || fragment.Data.Length > limits.MaxBufferedBytes)
                throw new WireFormatException("Empty or oversized application fragment.");
            if (!fragment.IsReliable && !fragment.IsFinal)
                throw new WireFormatException("Descriptor without reliability bit cannot carry partial messages.");
            if (fragment.Channel is 3 or 4)
            {
                AcceptUnordered(fragment, applications, limits);
                return;
            }
            if (fragment.Offset < NextOffset)
            {
                if (History.TryGetValue(fragment.Offset, out var old) && Same(old, fragment)) return;
                throw new WireFormatException("Conflicting or unverifiable already-delivered fragment.");
            }
            if (Pending.TryGetValue(fragment.Offset, out var existing))
            {
                if (Same(existing, fragment)) return;
                throw new WireFormatException("Conflicting duplicate fragment.");
            }
            ulong end = fragment.Offset + (ulong)fragment.Data.Length;
            if (Pending.Values.Any(p => fragment.Offset < p.Offset + (ulong)p.Data.Length && end > p.Offset))
                throw new WireFormatException("Overlapping fragments.");
            if (fragment.Channel != 1 && (!fragment.IsFinal || fragment.Offset != NextOffset))
                throw new WireFormatException("This channel supports only contiguous, complete final messages.");
            if (Pending.Count >= limits.MaxFragments || fragment.Data.Length > limits.MaxBufferedBytes - PendingBytes)
                throw new WireFormatException("Pending fragment capacity exceeded.");

            fragment = fragment with { Data = fragment.Data.ToArray() };
            Pending.Add(fragment.Offset, fragment);
            PendingBytes += fragment.Data.Length;
            Drain(applications, limits);
        }

        private static bool Same(WireFragment first, WireFragment second) =>
            first.Descriptor == second.Descriptor && first.IsFinal == second.IsFinal && first.Data.Span.SequenceEqual(second.Data.Span);

        private void AcceptUnordered(WireFragment fragment, List<WireApplication> applications, WireLimits limits)
        {
            if (!fragment.IsFinal) throw new WireFormatException("Unordered channel requires a complete final message.");
            ulong end = fragment.Offset + (ulong)fragment.Data.Length;
            if (History.TryGetValue(fragment.Offset, out var old))
            {
                if (Same(old, fragment)) return;
                throw new WireFormatException("Conflicting duplicate datagram offset.");
            }
            if (fragment.Offset < UnorderedFloor)
            {
                if (end > UnorderedFloor) throw new WireFormatException("Datagram overlaps expired replay history.");
                return;
            }
            if (History.Values.Any(p => fragment.Offset < p.Offset + (ulong)p.Data.Length && end > p.Offset))
                throw new WireFormatException("Overlapping datagram offsets.");
            Deliver(fragment.Data, fragment.Descriptor, applications, limits);
            Remember(fragment with { Data = fragment.Data.ToArray() }, limits, unordered: true);
            NextOffset = Math.Max(NextOffset, end);
        }

        private static void Deliver(ReadOnlyMemory<byte> bytes, byte descriptor, List<WireApplication> applications, WireLimits limits)
        {
            var decoded = WireApplication.ParseMany(bytes, limits);
            if (decoded.Count > limits.MaxApplicationRecords - applications.Count) throw new WireFormatException("Too many application records.");
            applications.AddRange(decoded.Select(a => a with { Descriptor = descriptor }));
        }

        private void Remember(WireFragment fragment, WireLimits limits, bool unordered)
        {
            History.Add(fragment.Offset, fragment);
            HistoryBytes += fragment.Data.Length;
            while (History.Count > limits.MaxFragments || HistoryBytes > limits.MaxBufferedBytes)
            {
                var oldest = History.First();
                HistoryBytes -= oldest.Value.Data.Length;
                History.Remove(oldest.Key);
                if (unordered) UnorderedFloor = oldest.Key + (ulong)oldest.Value.Data.Length;
            }
        }

        private void Drain(List<WireApplication> applications, WireLimits limits)
        {
            while (Pending.TryGetValue(NextOffset, out var first))
            {
                var chain = new List<WireFragment>();
                ulong next = NextOffset;
                int length = 0;
                bool final = false;
                while (Pending.TryGetValue(next, out var fragment))
                {
                    if (fragment.Descriptor != first.Descriptor) throw new WireFormatException("Descriptor changes within an application message.");
                    chain.Add(fragment);
                    length += fragment.Data.Length;
                    next += (ulong)fragment.Data.Length;
                    if (fragment.IsFinal) { final = true; break; }
                }
                if (!final) return;
                var bytes = new byte[length];
                int position = 0;
                foreach (var fragment in chain)
                {
                    fragment.Data.Span.CopyTo(bytes.AsSpan(position));
                    position += fragment.Data.Length;
                }
                Deliver(bytes, first.Descriptor, applications, limits);
                foreach (var fragment in chain)
                {
                    Pending.Remove(fragment.Offset);
                    PendingBytes -= fragment.Data.Length;
                    Remember(fragment, limits, unordered: false);
                }
                NextOffset = next;
            }
        }
    }
}
