using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReVerse.Capture.Protocol;





public sealed class DynamicRoutes(
    GameState state,
    SteamIdentityResolver steamIdentity,
    ILogger<DynamicRoutes> logger)
{
    private static readonly Regex MessagingMessage = new(@"^/v1/messaging/message$", RegexOptions.Compiled);
    private static readonly Regex BatchGetServiceId = new(@"^/v1/batch-get/service-id/?$", RegexOptions.Compiled);
    private static readonly Regex BatchGetServiceIdReverse = new(@"^/v1/batch-get/service-id/reverse/?$", RegexOptions.Compiled);
    private static readonly Regex PresenceGet = new(@"^/v1/presence/presence/get/?$", RegexOptions.Compiled);
    private static readonly Regex PresenceGameSession = new(@"^/v1/presence/gamesession/?$", RegexOptions.Compiled);
    private static readonly Regex Refresh = new(@"^/v1/refresh/(?<x>[^/]+)$", RegexOptions.Compiled);
    private static readonly Regex SignIn = new(@"^/v1/steam-steam/sign/RVS-B-WW$", RegexOptions.Compiled);
    private static readonly Regex TokenRefresh = new(@"^/v1/token/refresh/$", RegexOptions.Compiled);
    private static readonly Regex OpenService = new(@"^/v1/open/(?<x>[^/]+)$", RegexOptions.Compiled);
    private static readonly Regex VerifyService = new(@"^/v1/verify/(?<x>[^/]+)$", RegexOptions.Compiled);
    private static readonly Regex CloseService = new(@"^/v1/close/(?<x>[^/]+)$", RegexOptions.Compiled);
    private static readonly Regex Lastonemile = new(@"^/v1/lastonemile/playarea/(?<id>[^/]+)$", RegexOptions.Compiled);
    private static readonly Regex ServiceProfile = new(@"^/v1/service_profile/by_sub(?:/(?<sub>[^/]+))?$", RegexOptions.Compiled);
    private static readonly Regex Nickname = new(@"^/v1/nickname/by_match(?:/(?<sub>[^/]+))?$", RegexOptions.Compiled);









    private static readonly Regex RelationshipV2 = new(@"^/v2/relationship/relation(?:/(?<sub>[^/]+))?$", RegexOptions.Compiled);










    private static readonly Regex RelationshipFriendRequest = new(@"^/v2/relationship/friend/request(?:/(?<sub>[^/]+))?$", RegexOptions.Compiled);







    private static readonly Regex PlayareaUserAction = new(@"^/v1/playarea/user/(?<op>auto|set)$", RegexOptions.Compiled);






    public async Task<string?> MatchAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "";

        if (MessagingMessage.IsMatch(path)) return HandleMessaging(context, method);
        if (BatchGetServiceId.IsMatch(path)) return HandleBatchGetServiceId(context, false);
        if (BatchGetServiceIdReverse.IsMatch(path)) return HandleBatchGetServiceId(context, true);
        if (PresenceGet.IsMatch(path) || PresenceGameSession.IsMatch(path)) return HandlePresence(context);
        if (Refresh.IsMatch(path) && method == "POST") return await TokenResponseAsync(context, false);
        if (SignIn.IsMatch(path) && method == "POST") return await TokenResponseAsync(context, true);
        if (TokenRefresh.IsMatch(path) && method == "GET") return await TokenResponseAsync(context, false);
        if (OpenService.IsMatch(path) && method == "POST") return OpenResponse(context);
        if (VerifyService.IsMatch(path) && method is "GET" or "POST") return VerifyResponse(context);
        if (CloseService.IsMatch(path) && method == "POST") return JsonResponse(new { });
        if (ServiceProfile.IsMatch(path) && method == "GET") return ServiceProfileResponse(context, path);
        if (Nickname.IsMatch(path) && method == "GET") return NicknameResponse(context, path);
        if (RelationshipV2.IsMatch(path)) return RelationshipV2Response(context, path, method);
        if (RelationshipFriendRequest.IsMatch(path)) return RelationshipFriendRequestResponse(context, path, method);
        if (PlayareaUserAction.IsMatch(path) && method == "POST") return PlayareaUserActionResponse();
        if (Lastonemile.IsMatch(path) && method == "GET") return HandleLastonemile(path);

        return null;
    }


    private string? HandleMessaging(HttpContext context, string method)
    {
        if (method == "POST") return JsonResponse(new { messageId = Guid.NewGuid().ToString("N") });
        if (method != "GET") return null;






        return JsonResponse(new { messages = Array.Empty<object>() });
    }

    private string HandleBatchGetServiceId(HttpContext context, bool reverse)
    {
        var accountId = IdentityFor(context).AccountId;
        return reverse
            ? JsonResponse(new { matched = new[] { new { sub = accountId } }, unmatched = Array.Empty<object>() })
            : JsonResponse(new
            {
                matched = new[] { new { service = "steam", sub = accountId, service_user_id = accountId } },
                unmatched = Array.Empty<object>()
            });
    }

    private string HandlePresence(HttpContext context)
    {
        var accountId = IdentityFor(context).AccountId;
        return JsonResponse(new
        {
            presences = new[]
            {
                new
                {
                    accountId,
                    online = true,
                    lastOfflineChangedAt = 0,
                    apps = new[]
                    {
                        new
                        {
                            app = "reverse",
                            platform = "steam",
                            service = "steam",
                            userId = accountId,
                            gamesessionIds = Array.Empty<string>()
                        }
                    }
                }
            }
        });
    }

    private async Task<string> TokenResponseAsync(HttpContext context, bool resolveTicket)
    {
        SteamIdentity identity;
        SteamIdentityResolution resolution;
        if (resolveTicket)
        {
            var relay = RelayIdentity.Read(context);
            resolution = relay is not null
                ? new SteamIdentityResolution(relay, "player-relay", "secret-key account")
                : await steamIdentity.ResolveAsync(
                    context.Request.Headers.Authorization.FirstOrDefault(), context.RequestAborted);
            identity = resolution.Identity;
        }
        else
        {
            identity = IdentityFor(context);
            resolution = new SteamIdentityResolution(identity, "existing-session", "reused backend session identity");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var token = RebeTokenFactory.New(now);
        state.SetIdentity(identity, token);
        if (resolveTicket)
        {
            logger.LogInformation(
                "Steam sign-in identity: source {Source}, account {AccountId}, nickname {Nickname}, detail {Detail}",
                resolution.Source, identity.AccountId, identity.Nickname, resolution.Detail);
        }

        return JsonResponse(new
        {
            rebe_token = token,
            gcp_token = "local-gcp-dev",
            gcp_token_expire = now + RebeTokenFactory.LifetimeSeconds
        });
    }

    private string OpenResponse(HttpContext context)
    {



        var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400;
        var identity = IdentityFor(context);
        var key = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        state.SetIdentity(identity, key);
        return JsonResponse(new { expires_at = expiresAt, session_key = key });
    }

    private string VerifyResponse(HttpContext context)
    {
        var identity = IdentityFor(context);
        return JsonResponse(new { id_token = new { sub = identity.AccountId, sub_nickname = identity.Nickname } });
    }

    private string ServiceProfileResponse(HttpContext context, string path)
    {
        var sub = SubFrom(context, path, ServiceProfile);
        var identity = state.ResolveAccount(sub, RelayIdentity.Enabled(context));
        return JsonResponse(new
        {
            users = new[]
            {
                new
                {
                    service = "steam",
                    sub,
                    service_profiles = new[]
                    {
                        new { service = "steam", service_user_id = sub, nickname = identity.Nickname }
                    }
                }
            }
        });
    }

    private string NicknameResponse(HttpContext context, string path)
    {
        var sub = SubFrom(context, path, Nickname);
        var identity = state.ResolveAccount(sub, RelayIdentity.Enabled(context));
        return JsonResponse(new
        {
            users = new[]
            {
                new
                {
                    service = "steam",
                    sub,
                    nicknames = new[] { new { service = "steam", nickname = identity.Nickname } }
                }
            }
        });
    }

    private string SubFrom(HttpContext context, string path, Regex route) =>
        route.Match(path).Groups["sub"].Success ? route.Match(path).Groups["sub"].Value : IdentityFor(context).AccountId;








    private string RelationshipV2Response(HttpContext context, string path, string method)
    {
        if (method != "GET") return JsonResponse(new { });
        var sub = SubFrom(context, path, RelationshipV2);
        return JsonResponse(new
        {
            amount = 1,
            relations = new[]
            {
                new { accountId = sub, createdAt = 0 }
            }
        });
    }









    private string RelationshipFriendRequestResponse(HttpContext context, string path, string method)
    {
        if (method != "GET") return JsonResponse(new { });
        var sub = SubFrom(context, path, RelationshipFriendRequest);
        var requestType = context.Request.Query["requestType"].FirstOrDefault();
        if (string.IsNullOrEmpty(requestType)) requestType = "FRIEND_REQ";
        return JsonResponse(new
        {
            amount = 1,
            requestType,
            requestList = new[]
            {
                new { accountId = sub, createdAt = 0 }
            }
        });
    }

    private static string HandleLastonemile(string path)
    {
        var id = Lastonemile.Match(path).Groups["id"].Value;






        return JsonResponse(new
        {
            playarea = id,
            region = id,
            set = new { area = id, region = id },
            auto = new { area = id, region = id },
            areas = new[] { new { name = id, regions = new[] { new { name = id, state = "up", latency = 1 } } } }
        });
    }




    private static string PlayareaUserActionResponse() =>
        JsonResponse(new
        {
            set = new { area = "RVS-B-WW", region = "RVS-B-WW" },
            auto = new { area = "RVS-B-WW", region = "RVS-B-WW" },
            areas = new[] { new { name = "RVS-B-WW", regions = new[] { new { name = "RVS-B-WW", state = "up", latency = 1 } } } }
        });

    private SteamIdentity IdentityFor(HttpContext context)
    {
        var bearer = context.Request.Headers.Authorization.FirstOrDefault();
        if (bearer?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            bearer = bearer["Bearer ".Length..].Trim();
        else
            bearer = null;

        var relay = RelayIdentity.Read(context);
        var identity = state.ResolveIdentity(bearer, relay?.AccountId ?? context.Request.Headers["X-Be-Account-Id"].FirstOrDefault(), relay is not null);
        if (relay is not null && identity.AccountId != relay.AccountId)
            throw new BadHttpRequestException("Session belongs to a different relay player.", 401);
        return identity;
    }

    private static string JsonResponse(object value) => JsonSerializer.Serialize(value);
}
