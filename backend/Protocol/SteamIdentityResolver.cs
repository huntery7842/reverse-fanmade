using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReVerse.Capture.Configuration;

namespace ReVerse.Capture.Protocol;

public sealed class SteamIdentityResolver(
    HttpClient httpClient,
    SteamOptions options,
    SteamClientIdentityProvider localProvider,
    ILogger<SteamIdentityResolver> logger)
{
    private readonly ConcurrentDictionary<string, CachedIdentity> cache = new(StringComparer.Ordinal);

    public async Task<SteamIdentityResolution> ResolveAsync(
        string? authorization,
        CancellationToken cancellationToken)
    {
        var mode = options.Mode.Trim().ToLowerInvariant();

        if ((mode is "local-client" or "auto") && options.UseLocalClient &&
            localProvider.TryGetCurrent(out var localIdentity))
        {
            return new SteamIdentityResolution(localIdentity, "steam-client", "SteamUser/GetPersonaName");
        }

        if ((mode is "web-api" or "auto") && options.ApiKeyConfigured &&
            !string.IsNullOrWhiteSpace(options.TicketIdentity))
        {
            var ticket = ExtractRawHexTicket(authorization);
            if (ticket is not null)
            {
                var remote = await ResolveViaWebApiAsync(ticket, cancellationToken);
                if (remote is not null)
                    return remote;
            }
            else
            {
                logger.LogWarning("Steam Web API identity resolution skipped: sign Authorization is not a raw hex ticket");
            }
        }

        var detail = mode switch
        {
            "fallback" => "configured fallback mode",
            "web-api" when !options.ApiKeyConfigured => "Steam:ApiKey is not configured",
            "web-api" when string.IsNullOrWhiteSpace(options.TicketIdentity) =>
                "Steam:TicketIdentity is not configured",
            "local-client" => "Steam client provider unavailable",
            _ => "no Steam identity provider returned a result"
        };

        if (!options.AllowFallback)
        {
            logger.LogError("Steam identity resolution failed ({Detail}) and Steam:AllowFallback is false", detail);
            throw new InvalidOperationException($"Steam identity resolution failed: {detail}");
        }

        return new SteamIdentityResolution(
            new SteamIdentity(options.FallbackAccountId, options.FallbackNickname),
            "fallback",
            detail);
    }

    private async Task<SteamIdentityResolution?> ResolveViaWebApiAsync(
        string ticket,
        CancellationToken cancellationToken)
    {
        var ticketHash = HashTicket(ticket);
        if (cache.TryGetValue(ticketHash, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return new SteamIdentityResolution(cached.Identity, "steam-web-api-cache", "cached ticket resolution");

        try
        {
            var authUri = BuildEndpoint("ISteamUserAuth/AuthenticateUserTicket/v1/", new Dictionary<string, string>
            {
                ["appid"] = options.AppId.ToString(),
                ["ticket"] = ticket,
                ["identity"] = options.TicketIdentity
            });
            using var authRequest = NewApiRequest(authUri);
            using var authResponse = await httpClient.SendAsync(authRequest,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var authBody = await authResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!authResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Steam ticket validation returned HTTP {StatusCode} (ticket length {TicketLength}, hash {TicketHash})",
                    (int)authResponse.StatusCode, ticket.Length, ticketHash[..12]);
                return null;
            }

            using var authJson = JsonDocument.Parse(authBody);
            var result = FindString(authJson.RootElement, "result");
            if (!string.IsNullOrWhiteSpace(result) && !result.Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Steam ticket validation returned {Result} (ticket length {TicketLength}, hash {TicketHash})",
                    result, ticket.Length, ticketHash[..12]);
                return null;
            }

            var steamId = FindString(authJson.RootElement, "steamid");
            if (!ulong.TryParse(steamId, out var parsedSteamId) || parsedSteamId == 0)
            {
                logger.LogWarning("Steam ticket validation response contained no usable SteamID (ticket hash {TicketHash})",
                    ticketHash[..12]);
                return null;
            }

            var profileUri = BuildEndpoint("ISteamUser/GetPlayerSummaries/v2/", new Dictionary<string, string>
            {
                ["steamids"] = parsedSteamId.ToString()
            });
            using var profileRequest = NewApiRequest(profileUri);
            using var profileResponse = await httpClient.SendAsync(profileRequest,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var profileBody = await profileResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!profileResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Steam profile lookup returned HTTP {StatusCode} for SteamID {SteamId}",
                    (int)profileResponse.StatusCode, parsedSteamId);
                return null;
            }

            using var profileJson = JsonDocument.Parse(profileBody);
            if (!TryGetPlayer(profileJson.RootElement, parsedSteamId.ToString(), out var nickname, out var profileUrl))
            {
                logger.LogWarning("Steam profile lookup returned no persona name for SteamID {SteamId}", parsedSteamId);
                return null;
            }

            var identity = new SteamIdentity(parsedSteamId.ToString(), nickname, profileUrl);
            cache[ticketHash] = new CachedIdentity(identity,
                DateTimeOffset.UtcNow.AddSeconds(options.ProfileCacheSeconds));
            logger.LogInformation("Steam identity resolved via Web API: SteamID {SteamId}, nickname {Nickname}",
                identity.AccountId, identity.Nickname);
            return new SteamIdentityResolution(identity, "steam-web-api", "AuthenticateUserTicket + GetPlayerSummaries");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            JsonException or UriFormatException)
        {
            logger.LogWarning("Steam Web API identity resolution failed: {ErrorType}", exception.GetType().Name);
            return null;
        }
    }

    private HttpRequestMessage NewApiRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("x-webapi-key", options.ApiKey);
        return request;
    }

    private Uri BuildEndpoint(string relativePath, IReadOnlyDictionary<string, string> query)
    {
        var baseUri = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var endpoint = new Uri(baseUri, relativePath);
        var queryString = string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri(endpoint + "?" + queryString, UriKind.Absolute);
    }

    private static string? ExtractRawHexTicket(string? authorization)
    {
        var value = authorization?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
            value.Length % 2 != 0)
            return null;

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
                return null;
        }

        return value;
    }

    private static string HashTicket(string ticket) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(ticket)));

    private static string? FindString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
            }

            foreach (var property in element.EnumerateObject())
            {
                var result = FindString(property.Value, propertyName);
                if (result is not null)
                    return result;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var result = FindString(child, propertyName);
                if (result is not null)
                    return result;
            }
        }

        return null;
    }

    private static bool TryGetPlayer(JsonElement root, string steamId, out string nickname, out string? profileUrl)
    {
        nickname = "";
        profileUrl = null;
        if (!TryFindArray(root, "players", out var players))
            return false;

        foreach (var player in players.EnumerateArray())
        {
            if (player.ValueKind != JsonValueKind.Object)
                continue;

            var playerId = GetPropertyString(player, "steamid");
            if (!string.IsNullOrWhiteSpace(playerId) && playerId != steamId)
                continue;

            nickname = GetPropertyString(player, "personaname") ?? "";
            profileUrl = GetPropertyString(player, "profileurl");
            return !string.IsNullOrWhiteSpace(nickname);
        }

        return false;
    }

    private static bool TryFindArray(JsonElement element, string propertyName, out JsonElement array)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Array)
                {
                    array = property.Value;
                    return true;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindArray(property.Value, propertyName, out array))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                if (TryFindArray(child, propertyName, out array))
                    return true;
            }
        }

        array = default;
        return false;
    }

    private static string? GetPropertyString(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }

        return null;
    }

    private sealed record CachedIdentity(SteamIdentity Identity, DateTimeOffset ExpiresAt);
}
