using ReVerse.Capture.Signaling;



var tests = new (string Name, Action Run)[]
{
    ("compact boundary fixtures and all truncated prefixes", Tests.CompactFixtures),
    ("fixed-width big endian and byte strings", Tests.FixedIntegers),
    ("bounded primitives and strict packed hex", Tests.PrimitiveBounds),
    ("extended and short header exact fixtures", Tests.HeaderFixtures),
    ("reversed connection IDs and immutable association binding", Tests.ConnectionBinding),
    ("malformed header does not bind or emit", Tests.HeaderFailures),
    ("1201-byte probe coalesces padding", Tests.Probe),
    ("recovered controls and settings exact fixtures", Tests.Controls),
    ("unknown and malformed control boundaries rejected", Tests.ControlFailures),
    ("all eight fragment framing bit combinations", Tests.FragmentFraming),
    ("compact descriptor fixtures above 63 and byte-range bounds", Tests.DescriptorFixtures),
    ("fragment lengths and offset overflow rejected", Tests.FragmentFramingFailures),
    ("ACK inclusive range arithmetic fixture", Tests.AckFixture),
    ("ACK underflow, overflow, count and truncation bounds", Tests.AckFailures),
    ("selective ACK retires exactly covered retained sends", Tests.SelectiveAck),
    ("unsent ACK rejected without retiring real packets", Tests.UnsentAck),
    ("ACK and application validation is atomic", Tests.AtomicReceive),
    ("ACK history preserves holes and caps sparse ranges", Tests.AckHistory),
    ("empty history does not fabricate sequence-zero ACK", Tests.EmptyHistory),
    ("inferred initial ACK-zero sentinel is a no-op; first ACK is extended", Tests.InitialAckSentinel),
    ("packet duplicates ACK again but deliver controls once", Tests.PacketDuplicates),
    ("conflicting duplicate sequences rejected", Tests.ConflictingPackets),
    ("old sequence window cannot replay content", Tests.ReceiveWindow),
    ("ACK-only packets never create ACK loops", Tests.AckOnly),
    ("fixed 11-byte application and readiness fixture", Tests.ApplicationFixture),
    ("multiple applications, body truncation and count limits", Tests.ApplicationFailures),
    ("channel-1 final-first and missing middle reassembly", Tests.FinalFirst),
    ("nonfinal chains wait; adjacent final drains multiple messages", Tests.AdjacentMessages),
    ("pending and delivered fragment duplicates suppressed", Tests.FragmentDuplicates),
    ("conflicting duplicate offsets, final flags and overlap rejected", Tests.FragmentConflicts),
    ("malformed assembly preserves queued data for valid retry", Tests.MalformedAssembly),
    ("reassembly enforces 64-fragment queue cap", Tests.FragmentCap),
    ("reassembly enforces retained byte cap", Tests.FragmentByteCap),
    ("complete messages and duplicates on channels 2/3/4", Tests.OtherChannels),
    ("channels 3/4 deliver past gaps and out of order without waiting", Tests.UnorderedDelivery),
    ("unordered datagram duplicate/overlap guards and bounded history", Tests.UnorderedBounds),
    ("unknown channels and unsupported partial modes rejected", Tests.UnsupportedChannels),
    ("outgoing fragmentation counts 11-byte header and advances offsets", Tests.SendFragments),
    ("per-connection and per-channel send offsets independent", Tests.IndependentConnections),
    ("descriptor bit 3 controls fragmentation and send retention", Tests.UnreliableSends),
    ("timed retransmits preserve sequence, class, bytes and offsets", Tests.Retransmission),
    ("retained send count/byte exhaustion is atomic", Tests.SendBounds),
    ("send callback emits each ACK/send/retry once", Tests.Callback),
    ("two wire endpoints exchange reordered fragments and selective ACKs", Tests.EndpointExchange),
    ("asymmetric nonce fixture and direction-specific wrappers", Tests.RoutingEnvelopes),
    ("secondary payload metadata and opaque tag-1 block", Tests.SecondaryPayload),
    ("nested and directional envelope malformed lengths", Tests.EnvelopeFailures),
    ("bounded deterministic malformed datagram corpus", Tests.MalformedCorpus)
};
int failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} offline signaling tests passed.");
return failed == 0 ? 0 : 1;

internal static class Tests
{
    private const string Client = "01020304";
    private const string Server = "aabbccdd";
    private static byte[] Hex(string value) => Convert.FromHexString(value.Replace(" ", ""));
    private static void Check(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}");
    private static void Bytes(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) => Check(expected.SequenceEqual(actual), "Byte sequences differ");
    private static void Reject(Action action)
    {
        try { action(); }
        catch (WireFormatException) { return; }
        throw new InvalidOperationException("Expected WireFormatException");
    }
    private static byte[] Packet(ulong sequence, params WireContent[] contents) => new WirePacket(0x40, Client, null, sequence, contents).Encode();
    private static byte[] Extended(ulong sequence, params WireContent[] contents) => new WirePacket(0x80, Client, Server, sequence, contents).Encode();
    private static byte[] Raw(string content) => Packet(1).Concat(Hex(content)).ToArray();
    private static WireConnection Bound(WireLimits? limits = null, ManualTime? clock = null, Action<ReadOnlyMemory<byte>>? send = null)
    {
        var connection = new WireConnection(send, limits, clock);
        connection.Receive(Extended(0));
        return connection;
    }
    private static WireApplication App(int bodyBytes = 5) => new(1, 1, 1, 0x1234, 0x89abcdef,
        Enumerable.Range(0, bodyBytes).Select(i => (byte)(i & 255)).ToArray());
    private static WireFragment Fragment(ulong offset, ReadOnlyMemory<byte> bytes, bool final = true, byte descriptor = 0x1c) =>
        new(final ? (byte)0x0f : (byte)0x0d, descriptor, offset, bytes);
    private static WireControl Control() => new(3, Array.Empty<ulong>());
    private static WireAcknowledgment Ack(params WireAckRange[] ranges) => new(ranges);
    private static WireAcknowledgment EmittedAck(WireReceiveResult result) => (WireAcknowledgment)WirePacket.Parse(result.Outgoing.Single().Bytes).Contents.Single();

