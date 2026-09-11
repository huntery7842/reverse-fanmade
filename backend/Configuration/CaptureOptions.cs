namespace ReVerse.Capture.Configuration;

public sealed class CaptureOptions
{
    public string LogDirectory { get; set; } = "logs";
    public int MaxCapturedBodyBytes { get; set; } = 1024 * 1024;
    public bool RedactSensitiveHeaders { get; set; } = true;
    public int ResponseStatusCode { get; set; } = 200;
    public string ResponseBody { get; set; } = "{}";
    public string ResponseContentType { get; set; } = "application/json";

    public void Validate()
    {
        if (MaxCapturedBodyBytes is < 0 or > 64 * 1024 * 1024)
            throw new ArgumentException("Capture:MaxCapturedBodyBytes must be between 0 and 67108864 bytes.");
        if (ResponseStatusCode is < 200 or > 599)
            throw new ArgumentException("Capture:ResponseStatusCode must be between 200 and 599.");
        if (string.IsNullOrWhiteSpace(LogDirectory) || string.IsNullOrWhiteSpace(ResponseContentType))
            throw new ArgumentException("Capture:LogDirectory and Capture:ResponseContentType cannot be blank.");
    }

    public static bool IsSensitiveHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("api-key", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Auth-Session-Key", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Be-Ticket-Id", StringComparison.OrdinalIgnoreCase);
}