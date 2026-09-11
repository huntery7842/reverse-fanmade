namespace ReVerse.Capture.Protocol;


public static class RelayIdentity
{
    public static bool Enabled(HttpContext context) =>
        context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("Relay:Enabled");

    public static SteamIdentity? Read(HttpContext context)
    {
        if (!Enabled(context)) return null;
        if (context.Items[typeof(RelayIdentity)] is SteamIdentity cached) return cached;
        var token = context.Request.Headers["X-Relay-Session-Token"];
        if (token.Count != 1) throw new BadHttpRequestException("Start the relay to sign in.", 401);
        var identity = context.RequestServices.GetRequiredService<RelayAccounts>().Resolve(token[0]);
        context.Items[typeof(RelayIdentity)] = identity;
        return identity;
    }
}
