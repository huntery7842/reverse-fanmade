using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ReVerse.Capture.Signaling;


public sealed record PeerPrimaryAudit
{
    public bool? CrcValid { get; init; }
    public string Shape { get; init; } = "incomplete";
    public string Kind { get; init; } = "unknown";
    public byte? ControlSubtype { get; init; }
    public byte? ProtocolByte { get; init; }
    public bool? AdvertisesSecondary { get; init; }
    public bool? SenderNonceMatchesEnvelope { get; init; }
    public bool? DestinationNonceMatchesEnvelope { get; init; }
    public ushort? AcknowledgedSequence { get; init; }
    public int RecordCount { get; init; }
    public int ReadyRecordCount { get; init; }
    public int IndexRecordCount { get; init; }
    public bool RecordsTruncated { get; init; }
    public PeerRecordAudit[] Records { get; init; } = [];
}

public sealed record PeerRecordAudit(ushort Sequence, ushort Length, string Kind, byte? Operation, int? Index);


public static class PeerPrimaryDiagnostics
{
    public const int MaxRecords = 16;
    private static readonly int[] ControlLengths = [0, 11, 8, 5, 4, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 7, 7, 11, 15, 7, 16, 9, 9, 8, 8, 7, 7];

    public static PeerPrimaryAudit Inspect(ReadOnlySpan<byte> wire, uint envelopeSender, uint envelopeDestination)
    {
        if (wire.Length < 4) return new() { Shape = "primary_too_short" };
        if (wire.Length > ushort.MaxValue) return new() { Shape = "primary_over_limit" };
        var copy = wire.ToArray();
        try
        {
            byte previous = 0x25;
            for (var i = 0; i < copy.Length; i++)
            {
                var encoded = copy[i];
                copy[i] ^= previous;
                previous = encoded;
            }
            var supplied = BinaryPrimitives.ReadUInt32BigEndian(copy);
            copy.AsSpan(0, 4).Clear();
            uint crc = uint.MaxValue;
            foreach (var value in copy)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
            if (crc != supplied) return new() { CrcValid = false, Shape = "crc_mismatch" };
            return Parse(copy.AsSpan(4), envelopeSender, envelopeDestination);
        }
        finally { CryptographicOperations.ZeroMemory(copy); }
    }

    private static PeerPrimaryAudit Parse(ReadOnlySpan<byte> body, uint sender, uint destination)
    {
        var result = new PeerPrimaryAudit { CrcValid = true };
        if (body.Length < 2) return result;
        if (body[0] == 1 && body[1] == 2)
        {
            result = result with { Kind = "control" };
            if (body.Length < 3) return result;
            var subtype = body[2];
            var expected = subtype < ControlLengths.Length ? ControlLengths[subtype] : 0;
            result = result with { ControlSubtype = subtype, Shape = expected == 0 ? "unknown_control" :
                body.Length == expected ? "canonical" : "noncanonical_length" };
            if (expected != 0 && body.Length == expected && subtype is 2 or 0x16)
                result = result with { ProtocolByte = body[3] };
            if (subtype == 2 && body.Length == 8)
                result = result with { SenderNonceMatchesEnvelope = BinaryPrimitives.ReadUInt32BigEndian(body[4..]) == sender };
            if (body.Length == expected && subtype is 3 or 0x17 or 0x18)
                result = result with { AdvertisesSecondary = (body[^1] & 1) != 0 };
            return result;
        }
        if (body[0] == 4 && body[1] == 2)
            return result with { Kind = "peer_data", Shape = "header_only_not_validated" };
        if (body[0] is not (2 or 3) || body[1] != 1)
            return result with { Shape = "unknown_header" };
        result = result with { Kind = body[0] == 2 ? "control_stream" : "index_stream" };
        if (body.Length < 12) return result;
        result = result with
        {
            SenderNonceMatchesEnvelope = BinaryPrimitives.ReadUInt32BigEndian(body[2..]) == sender,
            DestinationNonceMatchesEnvelope = BinaryPrimitives.ReadUInt32BigEndian(body[6..]) == destination,
            AcknowledgedSequence = BinaryPrimitives.ReadUInt16BigEndian(body[10..]),
        };
        var records = new List<PeerRecordAudit>(MaxRecords);
        var count = 0;
        var ready = 0;
        var indices = 0;
        var shape = "canonical";
        for (var offset = 12; offset < body.Length;)
        {
            if (body.Length - offset < 4) { shape = "truncated_record_header"; break; }
            var sequence = BinaryPrimitives.ReadUInt16BigEndian(body[offset..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(body[(offset + 2)..]);
            offset += 4;
            if (length > 0x220) { shape = "record_over_reader_limit"; break; }
            if (length > body.Length - offset) { shape = "truncated_record_body"; break; }
            var record = body.Slice(offset, length);
            offset += length;
            count++;
            string kind = "opaque";
            byte? operation = null;
            int? index = null;
            if (body[0] == 2 && record.Length == 1 && record[0] == 1)
            {
                kind = "ready_record_observed";
                ready++;
            }
            else if (body[0] == 3 && record.Length >= 2 && record[0] == 1 && record[1] is >= 1 and <= 4
                && record.Length == (record[1] is 2 or 4 ? 3 : 2))
            {
                kind = "index_record_observed";
                operation = record[1];
                if (record.Length == 3) index = unchecked((sbyte)record[2]);
                indices++;
            }
            if (records.Count < MaxRecords) records.Add(new(sequence, length, kind, operation, index));
        }
        return result with { Shape = shape, Records = records.ToArray(), RecordCount = count,
            ReadyRecordCount = ready, IndexRecordCount = indices, RecordsTruncated = count > MaxRecords };
    }
}


public sealed class PeerDiagnosticSampling
{
    private readonly long[] counts = new long[52];
    public bool Observe(PeerPrimaryAudit value)
    {
        var key = value.CrcValid != true ? 0 : value.Kind == "peer_data" ? 6 : value.Shape != "canonical" ? 1 :
            value.ReadyRecordCount > 0 ? 2 : value.IndexRecordCount > 0 ? 3 : value.Kind switch
            {
                "control" when value.ControlSubtype is >= 1 and <= 0x1c => 7 + value.ControlSubtype.Value,
                "control_stream" => 4,
                "index_stream" => 5,
                _ => 6,
            };
        if (key == 3)
        {
            var operations = 0;
            foreach (var record in value.Records)
                if (record.Operation is >= 1 and <= 4) operations |= 1 << (record.Operation.Value - 1);
            key = 36 + operations;
        }
        var count = ++counts[key];
        return count <= 8 || count % 100 == 0;
    }
}
