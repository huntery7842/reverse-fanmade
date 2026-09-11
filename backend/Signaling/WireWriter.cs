using System.Buffers.Binary;

namespace ReVerse.Capture.Signaling;


public sealed class WireWriter
{
    private readonly MemoryStream _stream = new();
    private readonly int _maxBytes;
    public int Length => (int)_stream.Length;

    public WireWriter(int maxBytes = 1_048_576)
    {
        if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _maxBytes = maxBytes;
    }

    private void Reserve(int count)
    {
        if (count < 0 || count > _maxBytes - Length) throw new WireFormatException("Output exceeds byte limit.");
    }

    public void WriteByte(byte value) { Reserve(1); _stream.WriteByte(value); }
    public void WriteBytes(ReadOnlySpan<byte> bytes) { Reserve(bytes.Length); _stream.Write(bytes); }
    public void WriteUInt16(ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        WriteBytes(bytes);
    }
    public void WriteUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        WriteBytes(bytes);
    }
    public void WriteUInt64(ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        WriteBytes(bytes);
    }

    public static int CompactWidth(ulong value) => value switch
    {
        <= 63 => 1,
        <= 16_383 => 2,
        <= 1_073_741_823 => 4,
        <= WireReader.MaxCompact => 8,
        _ => throw new WireFormatException("Value exceeds the 62-bit compact range.")
    };

    public void WriteCompact(ulong value)
    {
        switch (CompactWidth(value))
        {
            case 1: WriteByte((byte)value); break;
            case 2: WriteUInt16((ushort)(value | 0x4000)); break;
            case 4: WriteUInt32((uint)(value | 0x80000000)); break;
            case 8: WriteUInt64(value | 0xc000000000000000); break;
        }
    }

    public void WriteBytes16(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > ushort.MaxValue) throw new WireFormatException("Byte string exceeds uint16 length.");
        Reserve(2 + bytes.Length);
        WriteUInt16((ushort)bytes.Length);
        WriteBytes(bytes);
    }

    public void WritePackedHex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if ((value.Length & 1) != 0 || value.Length > 510 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new WireFormatException("Connection identifier must be even-length hexadecimal, at most 255 bytes.");
        Reserve(1 + value.Length / 2);
        WriteByte((byte)(value.Length / 2));
        WriteBytes(Convert.FromHexString(value));
    }

    public byte[] ToArray() => _stream.ToArray();
}
