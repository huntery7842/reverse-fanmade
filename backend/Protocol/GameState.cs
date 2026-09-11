using System.Security.Cryptography;
using System.Text;

namespace ReVerse.Capture.Protocol;


public sealed class GameState
{
    private SteamIdentity currentIdentity = new("local-player", "LocalPlayer");
    private readonly Dictionary<string, SteamIdentity> identities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SteamIdentity> identitiesByTokenHash = new(StringComparer.Ordinal);

    public SteamIdentity CurrentIdentity
    {
        get
        {
            lock (this)
                return currentIdentity;
        }
    }

    public void SetIdentity(SteamIdentity identity, string? token)
    {
        lock (this)
        {
            currentIdentity = identity;
            identities[identity.AccountId] = identity;
            if (!string.IsNullOrWhiteSpace(token))
                identitiesByTokenHash[HashToken(token)] = identity;
        }
    }

    public SteamIdentity ResolveIdentity(string? bearerToken = null, string? accountId = null, bool strict = false)
    {
        lock (this)
        {
            if (!string.IsNullOrWhiteSpace(bearerToken) &&
                identitiesByTokenHash.TryGetValue(HashToken(bearerToken), out var tokenIdentity))
                return tokenIdentity;

            if (strict && !string.IsNullOrWhiteSpace(bearerToken))
                throw new BadHttpRequestException("Unknown session token; sign in again.", 401);

            if (!string.IsNullOrWhiteSpace(accountId) && identities.TryGetValue(accountId, out var accountIdentity))
                return accountIdentity;

            if (strict) throw new BadHttpRequestException("Unknown player; sign in first.", 401);
            return currentIdentity;
        }
    }

    public SteamIdentity ResolveAccount(string accountId, bool strict = false)
    {
        lock (this)
        {
            if (identities.TryGetValue(accountId, out var identity))
                return identity;

            if (currentIdentity.AccountId.Equals(accountId, StringComparison.Ordinal))
                return currentIdentity;

            if (strict) throw new BadHttpRequestException("Unknown player.", 404);
            return new SteamIdentity(accountId, currentIdentity.Nickname);
        }
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
