using System.Text.Json.Nodes;

namespace ReVerse.Capture.Matchmaking;

public static class ProtocolAudit
{
    public static JsonNode? Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var copy = new JsonObject();
            foreach (var pair in obj)
                copy[pair.Key] = Sensitive(pair.Key) ? JsonValue.Create("[REDACTED]") : Redact(pair.Value);
            return copy;
        }
        if (node is JsonArray array) return new JsonArray(array.Select(Redact).ToArray());
        return node?.DeepClone();
    }

    private static bool Sensitive(string key) => key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase) || key.Contains("key", StringComparison.OrdinalIgnoreCase)
        || key.Equals("psk", StringComparison.OrdinalIgnoreCase)
        || key.Equals("identityHint", StringComparison.OrdinalIgnoreCase)
        || key.Equals("content", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("customData", StringComparison.OrdinalIgnoreCase);
}