    public static void CompactFixtures()
    {
        (ulong Value, string Hex)[] fixtures =
        [
            (0, "00"), (63, "3f"), (64, "4040"), (16383, "7fff"),
            (16384, "80004000"), ((1UL << 30) - 1, "bfffffff"),
            (1UL << 30, "c000000040000000"), (WireReader.MaxCompact, "ffffffffffffffff")
        ];
        foreach (var (value, hex) in fixtures)
        {
            var writer = new WireWriter(); writer.WriteCompact(value);
            byte[] bytes = Hex(hex);
            Bytes(bytes, writer.ToArray());
            var reader = new WireReader(bytes);
            Equal(value, reader.ReadCompact()); reader.RequireEnd();
            for (int length = 0; length < bytes.Length; length++)
            {
                int capturedLength = length;
                Reject(() => new WireReader(bytes.AsMemory(0, capturedLength)).ReadCompact());
            }
        }
        Equal(0UL, new WireReader(Hex("4000")).ReadCompact());
        Reject(() => new WireWriter().WriteCompact(WireReader.MaxCompact + 1));
        Reject(() => new WireReader(Hex("c000000100000000")).ReadCompactUInt32());
    }

    public static void FixedIntegers()
    {
        var writer = new WireWriter();
        writer.WriteUInt16(0x1234); writer.WriteUInt32(0x89abcdef); writer.WriteUInt64(0x0123456789abcdef);
        writer.WriteBytes16(Hex("80ff0041"));
        Bytes(Hex("123489abcdef0123456789abcdef000480ff0041"), writer.ToArray());
        var reader = new WireReader(writer.ToArray());
        Equal((ushort)0x1234, reader.ReadUInt16()); Equal(0x89abcdefU, reader.ReadUInt32());
        Equal(0x0123456789abcdefUL, reader.ReadUInt64()); Bytes(Hex("80ff0041"), reader.ReadBytes16().Span);
        reader.RequireEnd();
    }

    public static void PrimitiveBounds()
    {
        Reject(() => new WireReader(new byte[2], 1));
        Reject(() => new WireReader(new byte[1]).ReadBytes(-1));
        Reject(() => new WireReader(new byte[1]).ReadUInt16());
        Reject(() => new WireReader(new byte[3]).ReadUInt32());
        Reject(() => new WireReader(new byte[7]).ReadUInt64());
        Reject(() => new WireReader(Hex("00034142")).ReadBytes16());
        Reject(() => new WireReader(Hex("01")).RequireEnd());
        Reject(() => new WireReader(Hex("0a")).ReadCompactLength(9));
        Reject(() => new WireWriter(1).WriteUInt16(0));
        Reject(() => new WireWriter().WriteBytes16(new byte[65536]));
        var writer = new WireWriter(); writer.WritePackedHex("0123ABcd");
        Bytes(Hex("040123abcd"), writer.ToArray());
        Equal("0123abcd", new WireReader(writer.ToArray()).ReadPackedHex());
        foreach (string bad in new[] { "a", "gg", "0x", "aa:1", new string('a', 512) })
            Reject(() => new WireWriter().WritePackedHex(bad));
        Reject(() => new WireReader(Hex("04aabb")).ReadPackedHex());
    }

    public static void HeaderFixtures()
    {
        var packet = new WirePacket(0x81, "0123abcd", "89abcdef", 64, [new WireControl(0x12, [1, 1])]);
        byte[] exact = Hex("8100000001040123abcd0489abcdef4040120101");
        Bytes(exact, packet.Encode()); Bytes(exact, WirePacket.Parse(exact).Encode());
        Bytes(Hex("40040123abcd3f"), new WirePacket(0x40, "0123abcd", null, 63, []).Encode());
    }

    public static void ConnectionBinding()
    {
        var connection = Bound();
        Equal(Server, connection.LocalId); Equal(Client, connection.RemoteId);
        var shortReply = WirePacket.Parse(connection.SendContent(Control()).Single().Bytes);
        Equal(Server, shortReply.FirstId); Check(shortReply.SecondId is null);
        var extendedReply = WirePacket.Parse(connection.SendContent(Control(), 0x81).Single().Bytes);
        Equal(Server, extendedReply.FirstId); Equal(Client, extendedReply.SecondId);
        connection.Receive(Packet(1));
        Reject(() => connection.Receive(new WirePacket(0x40, Server, null, 2, []).Encode()));
        Reject(() => connection.Receive(new WirePacket(0x80, Client, "ffffffff", 2, []).Encode()));
        Equal(Server, connection.LocalId); Equal(Client, connection.RemoteId);
        connection.Receive(Extended(2));
    }

    public static void HeaderFailures()
    {
        var connection = new WireConnection();
        Reject(() => connection.Receive(Packet(1)));
        Reject(() => WirePacket.Parse(Hex("00")));
        Reject(() => WirePacket.Parse(Hex("8000000002")));
        byte[] header = Extended(0);
        for (int length = 0; length < header.Length; length++)
        {
            int cut = length; Reject(() => connection.Receive(header.AsMemory(0, cut)));
        }
        Reject(() => connection.Receive(new WirePacket(0x80, "", Server, 0, []).Encode()));
        Reject(() => new WirePacket(0x40, Client, Server, 0, []).Encode());
        Reject(() => new WirePacket(0x80, Client, null, 0, []).Encode());
        Reject(() => new WirePacket(0, Client, null, 0, []).Encode());
        Reject(() => WirePacket.Parse(header, new WireLimits { MaxPacketBytes = 8 }));
        Check(!connection.IsBound); Equal(1UL, connection.NextSequence);
    }

    public static void Probe()
    {
        var bytes = Extended(1, Control(), new WirePadding(1200));
        var packet = WirePacket.Parse(bytes, new WireLimits { MaxContentItems = 2 });
        Equal(2, packet.Contents.Count); Equal(1200, ((WirePadding)packet.Contents[1]).Count);
        var result = new WireConnection().Receive(bytes);
        Equal(0, result.Applications.Count); Equal(1, result.Outgoing.Count);
        Check(result.Outgoing.All(d => WirePacket.Parse(d.Bytes).Contents.All(c => c is WireAcknowledgment)));
        var sent = Bound().SendContent(3, new byte[1200], 0x80).Single();
        Equal(1200, ((WirePadding)WirePacket.Parse(sent.Bytes).Contents[1]).Count);
        Reject(() => Bound().SendContent(3, Hex("0001")));
    }

