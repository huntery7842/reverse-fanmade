using System.Text.Json.Nodes;

namespace ReVerse.Capture.Matchmaking;


public sealed class GameSessionRead
{
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "sessionId", "createdTimestamp", "maxPlayers", "maxSpectators", "member(players)",
        "member(spectators)", "member(players(joinState))", "member(spectators(joinState))",
        "member(players(customData1))", "member(spectators(customData1))", "serviceProfiles",
        "serviceEncryptionKey", "joinDisabled", "supportedServices", "representative",
        "customData1", "customData2", "usePlayerSession", "useCrossPlay", "matchmaking", "signaling", "@default"
    };
    private readonly HashSet<string>? selections;
    private readonly HashSet<string> states;
    public bool? UsePlayerSession { get; }

    private GameSessionRead(HashSet<string>? selections, HashSet<string> states, bool? usePlayerSession)
    {
        this.selections = selections;
        this.states = states;
        UsePlayerSession = usePlayerSession;
    }

    public static GameSessionRead Parse(IQueryCollection query)
    {
        if (query.Keys.Any(k => k is not ("fields" or "joinStateFilter" or "usePlayerSessionFilter")))
            throw MatchmakingCoordinator.Error(400, "Unsupported session-read query parameter.");
        HashSet<string>? selections = null;
        var fields = Single(query, "fields");

        if (!string.IsNullOrEmpty(fields))
        {
            selections = Tokens(fields);
            if (!selections.IsSubsetOf(Fields)) throw MatchmakingCoordinator.Error(400, "Unsupported session-read field selection.");
            if (selections.Contains("@default")) selections = null;
        }
        var states = Single(query, "joinStateFilter") is { } filter ? Tokens(filter) : new HashSet<string> { "RESERVED", "JOINED" };
        if (states.Count == 0 || states.Any(s => s is not ("RESERVED" or "JOINED")))
            throw MatchmakingCoordinator.Error(400, "joinStateFilter requires RESERVED and/or JOINED.");
        bool? usePlayerSession = Single(query, "usePlayerSessionFilter") switch
        {
            null => null, "false" => false, "true" => true,
            _ => throw MatchmakingCoordinator.Error(400, "usePlayerSessionFilter must be true or false.")
        };
        return new(selections, states, usePlayerSession);
    }

    private static string? Single(IQueryCollection query, string name)
    {
        if (!query.TryGetValue(name, out var values)) return null;
        if (values.Count != 1 || values[0] is null || values[0]!.Length > 4096)
            throw MatchmakingCoordinator.Error(400, "Duplicate or oversized session-read parameter.");
        return values[0];
    }

    private static HashSet<string> Tokens(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length > 32 || parts.Any(string.IsNullOrEmpty))
            throw MatchmakingCoordinator.Error(400, "Invalid session-read list.");
        return new(parts, StringComparer.Ordinal);
    }

    private bool Wants(string name) => selections is null || selections.Contains(name);

    internal JsonObject Serialize(GameSessionRegistry.Session session)
    {
        var members = new JsonArray();
        foreach (var account in session.Reserved)
        {
            var joined = session.Players.TryGetValue(account, out var player);
            if (!states.Contains(joined ? "JOINED" : "RESERVED")) continue;


            members.Add(joined ? player!.DeepClone() : new JsonObject
            {
                ["accountId"] = account, ["platform"] = "steam", ["serviceProfiles"] = new JsonArray(),
                ["joinState"] = "RESERVED", ["joinTimestamp"] = 0, ["customData1"] = ""
            });
        }



        var result = new JsonObject
        {
            ["sessionId"] = session.Id, ["gameSessionSequenceNo"] = session.Sequence,
            ["createdTimestamp"] = session.Created,
            ["representative"] = new JsonObject { ["accountId"] = session.Representative },
            ["maxPlayers"] = session.Capacity, ["serviceEncryptionKey"] = session.ServiceEncryptionKey,
            ["signaling"] = session.SignalingStatus,
            ["member"] = new JsonObject { ["players"] = members, ["spectators"] = new JsonArray() }
        };
        if (Wants("maxSpectators")) result["maxSpectators"] = 0;
        if (Wants("joinDisabled")) result["joinDisabled"] = session.JoinDisabled;
        if (Wants("customData1")) result["customData1"] = session.CustomData1;
        if (Wants("customData2")) result["customData2"] = session.CustomData2;
        if (Wants("useCrossPlay")) result["useCrossPlay"] = session.UseCrossPlay;
        if (Wants("usePlayerSession")) result["usePlayerSession"] = session.UsePlayerSession;
        if (Wants("supportedServices")) result["supportedService"] = new JsonArray("steam");
        if (Wants("matchmaking")) result["matchmaking"] = new JsonObject { ["offerId"] = session.OfferId };
        return result;
    }

    public static JsonObject Audit(HttpRequest request)
    {
        var query = new JsonObject();
        foreach (var name in new[] { "fields", "joinStateFilter", "usePlayerSessionFilter" })
        {
            if (!request.Query.TryGetValue(name, out var values)) continue;

            var value = values.Count == 1 ? values[0] ?? "" : "[INVALID]";
            var valid = value.Length <= 4096 && (name switch
            {
                "fields" => value.Length == 0 || value.Split(',').All(v => Fields.Contains(v.Trim())),
                "joinStateFilter" => value.Split(',').All(v => v.Trim() is "RESERVED" or "JOINED"),
                _ => value is "true" or "false"
            });
            query[name] = valid ? value : "[INVALID]";
        }
        var ids = new JsonArray();
        foreach (var id in request.Headers["X-Be-Session-Ids"].ToString().Split(',').Take(10))
            if (Guid.TryParseExact(id.Trim(), "N", out var parsed)) ids.Add(parsed.ToString("N"));
        return new() { ["query"] = query, ["sessionIds"] = ids };
    }
}
