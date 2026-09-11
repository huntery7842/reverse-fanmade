namespace ReVerse.Capture.Signaling;


public sealed record WireApplication(
    byte Family, byte Operation, byte Qualifier, ushort Routing, uint Auxiliary, ReadOnlyMemory<byte> Body)
{
    public const int HeaderLength = 11;


    public byte Descriptor { get; init; }

    public byte[] Encode()
    {
        if (Body.Length > ushort.MaxValue) throw new WireFormatException("Application body exceeds uint16 length.");
        var writer = new WireWriter(HeaderLength + ushort.MaxValue);
        writer.WriteByte(Family);
        writer.WriteByte(Operation);
        writer.WriteByte(Qualifier);
        writer.WriteUInt16(Routing);
        writer.WriteUInt16((ushort)Body.Length);
        writer.WriteUInt32(Auxiliary);
        writer.WriteBytes(Body.Span);
        return writer.ToArray();
    }

    public static WireApplication Parse(ReadOnlyMemory<byte> data)
    {
        var reader = new WireReader(data, HeaderLength + ushort.MaxValue);
        var application = Read(reader);
        reader.RequireEnd();
        return application;
    }

    private static WireApplication Read(WireReader reader)
    {
        byte family = reader.ReadByte();
        byte operation = reader.ReadByte();
        byte qualifier = reader.ReadByte();
        ushort routing = reader.ReadUInt16();
        ushort length = reader.ReadUInt16();
        uint auxiliary = reader.ReadUInt32();
        return new(family, operation, qualifier, routing, auxiliary, reader.ReadBytes(length));
    }

    public static IReadOnlyList<WireApplication> ParseMany(ReadOnlyMemory<byte> data, WireLimits? limits = null)
    {
        limits ??= new();
        limits.Validate();
        var reader = new WireReader(data, limits.MaxBufferedBytes);
        var applications = new List<WireApplication>();
        while (reader.Remaining != 0)
        {
            if (applications.Count >= limits.MaxApplicationRecords) throw new WireFormatException("Too many application records.");
            applications.Add(Read(reader));
        }
        return applications.AsReadOnly();
    }
}


public sealed record WireOutboundPayload(IReadOnlyList<ushort> Destinations, byte BlockTag, ReadOnlyMemory<byte> Payload)
{
    public static WireOutboundPayload Parse(ReadOnlyMemory<byte> data, WireLimits? limits = null)
    {
        limits ??= new();
        limits.Validate();
        var reader = new WireReader(data, ushort.MaxValue);
        int count = reader.ReadUInt16();
        if (count > limits.MaxDestinations || count > reader.Remaining / 2) throw new WireFormatException("Invalid destination count.");
        var destinations = new ushort[count];
        for (int i = 0; i < count; i++) destinations[i] = reader.ReadUInt16();
        var block = new WireReader(reader.ReadBytes16());
        byte tag = block.ReadByte();
        var payload = block.ReadBytes(block.Remaining);
        reader.RequireEnd();
        return new(Array.AsReadOnly(destinations), tag, payload);
    }

    public byte[] Encode(WireLimits? limits = null)
    {
        limits ??= new();
        limits.Validate();
        if (Destinations.Count > limits.MaxDestinations || Destinations.Count > ushort.MaxValue)
            throw new WireFormatException("Too many destinations.");
        var writer = new WireWriter(ushort.MaxValue);
        writer.WriteUInt16((ushort)Destinations.Count);
        foreach (ushort destination in Destinations) writer.WriteUInt16(destination);
        WriteBlock(writer, BlockTag, Payload);
        return writer.ToArray();
    }

    internal static void WriteBlock(WireWriter writer, byte tag, ReadOnlyMemory<byte> payload)
    {
        if (payload.Length >= ushort.MaxValue) throw new WireFormatException("Block including its tag exceeds uint16 length.");
        writer.WriteUInt16((ushort)(payload.Length + 1));
        writer.WriteByte(tag);
        writer.WriteBytes(payload.Span);
    }
}


public sealed record WireInboundPayload(ReadOnlyMemory<byte> Context, ushort Sender, byte BlockTag, ReadOnlyMemory<byte> Payload)
{
    public static WireInboundPayload Parse(ReadOnlyMemory<byte> data)
    {
        var reader = new WireReader(data, ushort.MaxValue);
        var context = reader.ReadBytes16();
        ushort sender = reader.ReadUInt16();
        var block = new WireReader(reader.ReadBytes16());
        byte tag = block.ReadByte();
        var payload = block.ReadBytes(block.Remaining);
        reader.RequireEnd();
        return new(context, sender, tag, payload);
    }

    public byte[] Encode()
    {
        var writer = new WireWriter(ushort.MaxValue);
        writer.WriteBytes16(Context.Span);
        writer.WriteUInt16(Sender);
        WireOutboundPayload.WriteBlock(writer, BlockTag, Payload);
        return writer.ToArray();
    }
}


public sealed record WireNestedPayload(
    uint SenderNonce, uint DestinationNonce, ReadOnlyMemory<byte> Primary,
    ushort? SecondaryMetadata, ReadOnlyMemory<byte> Secondary)
{
    public static WireNestedPayload Parse(ReadOnlyMemory<byte> data)
    {
        var reader = new WireReader(data, ushort.MaxValue - 1);
        if (reader.ReadByte() != 1) throw new WireFormatException("Unsupported nested payload version.");
        uint sender = reader.ReadUInt32();
        uint destination = reader.ReadUInt32();
        int primaryLength = reader.ReadUInt16();
        int secondaryLength = reader.ReadUInt16();
        var primary = reader.ReadBytes(primaryLength);
        ushort? metadata = secondaryLength > 0 ? reader.ReadUInt16() : null;
        var secondary = reader.ReadBytes(secondaryLength);
        reader.RequireEnd();
        return new(sender, destination, primary, metadata, secondary);
    }

    public byte[] Encode()
    {
        if (Primary.Length > ushort.MaxValue || Secondary.Length > ushort.MaxValue ||
            (Secondary.Length > 0) != SecondaryMetadata.HasValue)
            throw new WireFormatException("Invalid nested payload lengths/secondary metadata.");
        var writer = new WireWriter(ushort.MaxValue - 1);
        writer.WriteByte(1);
        writer.WriteUInt32(SenderNonce);
        writer.WriteUInt32(DestinationNonce);
        writer.WriteUInt16((ushort)Primary.Length);
        writer.WriteUInt16((ushort)Secondary.Length);
        writer.WriteBytes(Primary.Span);
        if (SecondaryMetadata.HasValue) writer.WriteUInt16(SecondaryMetadata.Value);
        writer.WriteBytes(Secondary.Span);
        return writer.ToArray();
    }
}