    public static void Controls()
    {
        byte[] bytes = Raw("0112010114010405132a153b160300025388010103020119");
        var contents = WirePacket.Parse(bytes).Contents;
        Equal(8, contents.Count); Equal(0, ((WireControl)contents[0]).Values.Count);
        Equal(2, ((WireControl)contents[1]).Values.Count);
        Equal(42UL, ((WireControl)contents[5]).Values[0]);
        var settings = (WireSettings)contents[^1];
        Equal(5000UL, settings.Entries[0].Value); Equal(3UL, settings.Entries[1].Value); Equal(25UL, settings.Entries[2].Value);
        Bytes(bytes, WirePacket.Parse(bytes).Encode());
        var connection = Bound();
        Equal((byte)0x12, WirePacket.Parse(connection.SendContent(0x12, Hex("0101")).Single().Bytes).Contents.Single().Type);
        Equal((byte)0x16, WirePacket.Parse(connection.SendContent(0x16, Hex("03000101010102020103")).Single().Bytes).Contents.Single().Type);
    }

    public static void ControlFailures()
    {
        foreach (string body in new[] { "06", "10", "11", "17", "ff", "1201", "14", "13", "15", "13c000000100000000", "15c000000100000000", "1601", "16010000", "160100020001", "1601000240" })
            Reject(() => WirePacket.Parse(Raw(body)));
        Reject(() => WirePacket.Parse(Raw("040504"), new WireLimits { MaxContentItems = 2 }));
        Reject(() => WirePacket.Parse(Raw("1602000100010101"), new WireLimits { MaxSettings = 1 }));
        Reject(() => Bound().SendContent(4, Hex("00")));
        Reject(() => Bound().SendContent(0x12, Hex("01")));
        Reject(() => Bound().SendContent(new WireControl(4, [1])));
    }

    public static void FragmentFraming()
    {
        for (byte type = 8; type <= 15; type++)
        {
            ulong offset = (type & 4) != 0 ? 64UL : 0;
            var fragment = new WireFragment(type, 0x32, offset, Hex("deadbeef"));
            var decoded = (WireFragment)WirePacket.Parse(Packet(1, fragment)).Contents.Single();
            Equal(type, decoded.Type); Equal(offset, decoded.Offset); Equal(3, decoded.Channel);
            Equal((type & 2) != 0, decoded.IsFinal); Bytes(fragment.Data.Span, decoded.Data.Span);
        }
        var implicitLength = new WireFragment(0x0e, 0x1c, 0, Hex("020304"));
        Equal(1, WirePacket.Parse(Packet(1, implicitLength)).Contents.Count);
        Reject(() => Packet(1, implicitLength, Control()));
    }

    public static void FragmentFramingFailures()
    {
        foreach (string body in new[] { "0f", "0f1c", "0f1c00", "0f1c000201", "0f1c0040", "0f1cffffffffffffffff01ff" })
            Reject(() => WirePacket.Parse(Raw(body)));
        Reject(() => Packet(1, Fragment(WireReader.MaxCompact, new byte[1])));
        Reject(() => Packet(1, new WireFragment(9, 0x1c, 1, new byte[1])));
    }

    public static void DescriptorFixtures()
    {
        byte[] exact = Raw("0f4042404004deadbeef");
        Bytes(exact, Packet(1, new WireFragment(0x0f, 0x42, 64, Hex("deadbeef"))));
        var fragment = (WireFragment)WirePacket.Parse(exact).Contents.Single();
        Equal((byte)0x42, fragment.Descriptor); Equal(4, fragment.Channel); Equal(64UL, fragment.Offset);
        Check(!fragment.IsReliable); Bytes(Hex("deadbeef"), fragment.Data.Span);
        Equal((byte)255, ((WireFragment)WirePacket.Parse(Raw("0f40ff0001aa")).Contents.Single()).Descriptor);
        foreach (string bytes in new[] { "0f40", "0f41000001aa", "0fc0000001000000000001aa" })
            Reject(() => WirePacket.Parse(Raw(bytes)));
    }

    public static void AckFixture()
    {
        byte[] exact = Raw("020a00020201010000");
        var ack = (WireAcknowledgment)WirePacket.Parse(exact).Contents.Single();
        Check(ack.Ranges.SequenceEqual(new[] { new WireAckRange(8, 10), new WireAckRange(4, 5), new WireAckRange(2, 2) }));
        Bytes(exact, WirePacket.Parse(exact).Encode());
        var maxDelay = new WireAcknowledgment([new(0, 0)], uint.MaxValue);
        Equal(uint.MaxValue, ((WireAcknowledgment)WirePacket.Parse(Packet(1, maxDelay)).Contents.Single()).Delay);
    }

    public static void AckFailures()
    {
        foreach (string body in new[] { "02", "0201000002", "02030001000300", "02050001000004", "020a00404000", "020a000100", "020ac0000001000000000000" })
            Reject(() => WirePacket.Parse(Raw(body)));
        Reject(() => Packet(1, Ack()));
        Reject(() => Packet(1, Ack(new WireAckRange(2, 1))));
        Reject(() => Packet(1, Ack(new(8, 10), new(7, 7))));
        Reject(() => Packet(1, Ack(new(8, 10), new(9, 11))));
        Reject(() => WirePacket.Parse(Raw("020a0001020000"), new WireLimits { MaxAckRanges = 1 }));
    }

    public static void SelectiveAck()
    {
        var clock = new ManualTime(); var connection = Bound(clock: clock);
        for (int i = 0; i < 10; i++) connection.SendContent(Control());
        Equal(10, connection.RetainedPacketCount);
        var result = connection.Receive(Raw("020a00020201010000"));
        Check(result.AcknowledgedSequences.SequenceEqual(new ulong[] { 2, 4, 5, 8, 9, 10 }));
        Equal(4, connection.RetainedPacketCount); Equal(0, result.Outgoing.Count);
        clock.Advance(TimeSpan.FromSeconds(1));
        Check(connection.Tick().Select(d => d.Sequence).SequenceEqual(new ulong[] { 1, 3, 6, 7 }));
    }

