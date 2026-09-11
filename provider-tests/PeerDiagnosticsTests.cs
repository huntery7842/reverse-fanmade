using System.Buffers.Binary;
using System.Text.Json;
using ReVerse.Capture.Signaling;

namespace Provider.Tests;

internal static class PeerDiagnosticsTests
{

    internal static byte[] Encode(byte[] body)
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var value = i;
            for (var b = 0; b < 8; b++) value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320u);
            table[i] = value;
        }
        byte[] plain = [0, 0, 0, 0, .. body];
        uint crc = uint.MaxValue;
        foreach (var b in plain) crc = table[(crc ^ b) & 255] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(plain, crc);
        byte previous = 0x25;
        for (var i = 0; i < plain.Length; i++) { plain[i] ^= previous; previous = plain[i]; }
        return plain;
    }

    internal static byte[] Block(byte[] primary) =>
        [2, 1, .. Bytes.U32(0x01020304), .. Bytes.U32(0xaabbccdd), .. Bytes.U16((ushort)primary.Length),
            0, 3, .. primary, 0xbe, 0xef, 0x10, 0, 0xfe];

    private static PeerPrimaryAudit Inspect(byte[] body) => PeerPrimaryDiagnostics.Inspect(Encode(body), 0x01020304, 0xaabbccdd);

    internal static void Decode()
    {
        Assert.Bytes(Convert.FromHexString("FB406083"), Encode([]), "known independent empty vector");
        Assert.That(PeerPrimaryDiagnostics.Inspect(Convert.FromHexString("8033AECBCAC8DE"), 0, 0)
            is { CrcValid: true, ControlSubtype: 0x16, Shape: "noncanonical_length" }, "known incomplete fixture");
        (byte Type, int Length)[] shapes = [(1, 11), (2, 8), (3, 5), (4, 4), (5, 3), (0x11, 7), (0x12, 7),
            (0x13, 11), (0x14, 15), (0x15, 7), (0x16, 16), (0x17, 9), (0x18, 9), (0x19, 8), (0x1a, 8), (0x1b, 7), (0x1c, 7)];
        foreach (var (type, length) in shapes)
        {
            byte[] body = [1, 2, type, .. new byte[length - 3]];
            Assert.That(Inspect(body) is { CrcValid: true, Shape: "canonical", Kind: "control" }, "known control shape");
            for (var size = 3; size < length; size++)
                Assert.That(Inspect(body[..size]).Shape == "noncanonical_length", "truncated control accepted as canonical");
            Assert.That(Inspect([.. body, 0]).Shape == "noncanonical_length", "trailing control bytes ignored");
        }
        Assert.That(Inspect([1, 2, 2, 0xa7, 1, 2, 3, 4]) is { ProtocolByte: 0xa7, SenderNonceMatchesEnvelope: true }, "reply endian/nonce check");
        Assert.That(Inspect([1, 2, 2, 0xa7, 4, 3, 2, 1]).SenderNonceMatchesEnvelope == false, "nonce mismatch hidden");
        Assert.That(Inspect([1, 2, 3, 0xff, 3]).AdvertisesSecondary == true
            && Inspect([1, 2, 0x17, 1, 2, 3, 4, 0xff, 2]).AdvertisesSecondary == false, "secondary flag bit decoding");
        foreach (var type in new byte[] { 0, 6, 0x10, 0x1d, 255 })
            Assert.That(Inspect([1, 2, type, 0xaa]).Shape == "unknown_control", "unknown subtype interpreted");
        var wire = Encode([1, 2, 2, 0xa7, 1, 2, 3, 4]);
        var before = wire.ToArray();
        PeerPrimaryDiagnostics.Inspect(wire, 0, 0);
        Assert.Bytes(before, wire, "decoder changed caller bytes");
        for (var i = 0; i < wire.Length; i++)
            Assert.That(PeerPrimaryDiagnostics.Inspect(Bytes.Change(wire, i, (byte)(wire[i] ^ 1)), 0, 0).CrcValid == false, "corruption not detected");
        Assert.That(PeerPrimaryDiagnostics.Inspect([], 0, 0).Shape == "primary_too_short", "short primary");
        Assert.That(PeerPrimaryDiagnostics.Inspect(new byte[65536], 0, 0).Shape == "primary_over_limit", "large primary");
    }

    internal static void Streams()
    {
        byte[] header = [2, 1, 1, 2, 3, 4, 0xaa, 0xbb, 0xcc, 0xdd, 0xab, 0xcd];
        var ready = Inspect([.. header, 0, 1, 0, 1, 1]);
        Assert.That(ready is { Kind: "control_stream", Shape: "canonical", ReadyRecordCount: 1,
            SenderNonceMatchesEnvelope: true, DestinationNonceMatchesEnvelope: true, AcknowledgedSequence: 0xabcd }, "ready stream scalars");
        Assert.That(ready.Records[0] is { Sequence: 1, Length: 1, Kind: "ready_record_observed" }, "ready record missing");
        header[0] = 3;
        var index = Inspect([.. header, 0xff, 0xff, 0, 3, 1, 2, 0xff, 0, 0, 0, 2, 1, 3]);
        Assert.That(index.IndexRecordCount == 2 && index.ReadyRecordCount == 0 && index.Records[0].Index == -1
            && index.Records[1].Sequence == 0, "index classification/signed index/wrap");
        Assert.That(Inspect([.. header, 0, 1, 0, 1, 1]).ReadyRecordCount == 0, "wrong stream mistaken for ready");
        Assert.That(Inspect(header) is { Shape: "canonical", RecordCount: 0 }, "ACK-only stream rejected");
        Assert.That(Inspect([.. header, 0]).Shape == "truncated_record_header", "short record header");
        Assert.That(Inspect([.. header, 0, 1, 0, 2, 1]).Shape == "truncated_record_body", "short record body");
        Assert.That(Inspect([.. header, 0, 1, 2, 0x21]).Shape == "record_over_reader_limit", "oversized record");
        header[0] = 2;
        byte[] many = [.. header, .. Enumerable.Range(0, 100).SelectMany(i => new byte[] { 0, (byte)i, 0, 1, 1 })];
        Assert.That(Inspect(many) is { RecordCount: 100, ReadyRecordCount: 100, RecordsTruncated: true, Records.Length: 16 }, "bounded record output lost aggregate counts");
        var serialized = JsonSerializer.Serialize(index);
        Assert.That(!serialized.Contains("2864434397", StringComparison.Ordinal) && !serialized.Contains("16909060", StringComparison.Ordinal), "raw nonce leaked");
        string[] allowed = ["CrcValid", "Shape", "Kind", "ControlSubtype", "ProtocolByte", "AdvertisesSecondary", "SenderNonceMatchesEnvelope",
            "DestinationNonceMatchesEnvelope", "AcknowledgedSequence", "RecordCount", "ReadyRecordCount", "IndexRecordCount", "RecordsTruncated", "Records"];
        Assert.That(typeof(PeerPrimaryAudit).GetProperties().Select(p => p.Name).Order().SequenceEqual(allowed.Order()), "unreviewed peer audit field");
        Assert.That(typeof(PeerRecordAudit).GetProperties().Select(p => p.Name).Order().SequenceEqual(new[] { "Sequence", "Length", "Kind", "Operation", "Index" }.Order()), "unreviewed record audit field");
    }

    internal static void Forwarding()
    {
        foreach (var enabled in new[] { false, true })
        {
            var options = Room.NewOptions();
            options.PeerDiagnostics = enabled;
            using var room = new Room(options);
            room.Ready();
            foreach (var primary in new[] { Encode([1, 2, 2, 0xa7, 1, 2, 3, 4]), Encode([1, 2, 0x16]), new byte[] { 1, 2, 3, 4 }, Array.Empty<byte>() })
            {
                var block = Block(primary);
                var body = Bytes.Route([2], block);
                var before = body.ToArray();
                room.Conversations["alice"].Application(4, 1, 0x10, body);
                Assert.Bytes(before, body, "diagnostics mutated routed input");
                Assert.Bytes([.. Bytes.Text(Room.Session), 0, 1, .. Bytes.Sized(block)], room.Take("bob")!.Body, "diagnostics changed forwarded primary/secondary bytes");
                Assert.That((room.PayloadAudits[^1].PeerPrimary is not null) == enabled && room.PayloadAudits[^1].Outcome == "queued", "opt-in or malformed-inner noninterference");
                room.Empty();
            }
        }
    }

    internal static void Sampling()
    {
        var sampling = new PeerDiagnosticSampling();
        var ping = Inspect([1, 2, 3, 1, 1]);
        var count = Enumerable.Range(1, 1000).Count(_ => sampling.Observe(ping));
        Assert.That(count == 18, "routine traffic sampling unbounded");
        Assert.That(sampling.Observe(Inspect([1, 2, 1, .. new byte[8]])), "late hello sampled out");
        Assert.That(sampling.Observe(Inspect([2, 1, .. new byte[10], 0, 1, 0, 1, 1])), "late ready sampled out");
        Assert.That(sampling.Observe(Inspect([3, 1, .. new byte[10], 0, 1, 0, 2, 1, 1])), "late index sampled out");
        for (var i = 0; i < 1000; i++) sampling.Observe(Inspect([3, 1, .. new byte[10], 0, 1, 0, 2, 1, 1]));
        Assert.That(sampling.Observe(Inspect([3, 1, .. new byte[10], 0, 1, 0, 3, 1, 2, 7])), "index assignment hidden behind requests");
        for (var i = 0; i < 1000; i++) sampling.Observe(Inspect([1, 2, 255]));
        Assert.That(sampling.Observe(Inspect([4, 2])), "data header hidden behind malformed controls");
    }
}
