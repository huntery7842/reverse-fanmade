using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReVerse.Capture.Matchmaking;


internal static class GameJoinProtocol
{
    public static JsonObject ParsePlayer(string account, JsonObject body)
    {
        if (body["players"] is not JsonArray { Count: 1 } players || players[0] is not JsonObject player)
            throw MatchmakingCoordinator.Error(400, "Exactly one player is required.");
        if (MatchmakingCoordinator.RequiredString(player, "accountId") != account)
            throw MatchmakingCoordinator.Error(403, "Join player must match the authenticated account; restart the game after updating.");
        if (MatchmakingCoordinator.RequiredString(player, "joinState") != "JOINED")
            throw MatchmakingCoordinator.Error(400, "Only JOINED player admission is supported.");
        var custom = MatchmakingCoordinator.RequiredString(player, "customData1");
        _ = ReadNonce(custom);
        return new JsonObject
        {
            ["accountId"] = account, ["platform"] = "steam", ["serviceProfiles"] = new JsonArray(),
            ["joinState"] = "JOINED", ["joinTimestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["customData1"] = custom
        };
    }

    internal static uint ReadNonce(string custom)
    {
        if (string.IsNullOrWhiteSpace(custom) || custom.Length > 16384)
            throw MatchmakingCoordinator.Error(400, "Invalid member metadata length.");
        JsonObject? metadata;
        try { metadata = JsonNode.Parse(custom) as JsonObject; }
        catch (JsonException) { throw MatchmakingCoordinator.Error(400, "customData1 must contain JSON version and nonce."); }


        if (metadata is null || !Integer(metadata["version"]) || !Integer(metadata["nonce"]))
            throw MatchmakingCoordinator.Error(400, "customData1 requires integer version and nonce.");
        var nonce = (JsonValue)metadata["nonce"]!;

        return nonce.TryGetValue<ulong>(out var unsigned) ? unchecked((uint)unsigned)
            : unchecked((uint)nonce.GetValue<long>());
    }

    private static bool Integer(JsonNode? value) => value is JsonValue number
        && (number.TryGetValue<long>(out _) || number.TryGetValue<ulong>(out _));

    public static JsonObject Reply(string session, JsonObject player) => new()
    {
        ["sessionId"] = session, ["players"] = new JsonArray(player.DeepClone()),



        ["serviceEncryptionKey"] = "", ["keyword"] = ""
    };

    public static JsonObject CreatedEvent(JsonObject player) => new()
    {
        ["member"] = new JsonObject { ["players"] = new JsonArray(player.DeepClone()) }
    };
}
