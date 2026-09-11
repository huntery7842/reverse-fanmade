namespace ReVerse.Capture.Signaling;


public sealed record WirePacket(byte Class, string FirstId, string? SecondId, ulong Sequence, IReadOnlyList<WireContent> Contents)
{
    public bool IsExtended => (Class & 0x80) != 0;

    private void ValidateHeader()
    {
        if (!IsExtended && (Class & 0x40) == 0) throw new WireFormatException("Short header requires class bit 0x40.");
        if (IsExtended != (SecondId is not null)) throw new WireFormatException("Incorrect connection identifier count.");
    }

    public static WirePacket Parse(ReadOnlyMemory<byte> data, WireLimits? limits = null)
    {
        limits ??= new();
        limits.Validate();
        var reader = new WireReader(data, limits.MaxPacketBytes);
        byte packetClass = reader.ReadByte();
        bool extended = (packetClass & 0x80) != 0;
        if (!extended && (packetClass & 0x40) == 0) throw new WireFormatException("Short header requires class bit 0x40.");
        if (extended && reader.ReadUInt32() != 1) throw new WireFormatException("Unsupported extended header version.");
        string first = reader.ReadPackedHex();
        string? second = extended ? reader.ReadPackedHex() : null;
        ulong sequence = reader.ReadCompact();
        var contents = new List<WireContent>();
        while (reader.Remaining != 0)
        {
            var content = WireContentCodec.Read(reader, limits);
            if (content is WirePadding && contents.LastOrDefault() is WirePadding previous)
                contents[^1] = new WirePadding(previous.Count + 1);
            else
            {
                if (contents.Count >= limits.MaxContentItems) throw new WireFormatException("Too many content items.");
                contents.Add(content);
            }
        }
        return new(packetClass, first, second, sequence, contents.AsReadOnly());
    }

    public byte[] Encode(WireLimits? limits = null)
    {
        limits ??= new();
        limits.Validate();
        ValidateHeader();
        if (Contents.Count > limits.MaxContentItems) throw new WireFormatException("Too many content items.");
        var writer = new WireWriter(limits.MaxPacketBytes);
        writer.WriteByte(Class);
        if (IsExtended) writer.WriteUInt32(1);
        writer.WritePackedHex(FirstId);
        if (IsExtended) writer.WritePackedHex(SecondId!);
        writer.WriteCompact(Sequence);
        for (int i = 0; i < Contents.Count; i++) WireContentCodec.Write(writer, Contents[i], limits, i == Contents.Count - 1);
        return writer.ToArray();
    }
}
