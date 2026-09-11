namespace ReVerse.Capture.Protocol;

public sealed record SteamIdentity(
    string AccountId,
    string Nickname,
    string? ProfileUrl = null);

public sealed record SteamIdentityResolution(
    SteamIdentity Identity,
    string Source,
    string Detail);
