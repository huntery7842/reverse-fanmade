namespace ReVerse.Capture.Signaling;


public sealed record SignalingPayloadAudit
{
    public int BodyBytes { get; init; }
    public ushort? DestinationCount { get; init; }
    public byte? Tag { get; init; }
    public byte? Version { get; init; }
    public ushort? PrimaryLength { get; init; }
    public ushort? SecondaryLength { get; init; }
    public ushort? SecondaryMetadata { get; init; }
    public bool EnvelopeValid { get; init; }
    public PeerPrimaryAudit? PeerPrimary { get; init; }
    public string Outcome { get; init; } = "rejected";
}