    public static void UnsentAck()
    {
        var connection = Bound(); connection.SendContent(Control());
        Reject(() => connection.Receive(Packet(1, Ack(new WireAckRange(1, 2)))));
        Equal(1, connection.RetainedPacketCount);
        Reject(() => connection.Receive(Packet(1, Ack(new WireAckRange(1, 1)), Ack(new WireAckRange(9, 9)))));
        Equal(1, connection.RetainedPacketCount);
        var result = connection.Receive(Packet(1, Ack(new WireAckRange(1, 1))));
        Equal(0, connection.RetainedPacketCount); Equal(1UL, result.AcknowledgedSequences.Single());
        Reject(() => Bound().Receive(Packet(1, Ack(new WireAckRange(0, 1)))));
    }

    public static void AtomicReceive()
    {
        var connection = Bound(); connection.SendContent(Control());
        Reject(() => connection.Receive(Packet(1, Ack(new WireAckRange(1, 1)), Fragment(0, Hex("00")))));
        Equal(1, connection.RetainedPacketCount); Equal(0UL, connection.GetIncomingOffset()); Equal(2UL, connection.NextSequence);
        byte[] bytes = App().Encode();
        Reject(() => connection.Receive(Packet(1, Fragment(0, bytes.AsMemory(0, 5), false), Fragment(3, Hex("0102"), false))));
        Equal(0, connection.BufferedBytes); Equal(0, connection.BufferedFragmentCount);
        var result = connection.Receive(Packet(1, Ack(new WireAckRange(1, 1)), Fragment(0, bytes)));
        Equal(1, result.Applications.Count); Equal(0, connection.RetainedPacketCount);
        var unbound = new WireConnection();
        Reject(() => unbound.Receive(Extended(1, Fragment(0, Hex("01")))));
        Check(!unbound.IsBound);
    }

    public static void AckHistory()
    {
        var connection = Bound();
        connection.Receive(Packet(10, Control())); connection.Receive(Packet(8, Control()));
        var ack = EmittedAck(connection.Receive(Packet(9, Control())));
        Check(ack.Ranges.SequenceEqual(new[] { new WireAckRange(8, 10), new WireAckRange(0, 0) }));
        connection = Bound(new WireLimits { MaxAckRanges = 2 });
        foreach (ulong sequence in new ulong[] { 2, 4, 6, 8 }) connection.Receive(Packet(sequence, Control()));
        ack = (WireAcknowledgment)WirePacket.Parse(connection.CreateAcknowledgment()!.Bytes).Contents.Single();
        Check(ack.Ranges.SequenceEqual(new[] { new WireAckRange(8, 8), new WireAckRange(6, 6) }));
    }

    public static void EmptyHistory()
    {
        var connection = new WireConnection(); Check(connection.CreateAcknowledgment() is null);
        Equal(0, connection.Tick().Count); Equal(1UL, connection.NextSequence);
        Reject(() => connection.SendContent(Control()));
        connection.Receive(Extended(0, Control()));
        Equal(new WireAckRange(0, 0), ((WireAcknowledgment)WirePacket.Parse(connection.CreateAcknowledgment()!.Bytes).Contents.Single()).Ranges.Single());
    }

    public static void PacketDuplicates()
    {
        var connection = Bound(); byte[] packet = Packet(1, Control());
        Equal(1, connection.Receive(packet).Contents.Count);
        var duplicate = connection.Receive(packet);
        Check(duplicate.IsDuplicate); Equal(0, duplicate.Contents.Count); Equal(0, duplicate.Applications.Count); Equal(1, duplicate.Outgoing.Count);
        Equal(0, connection.RetainedPacketCount);
    }

    public static void InitialAckSentinel()
    {

        var connection = new WireConnection();
        byte[] probe = Extended(124, Ack(new WireAckRange(0, 0)), Control(), new WirePadding(1200));
        var result = connection.Receive(probe);
        Equal(0, result.AcknowledgedSequences.Count); Equal(0, connection.RetainedPacketCount);
        var reply = WirePacket.Parse(result.Outgoing.Single().Bytes);
        Check(reply.IsExtended); Equal(Server, reply.FirstId); Equal(Client, reply.SecondId);
        Equal(new WireAckRange(124, 124), ((WireAcknowledgment)reply.Contents.Single()).Ranges.Single());
        Check(WirePacket.Parse(connection.Receive(probe).Outgoing.Single().Bytes).IsExtended);
        var untouched = new WireConnection();
        Reject(() => untouched.Receive(Extended(124, Ack(new WireAckRange(1, 1)), Control())));
        Reject(() => untouched.Receive(Extended(124, Ack(new WireAckRange(0, 1)), Control())));
        Check(!untouched.IsBound); Equal(1UL, untouched.NextSequence);
        var empty = untouched.Receive(Extended(124, Ack(new WireAckRange(0, 0))));
        Equal(0, empty.Outgoing.Count); Equal(0, empty.AcknowledgedSequences.Count); Equal(1UL, untouched.NextSequence);
    }

    public static void ConflictingPackets()
    {
        var connection = Bound(); connection.Receive(Packet(1, Control()));
        Reject(() => connection.Receive(Packet(1, new WireControl(4, []))));
        Equal(2UL, connection.NextSequence);
        Check(connection.Receive(Packet(1, Control())).IsDuplicate);
    }

    public static void ReceiveWindow()
    {
        var connection = Bound(new WireLimits { MaxReceivedPackets = 2 });
        foreach (ulong sequence in new ulong[] { 1, 2, 3 }) connection.Receive(Packet(sequence, Control()));
        var old = connection.Receive(Packet(1, new WireControl(4, [])));
        Check(old.IsDuplicate); Equal(0, old.Contents.Count);
        Check(EmittedAck(old).Ranges.SequenceEqual(new[] { new WireAckRange(2, 3) }));
    }

    public static void AckOnly()
    {
        var connection = Bound(); connection.SendContent(Control());
        byte[] packet = Packet(1, Ack(new WireAckRange(1, 1)));
        Equal(0, connection.Receive(packet).Outgoing.Count);
        Equal(0, connection.Receive(packet).Outgoing.Count);
        Equal(0, connection.Tick().Count); Equal(0, connection.RetainedPacketCount);
    }

