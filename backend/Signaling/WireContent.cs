namespace ReVerse.Capture.Signaling;

public abstract record WireContent(byte Type);
public sealed record WirePadding(int Count = 1) : WireContent(0);


public sealed record WireControl(byte Code, IReadOnlyList<ulong> Values) : WireContent(Code);
public sealed record WireSetting(ulong Key, ulong Value);
public sealed record WireSettings(IReadOnlyList<WireSetting> Entries) : WireContent(0x16);
public readonly record struct WireAckRange(ulong Low, ulong High);


public sealed record WireAcknowledgment(IReadOnlyList<WireAckRange> Ranges, uint Delay = 0) : WireContent(2)
{
    internal void Validate(WireLimits limits)
    {
        if (Ranges.Count < 1 || Ranges.Count > limits.MaxAckRanges)
            throw new WireFormatException("ACK range count exceeds limit or is empty.");
        for (int i = 0; i < Ranges.Count; i++)
        {
            var range = Ranges[i];
            if (range.Low > range.High || range.High > WireReader.MaxCompact)
                throw new WireFormatException("Invalid ACK range.");
            if (i > 0 && (Ranges[i - 1].Low < 2 || range.High > Ranges[i - 1].Low - 2))
                throw new WireFormatException("ACK ranges must be descending and separated by a gap.");
        }
    }
}


public sealed record WireFragment(byte Code, byte Descriptor, ulong Offset, ReadOnlyMemory<byte> Data) : WireContent(Code)
{
    public int Channel => Descriptor >> 4;
    public bool IsReliable => (Descriptor & 8) != 0;
    public bool IsFinal => (Type & 2) != 0;
    public bool HasOffset => (Type & 4) != 0;
    public bool HasLength => (Type & 1) != 0;
}

internal static class WireContentCodec
{
    private static int ControlCount(byte type) => type switch
    {
        3 or 4 or 5 => 0,
        0x12 => 2,
        0x13 or 0x14 or 0x15 => 1,
        _ => throw new WireFormatException($"Unsupported content type 0x{type:x2}; its boundary is not recovered.")
    };

    internal static WireContent Read(WireReader reader, WireLimits limits)
    {
        byte type = reader.ReadByte();
        if (type == 0) return new WirePadding();
        if (type is >= 8 and <= 15)
        {
            uint descriptorValue = reader.ReadCompactUInt32();
            if (descriptorValue > byte.MaxValue) throw new WireFormatException("Fragment descriptor exceeds byte range.");
            byte descriptor = (byte)descriptorValue;
            ulong offset = (type & 4) != 0 ? reader.ReadCompact() : 0;
            int length = (type & 1) != 0 ? reader.ReadCompactLength(reader.Remaining) : reader.Remaining;
            if (offset > WireReader.MaxCompact - (ulong)length) throw new WireFormatException("Fragment offset overflows.");
            return new WireFragment(type, descriptor, offset, reader.ReadBytes(length));
        }
        if (type == 2)
        {
            ulong high = reader.ReadCompact();
            uint delay = reader.ReadCompactUInt32();
            int extra = reader.ReadCompactLength(limits.MaxAckRanges - 1);
            ulong length = reader.ReadCompact();
            if (extra > reader.Remaining / 2 || length > high) throw new WireFormatException("Truncated or underflowing ACK.");
            var ranges = new List<WireAckRange> { new(high - length, high) };
            for (int i = 0; i < extra; i++)
            {
                ulong gap = reader.ReadCompact();
                length = reader.ReadCompact();
                ulong previousLow = ranges[^1].Low;
                if (previousLow < 2 || gap > previousLow - 2) throw new WireFormatException("ACK gap underflows.");
                high = previousLow - gap - 2;
                if (length > high) throw new WireFormatException("ACK range underflows.");
                ranges.Add(new(high - length, high));
            }
            return new WireAcknowledgment(ranges.AsReadOnly(), delay);
        }
        if (type == 0x16)
        {
            int count = reader.ReadCompactLength(limits.MaxSettings);
            if (count > reader.Remaining / 3) throw new WireFormatException("Truncated settings collection.");
            var entries = new List<WireSetting>();
            for (int i = 0; i < count; i++)
            {
                ulong key = reader.ReadCompact();
                int length = reader.ReadCompactLength(8);
                var valueReader = new WireReader(reader.ReadBytes(length));
                ulong value = valueReader.ReadCompact();
                valueReader.RequireEnd();
                entries.Add(new(key, value));
            }
            return new WireSettings(entries.AsReadOnly());
        }
        int valueCount = ControlCount(type);
        var values = new ulong[valueCount];
        for (int i = 0; i < valueCount; i++)
            values[i] = type is 0x13 or 0x15 ? reader.ReadCompactUInt32() : reader.ReadCompact();
        return new WireControl(type, Array.AsReadOnly(values));
    }

    internal static void Write(WireWriter writer, WireContent content, WireLimits limits, bool last)
    {
        if (content is WirePadding padding)
        {
            if (padding.Count < 1 || padding.Count > limits.MaxPacketBytes) throw new WireFormatException("Invalid padding length.");
            for (int i = 0; i < padding.Count; i++) writer.WriteByte(0);
            return;
        }
        writer.WriteByte(content.Type);
        switch (content)
        {
            case WireFragment fragment:
                if (fragment.Type is < 8 or > 15 || (!fragment.HasOffset && fragment.Offset != 0) ||
                    (!fragment.HasLength && !last) || fragment.Offset > WireReader.MaxCompact ||
                    (ulong)fragment.Data.Length > WireReader.MaxCompact - fragment.Offset)
                    throw new WireFormatException("Invalid fragment framing or offset.");
                writer.WriteCompact(fragment.Descriptor);
                if (fragment.HasOffset) writer.WriteCompact(fragment.Offset);
                if (fragment.HasLength) writer.WriteCompact((ulong)fragment.Data.Length);
                writer.WriteBytes(fragment.Data.Span);
                break;
            case WireAcknowledgment acknowledgment:
                acknowledgment.Validate(limits);
                var first = acknowledgment.Ranges[0];
                writer.WriteCompact(first.High);
                writer.WriteCompact(acknowledgment.Delay);
                writer.WriteCompact((ulong)acknowledgment.Ranges.Count - 1);
                writer.WriteCompact(first.High - first.Low);
                for (int i = 1; i < acknowledgment.Ranges.Count; i++)
                {
                    var range = acknowledgment.Ranges[i];
                    writer.WriteCompact(acknowledgment.Ranges[i - 1].Low - range.High - 2);
                    writer.WriteCompact(range.High - range.Low);
                }
                break;
            case WireSettings settings:
                if (settings.Entries.Count > limits.MaxSettings) throw new WireFormatException("Too many settings.");
                writer.WriteCompact((ulong)settings.Entries.Count);
                foreach (var setting in settings.Entries)
                {
                    writer.WriteCompact(setting.Key);
                    writer.WriteCompact((ulong)WireWriter.CompactWidth(setting.Value));
                    writer.WriteCompact(setting.Value);
                }
                break;
            case WireControl control:
                if (control.Values.Count != ControlCount(control.Type)) throw new WireFormatException("Incorrect control scalar count.");
                if (control.Type is 0x13 or 0x15 && control.Values[0] > uint.MaxValue)
                    throw new WireFormatException("Control value exceeds recovered uint32 field.");
                foreach (ulong value in control.Values) writer.WriteCompact(value);
                break;
            default: throw new WireFormatException("Unsupported content object.");
        }
    }
}
