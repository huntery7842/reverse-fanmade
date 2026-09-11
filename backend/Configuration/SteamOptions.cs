namespace ReVerse.Capture.Configuration;

public sealed class SteamOptions
{
    public const uint ResidentEvilReverseAppId = 1236300;




    public string Mode { get; set; } = "auto";

    public uint AppId { get; set; } = ResidentEvilReverseAppId;





    public string ApiKey { get; set; } = "";

    public string ApiBaseUrl { get; set; } = "https://partner.steam-api.com";





    public string TicketIdentity { get; set; } = "";





    public string NativeApiPath { get; set; } = "";

    public bool UseLocalClient { get; set; } = true;
    public bool AllowFallback { get; set; } = true;
    public string FallbackAccountId { get; set; } = "local-player";
    public string FallbackNickname { get; set; } = "LocalPlayer";
    public int RequestTimeoutSeconds { get; set; } = 5;
    public int ProfileCacheSeconds { get; set; } = 300;
    public bool ApiKeyConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public void Validate()
    {
        var mode = Mode.Trim().ToLowerInvariant();
        if (mode is not ("auto" or "local-client" or "web-api" or "fallback"))
            throw new ArgumentException("Steam:Mode must be auto, local-client, web-api, or fallback.");

        if (AppId == 0)
            throw new ArgumentException("Steam:AppId must be a positive Steam application ID.");

        if (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var apiBaseUri) ||
            apiBaseUri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Steam:ApiBaseUrl must be an absolute HTTPS URL.");

        if (string.IsNullOrWhiteSpace(FallbackAccountId) || string.IsNullOrWhiteSpace(FallbackNickname))
            throw new ArgumentException("Steam fallback account and nickname cannot be blank.");

        if (RequestTimeoutSeconds is < 1 or > 60)
            throw new ArgumentException("Steam:RequestTimeoutSeconds must be between 1 and 60.");

        if (ProfileCacheSeconds is < 0 or > 86400)
            throw new ArgumentException("Steam:ProfileCacheSeconds must be between 0 and 86400.");
    }
}
