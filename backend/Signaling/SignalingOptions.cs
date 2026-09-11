using System.Net;

namespace ReVerse.Capture.Signaling;

public sealed class SignalingOptions
{
    public bool Enabled { get; set; }
    public bool PeerDiagnostics { get; set; }
    public string BindAddress { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 5070;
    public string PublicHost { get; set; } = "";
    public int PublicPort { get; set; } = 5070;
    public int MaxConnections { get; set; } = 32;
    public int HandshakeTimeoutSeconds { get; set; } = 10;
    public int IdleTimeoutSeconds { get; set; } = 90;
    public int MaxSessionSeconds { get; set; } = 7200;

    public bool ExperimentalReplies { get; set; }
    public uint? Reply19 { get; set; }
    public uint? Reply21 { get; set; }

    public int? NegativeControlReply { get; set; }
    public ushort? RegistrationFieldB { get; set; }
    public bool RegistrationFieldBIsPeerNumber { get; set; }

    public void Validate()
    {
        if (!IPAddress.TryParse(BindAddress, out _)) throw new ArgumentException("Signaling:BindAddress must be an IP address.");
        if (Port is < 1 or > 65535 || PublicPort is < 1 or > 65535)
            throw new ArgumentException("Signaling ports must be 1..65535.");
        if (Enabled && (string.IsNullOrWhiteSpace(PublicHost) || PublicHost.Length > 253
            || Uri.CheckHostName(PublicHost) == UriHostNameType.Unknown
            || PublicHost is "0.0.0.0" or "::"))
            throw new ArgumentException("Signaling:PublicHost must be the UDP-reachable IP or hostname, without URL scheme/path.");
        if (MaxConnections is < 2 or > 128 || HandshakeTimeoutSeconds is < 1 or > 30
            || IdleTimeoutSeconds is < 5 or > 600 || MaxSessionSeconds is < 60 or > 86400)
            throw new ArgumentException("Invalid signaling resource/lifetime limits.");
        if (ExperimentalReplies && (Reply19 is null or 0 || Reply21 is null or 0
            || (RegistrationFieldB is null) == !RegistrationFieldBIsPeerNumber))
            throw new ArgumentException("Experimental signaling requires nonzero Reply19/Reply21 and exactly one of RegistrationFieldB or RegistrationFieldBIsPeerNumber.");
        if (NegativeControlReply is not null && (!Enabled || !ExperimentalReplies || NegativeControlReply is not (19 or 21)))
            throw new ArgumentException("NegativeControlReply requires an enabled experimental provider and must select 19 or 21.");
    }
}