    public static void ApplicationFixture()
    {
        byte[] bytes = Hex("0307100000000c00000000000153010001000150002a02");
        var application = WireApplication.Parse(bytes);
        Equal((byte)3, application.Family); Equal((byte)7, application.Operation); Equal((byte)0x10, application.Qualifier);
        Equal(12, application.Body.Length); Bytes(bytes, application.Encode());
        var body = new WireReader(application.Body);
        Bytes(Hex("53"), body.ReadBytes16().Span); Equal((byte)1, body.ReadByte()); Equal((ushort)1, body.ReadUInt16());
        Bytes(Hex("50"), body.ReadBytes16().Span); Equal((ushort)42, body.ReadUInt16()); Equal((byte)2, body.ReadByte()); body.RequireEnd();
        Bytes(Hex("0101011234000589abcdef0001020304"), App().Encode());
    }

    public static void ApplicationFailures()
    {
        byte[] bytes = App().Encode();
        for (int length = 0; length < bytes.Length; length++)
        {
            int cut = length; Reject(() => WireApplication.Parse(bytes.AsMemory(0, cut)));
        }
        byte[] two = bytes.Concat(App(0).Encode()).ToArray();
        Equal(2, WireApplication.ParseMany(two).Count);
        Reject(() => WireApplication.Parse(two));
        Reject(() => WireApplication.ParseMany(two, new WireLimits { MaxApplicationRecords = 1 }));
        Reject(() => WireApplication.ParseMany(two, new WireLimits { MaxBufferedBytes = 10 }));
        Reject(() => App(65536).Encode());
        Equal(65535, WireApplication.Parse(App(65535).Encode()).Body.Length);
    }

    public static void FinalFirst()
    {
        var connection = Bound(); byte[] bytes = App().Encode();
        Equal(0, connection.Receive(Packet(3, Fragment(11, bytes.AsMemory(11)))).Applications.Count);
        Equal(0, connection.Receive(Packet(1, Fragment(0, bytes.AsMemory(0, 6), false))).Applications.Count);
        Equal(2, connection.BufferedFragmentCount); Equal(0UL, connection.GetIncomingOffset());
        var result = connection.Receive(Packet(2, Fragment(6, bytes.AsMemory(6, 5), false)));
        Bytes(bytes, result.Applications.Single().Encode()); Equal((byte)0x1c, result.Applications.Single().Descriptor);
        Equal((ulong)bytes.Length, connection.GetIncomingOffset()); Equal(0, connection.BufferedBytes);
    }

    public static void AdjacentMessages()
    {
        var connection = Bound(); byte[] first = App().Encode(); byte[] second = App(2).Encode();
        connection.Receive(Packet(1, Fragment(0, first.AsMemory(0, 8), false)));
        connection.Receive(Packet(2, Fragment(8, first.AsMemory(8, 7), false)));
        Equal(0UL, connection.GetIncomingOffset()); Equal(2, connection.BufferedFragmentCount);
        connection.Receive(Packet(4, Fragment((ulong)first.Length, second)));
        var result = connection.Receive(Packet(3, Fragment(15, first.AsMemory(15))));
        Equal(2, result.Applications.Count); Bytes(first, result.Applications[0].Encode()); Bytes(second, result.Applications[1].Encode());
        Equal((ulong)(first.Length + second.Length), connection.GetIncomingOffset());
        connection = Bound();
        result = connection.Receive(Packet(1, Fragment(0, first.Concat(second).ToArray())));
        Equal(2, result.Applications.Count);
    }

    public static void FragmentDuplicates()
    {
        var connection = Bound(); byte[] bytes = App().Encode();
        var tail = Fragment(8, bytes.AsMemory(8));
        connection.Receive(Packet(1, tail)); connection.Receive(Packet(2, tail));
        Equal(1, connection.BufferedFragmentCount); Equal(bytes.Length - 8, connection.BufferedBytes);
        Equal(1, connection.Receive(Packet(3, Fragment(0, bytes.AsMemory(0, 8), false))).Applications.Count);
        Equal(0, connection.Receive(Packet(4, tail)).Applications.Count);
        Equal((ulong)bytes.Length, connection.GetIncomingOffset());
    }

    public static void FragmentConflicts()
    {
        var connection = Bound(); byte[] bytes = App().Encode();
        connection.Receive(Packet(1, Fragment(8, bytes.AsMemory(8))));
        Reject(() => connection.Receive(Packet(2, Fragment(8, Hex("0102")))));
        Reject(() => connection.Receive(Packet(2, Fragment(8, bytes.AsMemory(8), false))));
        Reject(() => connection.Receive(Packet(2, Fragment(7, new byte[2], false))));
        Reject(() => connection.Receive(Packet(2, Fragment(10, new byte[2], false))));
        Equal(1, connection.BufferedFragmentCount);
        connection.Receive(Packet(2, Fragment(0, bytes.AsMemory(0, 8), false)));
        Reject(() => connection.Receive(Packet(3, Fragment(0, new byte[8], false))));
        Reject(() => connection.Receive(Packet(3, Fragment(15, new byte[2]))));
        connection = Bound();
        connection.Receive(Packet(1, Fragment(0, bytes.AsMemory(0, 8), false)));
        Reject(() => connection.Receive(Packet(2, Fragment(8, bytes.AsMemory(8), true, 0x12))));
    }

    public static void MalformedAssembly()
    {
        var connection = Bound(); byte[] bytes = App().Encode();
        connection.Receive(Packet(1, Fragment(0, bytes.AsMemory(0, 8), false)));
        Reject(() => connection.Receive(Packet(2, Fragment(8, bytes.AsMemory(8, 1)))));
        Equal(1, connection.BufferedFragmentCount); Equal(8, connection.BufferedBytes); Equal(0UL, connection.GetIncomingOffset());
        Equal(1, connection.Receive(Packet(2, Fragment(8, bytes.AsMemory(8)))).Applications.Count);
    }

