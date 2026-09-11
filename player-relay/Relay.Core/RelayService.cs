using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReVerse.Relay;

public sealed class RelayService : IAsyncDisposable
{
    public const string PlayerHeader = "X-Relay-Player-Id";
    public const string NameHeader = "X-Relay-Username";
    public const string SessionHeader = "X-Relay-Session-Token";
    public const string NgrokSkipBrowserWarningHeader = "ngrok-skip-browser-warning";
    private string sessionToken = "";
    private readonly HttpClient client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(10)
    }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly CancellationTokenSource stopping = new();
    private WebApplication? app;
    private X509Certificate2? certificate;
    public event Action<string>? StatusChanged;

    public async Task StartAsync(RelaySettings settings, int httpPort = 5080, int httpsPort = 5081,
        string? certificateDirectory = null)
    {
        if (app is not null) throw new InvalidOperationException("Relay is already running.");
        var upstream = settings.Validate();
        var addresses = await Dns.GetHostAddressesAsync(upstream.DnsSafeHost);
        if (addresses.Any(IPAddress.IsLoopback) && (upstream.Port == httpPort || upstream.Port == httpsPort))
            throw new ArgumentException("The backend address points back to this relay. Use the shared server's address and port.");
        StatusChanged?.Invoke("Signing in…");
        using var loginTimeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        loginTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var login = await client.PostAsJsonAsync(new Uri(upstream, "/relay/account/login"),
            new { username = settings.Username, secretKey = settings.SecretKey }, loginTimeout.Token);
        if (!login.IsSuccessStatusCode)
            throw new HttpRequestException($"Account sign-in failed (HTTP {(int)login.StatusCode}). Check that the backend supports relay accounts and has Relay__Enabled=true.");
        var account = await login.Content.ReadFromJsonAsync<RelayAccount>(loginTimeout.Token)
            ?? throw new HttpRequestException("Backend returned an empty account response.");
        if (!Guid.TryParseExact(account.AccountId, "N", out _) || account.SessionToken is not { Length: 64 } ||
            !account.SessionToken.All(Uri.IsHexDigit) || string.IsNullOrEmpty(account.Username))
            throw new HttpRequestException("Backend returned an invalid account response.");
        sessionToken = account.SessionToken;
        var playerId = account.AccountId;
        var name = Convert.ToBase64String(Encoding.UTF8.GetBytes(account.Username));
        certificate = RelayCertificate.LoadOrCreate(certificateDirectory ?? RelaySettings.DataDirectory);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Limits.MaxRequestBodySize = null;
            o.Listen(IPAddress.Loopback, httpPort);
            o.Listen(IPAddress.Loopback, httpsPort, listen => listen.UseHttps(certificate));
        });
        app = builder.Build();
        app.UseWebSockets();
        app.Run(async context =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping.Token);
            try
            {
                var raw = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? "/";
                if (!raw.StartsWith('/') || raw.StartsWith("//") || raw.Contains('#'))
                {
                    context.Response.StatusCode = 400;
                    return;
                }
                var target = new Uri(upstream.GetLeftPart(UriPartial.Authority) + raw);
                if (context.WebSockets.IsWebSocketRequest)
                    await ForwardWebSocket(context, target, playerId, name, linked.Token);
                else
                    await ForwardHttp(context, target, playerId, name, linked.Token);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { context.Abort(); }
            catch (Exception ex) when (ex is HttpRequestException or WebSocketException or IOException or OperationCanceledException)
            {
                StatusChanged?.Invoke("Running — backend connection failed");
                if (context.Response.HasStarted) context.Abort();
                else { context.Response.StatusCode = 502; await context.Response.WriteAsync("Backend connection failed."); }
            }
        });
        try { await app.StartAsync(); }
        catch { await app.DisposeAsync(); app = null; certificate.Dispose(); certificate = null; throw; }
        StatusChanged?.Invoke("Running — waiting for game");
    }

    private static HashSet<string> Excluded(IEnumerable<string> connectionValues)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Host", "Connection", "Keep-Alive", "Proxy-Connection", "Proxy-Authenticate", "Proxy-Authorization",
          "TE", "Trailer", "Transfer-Encoding", "Upgrade", PlayerHeader, NameHeader, SessionHeader,
          NgrokSkipBrowserWarningHeader };
        foreach (var value in connectionValues)
            foreach (var item in value.Split(',')) result.Add(item.Trim());
        return result;
    }

    private async Task ForwardHttp(HttpContext context, Uri target, string playerId, string name, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
            request.Content = new StreamContent(context.Request.Body);
        var excluded = Excluded(context.Request.Headers.Connection.Select(v => v ?? ""));
        foreach (var header in context.Request.Headers)
        {
            if (excluded.Contains(header.Key)) continue;
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                if (request.Content is null && header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                    request.Content = new ByteArrayContent([]);
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }
        request.Headers.TryAddWithoutValidation(PlayerHeader, playerId);
        request.Headers.TryAddWithoutValidation(NameHeader, name);
        request.Headers.TryAddWithoutValidation(SessionHeader, sessionToken);
        request.Headers.TryAddWithoutValidation(NgrokSkipBrowserWarningHeader, "1");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        context.Response.StatusCode = (int)response.StatusCode;
        var responseExcluded = Excluded(response.Headers.Connection);
        foreach (var header in response.Headers.Concat(response.Content.Headers))
            if (!responseExcluded.Contains(header.Key)) context.Response.Headers[header.Key] = header.Value.ToArray();

        if (response.Headers.Location is { IsAbsoluteUri: true } location && location.Authority == target.Authority)
            context.Response.Headers.Location = $"{context.Request.Scheme}://{context.Request.Host}{location.PathAndQuery}{location.Fragment}";
        if (!HttpMethods.IsHead(context.Request.Method) && (int)response.StatusCode is not (204 or 205 or 304))
            await response.Content.CopyToAsync(context.Response.Body, ct);
        StatusChanged?.Invoke(response.StatusCode == HttpStatusCode.Unauthorized
            ? "Session expired — stop and start service"
            : "Running — connected to backend");
    }

    private async Task ForwardWebSocket(HttpContext context, Uri target, string playerId, string name, CancellationToken ct)
    {
        using var remote = new ClientWebSocket();
        var excluded = Excluded(context.Request.Headers.Connection.Select(v => v ?? ""));
        foreach (var header in context.Request.Headers)
            if (!excluded.Contains(header.Key) && !header.Key.StartsWith("Sec-WebSocket-", StringComparison.OrdinalIgnoreCase))
                remote.Options.SetRequestHeader(header.Key, header.Value.ToString());
        foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols) remote.Options.AddSubProtocol(protocol);
        remote.Options.SetRequestHeader(PlayerHeader, playerId);
        remote.Options.SetRequestHeader(NameHeader, name);
        remote.Options.SetRequestHeader(SessionHeader, sessionToken);
        remote.Options.SetRequestHeader(NgrokSkipBrowserWarningHeader, "1");
        var wsTarget = new UriBuilder(target) { Scheme = target.Scheme == "https" ? "wss" : "ws" };
        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            await remote.ConnectAsync(wsTarget.Uri, connectTimeout.Token);
        }
        using var local = await context.WebSockets.AcceptWebSocketAsync(remote.SubProtocol);
        StatusChanged?.Invoke("Running — connected to backend");
        using var pumps = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var toRemote = Pump(local, remote, pumps.Token);
        var toLocal = Pump(remote, local, pumps.Token);
        try
        {
            var first = await Task.WhenAny(toRemote, toLocal);
            await first;
            await Task.WhenAll(toRemote, toLocal).WaitAsync(TimeSpan.FromSeconds(3), ct);
        }
        catch (TimeoutException) { }
        finally
        {
            await pumps.CancelAsync();
            local.Abort(); remote.Abort();
            try { await Task.WhenAll(toRemote, toLocal); }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        }
    }

    private static async Task Pump(WebSocket source, WebSocket destination, CancellationToken ct)
    {
        var buffer = new byte[32768];
        while (true)
        {
            var result = await source.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (destination.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await destination.CloseOutputAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription, ct);
                return;
            }
            await destination.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count),
                result.MessageType, result.EndOfMessage, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); app = null; }
        client.Dispose(); certificate?.Dispose(); stopping.Dispose();
        sessionToken = "";
    }

    private sealed record RelayAccount(string AccountId, string Username, string SessionToken);
}
