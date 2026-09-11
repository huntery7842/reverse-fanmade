using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using ReVerse.Capture.Protocol;

namespace ReVerse.Capture.Persistence;

public static class SaveEndpoints
{
    public static async Task Handle(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var identity = RelayIdentity.Read(context);
        if (identity is null) { context.Response.StatusCode = 404; return; }
        var segments = (context.Request.Path.Value ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3) { context.Response.StatusCode = 404; return; }
        var slot = segments[2];
        SaveRepository.ValidateSlot(slot);

        var accountId = identity.AccountId;
        var repository = context.RequestServices.GetRequiredService<SaveRepository>();
        SaveSnapshot snapshot;
        if (HttpMethods.IsGet(context.Request.Method))
        {
            long? revision = null;
            if (context.Request.Query.TryGetValue("revision", out var value))
            {
                if (value.Count != 1 || !long.TryParse(value[0], out var parsed) || parsed < 1)
                    throw new BadHttpRequestException("Invalid revision.", 400);
                revision = parsed;
            }
            snapshot = repository.Get(accountId, slot, revision);
        }
        else if (HttpMethods.IsPut(context.Request.Method))
        {
            if (!context.Request.HasJsonContentType()) { context.Response.StatusCode = 415; return; }
            const int maxJson = 12 * 1024 * 1024;
            var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = maxJson;
            if (context.Request.ContentLength > maxJson) throw new BadHttpRequestException("Save request too large.", 413);
            SaveWrite? write;
            try { write = await context.Request.ReadFromJsonAsync<SaveWrite>(context.RequestAborted); }
            catch (JsonException) { throw new BadHttpRequestException("Invalid save JSON or missing required fields.", 400); }
            if (write is null) throw new BadHttpRequestException("Save body is required.", 400);
            snapshot = repository.Put(accountId, slot, write);
        }
        else
        {
            context.Response.StatusCode = 405;
            context.Response.Headers.Allow = "GET, PUT";
            return;
        }
        context.Response.Headers.ETag = $"\"{snapshot.Revision}\"";
        await context.Response.WriteAsJsonAsync(snapshot);
    }
}