    public static void FragmentCap()
    {
        var connection = Bound();
        for (ulong i = 1; i <= 64; i++) connection.Receive(Packet(i, Fragment(i, new byte[1], false)));
        Equal(64, connection.BufferedFragmentCount);
        Reject(() => connection.Receive(Packet(65, Fragment(65, new byte[1], false))));
        Equal(64, connection.BufferedFragmentCount); Equal(64, connection.BufferedBytes);
        connection.Receive(Packet(65, Fragment(64, new byte[1], false)));
        Equal(64, connection.BufferedFragmentCount);
        try { _ = new WireConnection(limits: new WireLimits { MaxFragments = 65 }); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException("Native fragment cap may not be raised above 64");
    }

    public static void FragmentByteCap()
    {
        var connection = Bound(new WireLimits { MaxBufferedBytes = 16 });
        connection.Receive(Packet(1, Fragment(100, new byte[8], false)));
        connection.Receive(Packet(2, Fragment(108, new byte[8], false)));
        Reject(() => connection.Receive(Packet(3, Fragment(116, new byte[1], false))));
        Equal(16, connection.BufferedBytes); Equal(2, connection.BufferedFragmentCount);
        Reject(() => Bound(new WireLimits { MaxBufferedBytes = 8 }).Receive(Packet(1, Fragment(10, new byte[9], false))));
    }

    public static void OtherChannels()
    {
        foreach (byte descriptor in new byte[] { 0x22, 0x32, 0x42 })
        {
            var connection = Bound(); byte[] bytes = App().Encode();
            var first = connection.Receive(Packet(1, Fragment(0, bytes, true, descriptor)));
            Equal(descriptor, first.Applications.Single().Descriptor);
            Equal(1, connection.Receive(Packet(2, Fragment((ulong)bytes.Length, bytes, true, descriptor))).Applications.Count);
            Equal(0, connection.Receive(Packet(3, Fragment(0, bytes, true, descriptor))).Applications.Count);
            Equal(0, connection.BufferedFragmentCount);
            Equal(2UL * (ulong)bytes.Length, connection.GetIncomingOffset(descriptor >> 4));
        }
    }

    public static void UnsupportedChannels()
    {
        byte[] bytes = App().Encode();
        foreach (byte descriptor in new byte[] { 0, 0x52, 0xf2 })
            Reject(() => Bound().Receive(Packet(1, Fragment(0, bytes, true, descriptor))));
        foreach (byte descriptor in new byte[] { 0x22, 0x32, 0x42 })
        {
            Reject(() => Bound().Receive(Packet(1, Fragment(0, bytes, false, descriptor))));
        }
        Reject(() => Bound().Receive(Packet(1, Fragment(1, bytes, true, 0x22))));
        Reject(() => Bound().Receive(Packet(1, Fragment(0, bytes, false, 0x12))));
        Reject(() => Bound().Receive(Packet(1, Fragment(0, Array.Empty<byte>()))));
    }

    public static void UnorderedDelivery()
    {
        foreach (byte descriptor in new byte[] { 0x32, 0x42 })
        {
            var connection = Bound(); byte[] bytes = App().Encode();
            Equal(1, connection.Receive(Packet(3, Fragment(32, bytes, true, descriptor))).Applications.Count);
            Equal(1, connection.Receive(Packet(1, Fragment(0, bytes, true, descriptor))).Applications.Count);

            Equal(1, connection.Receive(Packet(4, Fragment(48, bytes, true, descriptor))).Applications.Count);
            Equal(64UL, connection.GetIncomingOffset(descriptor >> 4));
            Equal(1, connection.Receive(Packet(2, Fragment(16, bytes, true, descriptor))).Applications.Count);
            Equal(64UL, connection.GetIncomingOffset(descriptor >> 4));
            Equal(0, connection.BufferedFragmentCount); Equal(0, connection.BufferedBytes);
            Equal(0, connection.Receive(Packet(5, Fragment(32, bytes, true, descriptor))).Applications.Count);
            Reject(() => connection.Receive(Packet(6, Fragment(32, App(4).Encode(), true, descriptor))));
            Reject(() => connection.Receive(Packet(6, Fragment(40, bytes, true, descriptor))));
            Reject(() => connection.Receive(Packet(6, Fragment(100, new byte[1], true, descriptor))));
            Equal(1, connection.Receive(Packet(6, Fragment(100, bytes, true, descriptor))).Applications.Count);
        }
    }

    public static void UnorderedBounds()
    {
        foreach (byte descriptor in new byte[] { 0x32, 0x42 })
        {
            var connection = Bound(new WireLimits { MaxFragments = 2, MaxBufferedBytes = 32 });
            byte[] bytes = App().Encode();
            for (ulong i = 0; i < 100; i++)
            {
                Equal(1, connection.Receive(Packet(i + 1, Fragment(i * 32, bytes, true, descriptor))).Applications.Count);
                Check(connection.RememberedFragmentCount <= 2); Check(connection.RememberedFragmentBytes <= 32);
            }
            Equal(0, connection.Receive(Packet(101, Fragment(0, bytes, true, descriptor))).Applications.Count);
            Equal(0, connection.BufferedBytes);

            Reject(() => connection.Receive(Packet(102, Fragment(3110, bytes, true, descriptor))));
            connection = Bound(new WireLimits { MaxBufferedBytes = 16 });
            for (ulong i = 0; i < 5; i++) connection.Receive(Packet(i + 1, Fragment(i * 32, bytes, true, descriptor)));
            Equal(1, connection.RememberedFragmentCount); Equal(16L, connection.RememberedFragmentBytes);
        }
    }

    public static void SendFragments()
    {
        var connection = Bound(); var application = App(2700);
        var datagrams = connection.SendApplication(application);
        Equal(3, datagrams.Count); Equal(3, connection.RetainedPacketCount);
        var fragments = datagrams.Select(d => (WireFragment)WirePacket.Parse(d.Bytes).Contents.Single()).ToArray();
        Check(fragments.Select(f => f.Data.Length).SequenceEqual(new[] { 1311, 1300, 100 }));
        Check(fragments.Select(f => f.Offset).SequenceEqual(new ulong[] { 0, 1311, 2611 }));
        Check(fragments.Select(f => f.Type).SequenceEqual(new byte[] { 0x0d, 0x0d, 0x0f }));
        Bytes(application.Encode(), fragments.SelectMany(f => f.Data.ToArray()).ToArray());
        Equal(2711UL, connection.GetOutgoingOffset());
        var next = connection.SendApplication(App(0)).Single();
        Equal(2711UL, ((WireFragment)WirePacket.Parse(next.Bytes).Contents.Single()).Offset);
        Equal(2722UL, connection.GetOutgoingOffset());
    }

    public static void IndependentConnections()
    {
        var first = Bound(); var second = Bound();
        first.SendApplication(App()); first.SendApplication(App());
        Equal(32UL, first.GetOutgoingOffset()); Equal(0UL, second.GetOutgoingOffset());
        Equal(1UL, second.NextSequence);
        var outbound = (WireFragment)WirePacket.Parse(first.SendApplication(App(), 0x32).Single().Bytes).Contents.Single();
        Equal(0UL, outbound.Offset); Equal((byte)0x32, outbound.Descriptor);
        first.SendApplication(App(2), 0x42);
        Equal(16UL, first.GetOutgoingOffset(3)); Equal(13UL, first.GetOutgoingOffset(4)); Equal(32UL, first.GetOutgoingOffset(1));
    }

    public static void Retransmission()
    {
        var clock = new ManualTime(); var connection = Bound(clock: clock);
        var sent = connection.SendApplication(App(), packetClass: 0x81).Single();
        ulong sequence = connection.NextSequence, offset = connection.GetOutgoingOffset();
        clock.Advance(TimeSpan.FromMilliseconds(999)); Equal(0, connection.Tick().Count);
        clock.Advance(TimeSpan.FromMilliseconds(1)); var retry = connection.Tick().Single();
        Equal(2, retry.Attempt); Check(retry.IsRetransmission); Equal(sent.Sequence, retry.Sequence); Bytes(sent.Bytes.Span, retry.Bytes.Span);
        Equal(sequence, connection.NextSequence); Equal(offset, connection.GetOutgoingOffset()); Equal(0, connection.Tick().Count);
        clock.Advance(TimeSpan.FromSeconds(1)); Equal(3, connection.Tick().Single().Attempt);
        connection.Receive(Packet(1, Ack(new WireAckRange(sent.Sequence, sent.Sequence))));
        clock.Advance(TimeSpan.FromDays(1)); Equal(0, connection.Tick().Count); Equal(0, connection.RetainedBytes);
    }

    public static void UnreliableSends()
    {
        foreach (byte descriptor in new byte[] { 0x12, 0x22, 0x32, 0x42 })
        {
            var clock = new ManualTime(); var connection = Bound(new WireLimits { MaxPacketBytes = 4096 }, clock);
            var sent = connection.SendApplication(App(2000), descriptor, fragmentBodyBytes: 1).Single();
            var fragment = (WireFragment)WirePacket.Parse(sent.Bytes).Contents.Single();
            Check(fragment.IsFinal); Check(!fragment.IsReliable); Equal(2011, fragment.Data.Length);
            Equal(2011UL, connection.GetOutgoingOffset(descriptor >> 4));
            Equal(0, connection.RetainedPacketCount); Equal(0, connection.RetainedBytes);
            clock.Advance(TimeSpan.FromDays(1)); Equal(0, connection.Tick().Count);
            var next = connection.SendApplication(App(0), descriptor).Single();
            Equal(2011UL, ((WireFragment)WirePacket.Parse(next.Bytes).Contents.Single()).Offset);
        }
        var reliable = Bound(); Equal(2, reliable.SendApplication(App(2000), 0x1c).Count); Equal(2, reliable.RetainedPacketCount);

        var full = Bound(new WireLimits { MaxRetainedPackets = 1 });
        full.SendApplication(App()); full.SendApplication(App(), 0x42); Equal(1, full.RetainedPacketCount);
    }

    public static void SendBounds()
    {
        var connection = Bound(new WireLimits { MaxRetainedPackets = 1 });
        Reject(() => connection.SendApplication(App(2700)));
        Equal(0, connection.RetainedPacketCount); Equal(1UL, connection.NextSequence); Equal(0UL, connection.GetOutgoingOffset());
        connection.SendContent(Control()); Reject(() => connection.SendContent(Control())); Equal(1, connection.RetainedPacketCount);
        connection = Bound(new WireLimits { MaxRetainedBytes = 8 });
        Reject(() => connection.SendApplication(App())); Equal(0, connection.RetainedBytes); Equal(0UL, connection.GetOutgoingOffset());
        connection = Bound(new WireLimits { MaxPacketBytes = 32 });
        Reject(() => connection.SendApplication(App(100))); Equal(1UL, connection.NextSequence);
        connection = Bound();
        Reject(() => connection.SendApplication(App(65), fragmentBodyBytes: 1));
        Reject(() => connection.SendApplication(App(2000), 0x3a));
        Reject(() => connection.SendApplication(App(), 0x52));
        Reject(() => connection.SendContent(Fragment(0, App().Encode())));
        Equal(1UL, connection.NextSequence); Equal(0UL, connection.GetOutgoingOffset());
        var smallDatagram = Bound(new WireLimits { MaxPacketBytes = 1400 });
        Reject(() => smallDatagram.SendApplication(App(2000), 0x42));
        Equal(1UL, smallDatagram.NextSequence); Equal(0UL, smallDatagram.GetOutgoingOffset(4));

        connection = new WireConnection(limits: new WireLimits { MaxPacketBytes = 11 });
        byte[] tiny = new WirePacket(0x80, "aa", "bb", 0, [Control()]).Encode();
        Reject(() => connection.Receive(tiny)); Check(!connection.IsBound);
    }

    public static void Callback()
    {
        var clock = new ManualTime(); var queue = new List<byte[]>();
        var connection = Bound(clock: clock, send: bytes => queue.Add(bytes.ToArray()));
        Equal(0, queue.Count);
        var receive = connection.Receive(Packet(1, Control())); Equal(1, queue.Count); Bytes(receive.Outgoing.Single().Bytes.Span, queue[0]);
        var send = connection.SendContent(0x12, Hex("0101")).Single(); Equal(2, queue.Count); Bytes(send.Bytes.Span, queue[1]);
        clock.Advance(TimeSpan.FromSeconds(1)); var retries = connection.Tick(); Equal(3, queue.Count); Bytes(retries.Single().Bytes.Span, queue[2]);
        connection.Receive(Packet(2, Ack(new WireAckRange(send.Sequence, send.Sequence)))); Equal(3, queue.Count);
        clock.Advance(TimeSpan.FromSeconds(1)); Equal(0, connection.Tick().Count);
    }

    public static void EndpointExchange()
    {
        var clock = new ManualTime(); var server = Bound(clock: clock); var client = new WireConnection();
        client.Receive(new WirePacket(0x80, Server, Client, 0, []).Encode());
        var sent = server.SendApplication(App(2700));
        var finalFirst = client.Receive(sent[2].Bytes); Equal(0, finalFirst.Applications.Count);
        server.Receive(finalFirst.Outgoing.Single().Bytes); Equal(2, server.RetainedPacketCount);
        var head = client.Receive(sent[0].Bytes); server.Receive(head.Outgoing.Single().Bytes);
        Equal(1, server.RetainedPacketCount);
        clock.Advance(TimeSpan.FromSeconds(1)); var retry = server.Tick().Single(); Equal(sent[1].Sequence, retry.Sequence);
        var delivery = client.Receive(retry.Bytes); Bytes(App(2700).Encode(), delivery.Applications.Single().Encode());
        server.Receive(delivery.Outgoing.Single().Bytes); Equal(0, server.RetainedPacketCount);
        var duplicate = client.Receive(sent[2].Bytes); Check(duplicate.IsDuplicate); Equal(0, duplicate.Applications.Count);
        server.Receive(duplicate.Outgoing.Single().Bytes);
    }

    public static void RoutingEnvelopes()
    {
        byte[] exact = Hex("01112233445566778800040000deadbeef");
        var nested = WireNestedPayload.Parse(exact);
        Equal(0x11223344U, nested.SenderNonce); Equal(0x55667788U, nested.DestinationNonce); Bytes(exact, nested.Encode());
        var outbound = new WireOutboundPayload([42, 43], 2, exact);
        var parsed = WireOutboundPayload.Parse(outbound.Encode()); Check(parsed.Destinations.SequenceEqual(new ushort[] { 42, 43 }));
        var inbound = new WireInboundPayload(Hex("53455353"), 7, parsed.BlockTag, parsed.Payload);
        var received = WireInboundPayload.Parse(inbound.Encode()); Equal((ushort)7, received.Sender); Bytes(exact, received.Payload.Span);

        static bool Matches(WireNestedPayload block, uint local, uint remote) => block.SenderNonce == remote && block.DestinationNonce == local;
        Check(Matches(WireNestedPayload.Parse(received.Payload), 0x55667788, 0x11223344));
        Check(!Matches(nested with { SenderNonce = nested.DestinationNonce, DestinationNonce = nested.SenderNonce }, 0x55667788, 0x11223344));
        Check(!Matches(nested, 0x55667788, 0x11223345));
        var result = Bound().Receive(Packet(1, Fragment(0, new WireApplication(4, 1, 0x10, 0, 0, outbound.Encode()).Encode(), true, 0x32)));
        Equal((byte)4, result.Applications.Single().Family); Equal((byte)0x32, result.Applications.Single().Descriptor);
    }

    public static void SecondaryPayload()
    {
        var nested = new WireNestedPayload(1, 2, Hex("abcd"), 0x1234, Hex("eeff00"));
        Bytes(Hex("01000000010000000200020003abcd1234eeff00"), nested.Encode());
        var parsed = WireNestedPayload.Parse(nested.Encode()); Equal((ushort?)0x1234, parsed.SecondaryMetadata); Bytes(nested.Secondary.Span, parsed.Secondary.Span);
        var opaque = new WireInboundPayload(new byte[0], 9, 1, Hex("ff000102"));
        var received = WireInboundPayload.Parse(opaque.Encode()); Equal((byte)1, received.BlockTag); Bytes(opaque.Payload.Span, received.Payload.Span);
    }

    public static void EnvelopeFailures()
    {
        byte[] nested = Hex("01112233445566778800040000deadbeef");
        byte[] outbound = new WireOutboundPayload([42], 2, nested).Encode();
        byte[] inbound = new WireInboundPayload(Hex("53"), 7, 2, nested).Encode();
        for (int length = 0; length < nested.Length; length++)
        {
            int cut = length; Reject(() => WireNestedPayload.Parse(nested.AsMemory(0, cut)));
        }
        for (int length = 0; length < outbound.Length; length++)
        {
            int cut = length; Reject(() => WireOutboundPayload.Parse(outbound.AsMemory(0, cut)));
        }
        for (int length = 0; length < inbound.Length; length++)
        {
            int cut = length; Reject(() => WireInboundPayload.Parse(inbound.AsMemory(0, cut)));
        }
        Reject(() => WireOutboundPayload.Parse(Hex("00000000")));
        Reject(() => WireOutboundPayload.Parse(Hex("ffff")));
        Reject(() => WireInboundPayload.Parse(Hex("000000010000")));
        Reject(() => WireNestedPayload.Parse(nested.Concat(new byte[1]).ToArray()));
        nested[0] = 2; Reject(() => WireNestedPayload.Parse(nested));
        Reject(() => new WireNestedPayload(1, 2, new byte[65536], null, new byte[0]).Encode());
        Reject(() => new WireNestedPayload(1, 2, new byte[0], null, new byte[1]).Encode());
        Reject(() => new WireNestedPayload(1, 2, new byte[0], 1, new byte[0]).Encode());
        Reject(() => new WireOutboundPayload([1], 2, new byte[65535]).Encode());
        Reject(() => new WireInboundPayload(new byte[65536], 1, 2, new byte[0]).Encode());
    }

    public static void MalformedCorpus()
    {
        var random = new Random(0x14227);
        var limits = new WireLimits { MaxPacketBytes = 256, MaxBufferedBytes = 256, MaxAckRanges = 8, MaxContentItems = 32 };
        for (int i = 0; i < 1000; i++)
        {
            byte[] bytes = new byte[random.Next(257)]; random.NextBytes(bytes);


            try { var parsed = WirePacket.Parse(bytes, limits); _ = WirePacket.Parse(parsed.Encode(limits), limits); }
            catch (WireFormatException) { }
            var connection = new WireConnection(limits: limits);
            try { connection.Receive(bytes); }
            catch (WireFormatException) { Check(!connection.IsBound); Equal(0, connection.BufferedBytes); }
        }
    }
}

internal sealed class ManualTime : TimeProvider
{
    private long _timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
}
