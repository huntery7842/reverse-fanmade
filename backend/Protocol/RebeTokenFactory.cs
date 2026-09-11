using System.Text;
using System.Text.Json;

namespace ReVerse.Capture.Protocol;

public static class RebeTokenFactory
{
    public const long LifetimeSeconds = 7 * 24 * 60 * 60;


    public static string New(long issuedAtSeconds)
    {
        var payload = JsonSerializer.Serialize(new { iat = issuedAtSeconds, exp = issuedAtSeconds + LifetimeSeconds, linked = true });
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=');
        return $"header.{encoded}.{Guid.NewGuid():N}";
    }
}
