using System.Buffers.Binary;

namespace ReVerse.Capture.Signaling;


public sealed class WireFormatException(string message) : FormatException(message);


public sealed record WireLimits
{
    public int MaxPacketBytes { get; init; } = 65_535;
    public int MaxContentItems { get; init; } = 256;
    public int MaxAckRanges { get; init; } = 64;
    public int MaxSettings { get; init; } = 64;
    public int MaxFragments { get; init; } = 64;
    public int MaxBufferedBytes { get; init; } = 262_144;
    public int MaxApplicationRecords { get; init; } = 256;
    public int MaxReceivedPackets { get; init; } = 1_024;
    public int MaxRetainedPackets { get; init; } = 256;
    public int MaxRetainedBytes { get; init; } = 1_048_576;
    public int MaxDestinations { get; init; } = 128;

    internal void Validate()
    {
        if (MaxPacketBytes < 1 || MaxContentItems < 1 || MaxAckRanges < 1 ||
            MaxSettings < 1 || MaxFragments is < 1 or > 64 || MaxBufferedBytes < 1 ||
            MaxApplicationRecords < 1 || MaxReceivedPackets < 1 ||
            MaxRetainedPackets < 1 || MaxRetainedBytes < 1 || MaxDestinations < 1)
            throw new ArgumentOutOfRangeException(nameof(WireLimits));
    }
}


public sealed class WireReader
{
    public const ulong MaxCompact = (1UL << 62) - 1;
    private readonly ReadOnlyMemory<byte> _data;
    public int Position { get; private set; }
    public int Remaining => _data.Length - Position;

    public WireReader(ReadOnlyMemory<byte> data, int maxBytes = 1_048_576)
    {
        if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (data.Length > maxBytes) throw new WireFormatException("Input exceeds byte limit.");
        _data = data;
    }

    public byte ReadByte() => ReadBytes(1).Span[0];
    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(2).Span);
    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4).Span);
    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64BigEndian(ReadBytes(8).Span);

    public ReadOnlyMemory<byte> ReadBytes(int length)
    {
        if (length < 0 || length > Remaining) throw new WireFormatException("Truncated or invalid length.");
        var result = _data.Slice(Position, length);
        Position += length;
        return result;
    }

    public ReadOnlyMemory<byte> ReadBytes16() => ReadBytes(ReadUInt16());

    public ulong ReadCompact()
    {
        byte first = ReadByte();
        int width = 1 << (first >> 6);
        ulong result = (ulong)(first & 0x3f);
        foreach (byte next in ReadBytes(width - 1).Span) result = (result << 8) | next;

        return result;
    }

    public uint ReadCompactUInt32()
    {
        ulong value = ReadCompact();
        if (value > uint.MaxValue) throw new WireFormatException("Compact value exceeds uint32.");
        return (uint)value;
    }

    public int ReadCompactLength(int maximum)
    {
        ulong value = ReadCompact();
        if (maximum < 0 || value > (ulong)maximum) throw new WireFormatException("Length/count exceeds limit.");
        return (int)value;
    }

    public string ReadPackedHex() => Convert.ToHexString(ReadBytes(ReadByte()).Span).ToLowerInvariant();

    public void RequireEnd()
    {
        if (Remaining != 0) throw new WireFormatException("Unexpected trailing bytes.");
    }
}
