using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Features;
using ReVerse.Capture.Capturing;
using ReVerse.Capture.Protocol;

namespace ReVerse.Capture.Matchmaking;


public sealed class MatchmakingEndpoints(MatchmakingCoordinator coordinator, GameState state, RequestLog log)
{
    public static bool Owns(PathString path) => path.StartsWithSegments("/v1/gameSession") || path.StartsWithSegments("/v1/matchmaking");

    public static SteamIdentity Authenticate(HttpContext context, GameState state)
    {
        var identity = RelayIdentity.Read(context) ?? throw new BadHttpRequestException("Matchmaking requires an authenticated player relay.", 401);
        var key = context.Request.Headers["X-Auth-Session-Key"].ToString();
        if (!string.IsNullOrEmpty(key) && state.ResolveIdentity(key, strict: true).AccountId != identity.AccountId)
            throw new BadHttpRequestException("Game session key belongs to another player.", 401);
        var bearer = context.Request.Headers.Authorization.ToString();
        if (bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && state.ResolveIdentity(bearer[7..].Trim(), strict: true).AccountId != identity.AccountId)
            throw new BadHttpRequestException("Bearer token belongs to another player.", 401);
        return identity;
    }

    public async Task HandleAsync(HttpContext context)
    {
        const int limit = 256 * 1024;
        var identity = Authenticate(context, state);
        var account = identity.AccountId;
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "";
        JsonObject? request = null;
        JsonObject response;
        var status = 200;
        var reason = "";
        try
        {
            var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = limit;
            if (context.Request.ContentLength > limit) throw MatchmakingCoordinator.Error(413, "Matchmaking body exceeds 256 KiB.");
            var bytes = await BodyCapture.ReadAsync(context.Request.Body, limit, false, context.RequestAborted);
            if (bytes.Truncated) throw MatchmakingCoordinator.Error(413, "Matchmaking body exceeds 256 KiB.");
            if (bytes.ReadError is not null) throw MatchmakingCoordinator.Error(400, "Could not read matchmaking request.");
            request = bytes.TotalBytes == 0 ? new() : JsonNode.Parse(bytes.Data) as JsonObject
                ?? throw MatchmakingCoordinator.Error(400, "JSON object required.");
            if (path == "/v1/matchmaking/ticket")
                response = method switch
                {
                    "POST" => coordinator.Submit(account, request),
                    "DELETE" => coordinator.Cancel(account, Header(context, "X-Be-Ticket-Id")),
                    _ => throw MatchmakingCoordinator.Error(405, "Use POST or DELETE.")
                };
            else if (path.StartsWith("/v1/gameSession", StringComparison.Ordinal))
            {
                var read = path == "/v1/gameSession" && method == "GET" ? GameSessionRead.Parse(context.Request.Query) : null;
                if (read is null && context.Request.Query.Keys.Any(k => k is "fields" or "joinStateFilter" or "usePlayerSessionFilter"))
                    throw MatchmakingCoordinator.Error(400, "Session-read parameters are only valid on GET /v1/gameSession.");
                var ids = Header(context, path == "/v1/gameSession" && method == "GET" ? "X-Be-Session-Ids" : "X-Be-Session-Id")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (ids.Length > 10) throw MatchmakingCoordinator.Error(400, "Too many session IDs.");
                var reasons = context.Request.Query["reason"];
                if (reasons.Count > 1) throw MatchmakingCoordinator.Error(400, "Duplicate reason query parameter.");
                reason = reasons.ToString();
                response = coordinator.SessionRequest(account, identity.Nickname, path, method, ids, Header(context, "X-Be-Account-Id"), request, read,
                    Header(context, "X-Be-Session-Keyword"), reason);
            }
            else throw MatchmakingCoordinator.Error(404, "Unknown matchmaking route.");
        }
        catch (BadHttpRequestException ex) { status = ex.StatusCode; response = new() { ["error"] = ex.Message }; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        { status = 400; response = new() { ["error"] = "Invalid matchmaking JSON field type." }; }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        var payload = System.Text.Encoding.UTF8.GetBytes(response.ToJsonString());
        context.Response.ContentLength = payload.Length;
        await log.WriteAsync(new
        {
            type = "matchmakingRequest", timestampUtc = DateTimeOffset.UtcNow, account, method, path,
            sessionRead = path == "/v1/gameSession" && method == "GET" ? GameSessionRead.Audit(context.Request) : null,
            request = ProtocolAudit.Redact(request), responseStatusCode = status, response = ProtocolAudit.Redact(response),
            responseSource = "matchmaking", reason
        });
        await context.Response.Body.WriteAsync(payload, context.RequestAborted);
    }

    private static string Header(HttpContext context, string name)
    {
        var values = context.Request.Headers[name];
        if (values.Count > 1) throw MatchmakingCoordinator.Error(400, $"Duplicate {name} header.");
        return values.ToString();
    }
}
