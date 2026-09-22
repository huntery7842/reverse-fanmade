using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Features;
using ReVerse.Capture.Capturing;
using ReVerse.Capture.Configuration;
using ReVerse.Capture.Protocol;
using ReVerse.Capture.Matchmaking;
using ReVerse.Traffic;

namespace ReVerse.Capture.Middleware;







public sealed class ProtocolMiddleware(
    RequestLog log,
    CaptureOptions capture,
    ResponseOverrides overrides,
    ContractOverrides contract,
    DynamicRoutes routes,
    DetailedTrafficLog detailed,
    ILogger<ProtocolMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            await HandleWebSocketAsync(context);
            return;
        }

        await HandleHttpAsync(context);
    }

    private async Task HandleHttpAsync(HttpContext context)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        context.Response.Headers["X-Capture-Id"] = correlationId;
        var receivedAt = DateTimeOffset.UtcNow;
        var headers = RedactedHeaders(context);

        var body = await BodyCapture.ReadAsync(context.Request.Body, capture.MaxCapturedBodyBytes,
            BodyContentType.IsBinary(context.Request.ContentType), context.RequestAborted);

        var (status, contentType, responseBody, responseSource) = await ResolveResponseAsync(context, body);

        byte[]? responseBytes = null;
        string? responseError = body.ReadError;
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        if (body.ReadError is null && status is not (204 or 205 or 304) && !HttpMethods.IsHead(context.Request.Method))
        {



            responseBytes = Encoding.UTF8.GetBytes(responseBody ?? capture.ResponseBody);
            context.Response.ContentLength = responseBytes.Length;
            try
            {
                await context.Response.Body.WriteAsync(responseBytes);
                await context.Response.CompleteAsync();
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
                responseError = exception.GetType().Name;
                logger.LogWarning(exception, "Response delivery failed for {Method} {Path}",
                    context.Request.Method, context.Request.Path.Value);
            }
        }
        else
        {
            try
            {
                await context.Response.CompleteAsync();
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
                responseError ??= exception.GetType().Name;
                logger.LogWarning(exception, "Server response completion failed for {Method} {Path}",
                    context.Request.Method, context.Request.Path.Value);
            }
        }

        await log.WriteAsync(new
        {
            type = "httpRequest",
            timestampUtc = receivedAt,
            completedAtUtc = DateTimeOffset.UtcNow,
            correlationId,
            method = context.Request.Method,
            path = context.Request.Path.Value,
            rawQuery = context.Request.QueryString.Value,
            rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget,
            scheme = context.Request.Scheme,
            host = context.Request.Host.Value,
            protocol = context.Request.Protocol,
            remoteAddress = context.Connection.RemoteIpAddress?.ToString(),
            headers,
            sensitiveHeadersRedacted = capture.RedactSensitiveHeaders,
            body,
            responseStatusCode = status,
            responseSource,
            responseContentLength = responseBytes?.Length,
            responseBody = CaptureResponse(context, responseBytes, contentType),
            responseBodyRedacted = context.Request.Path.StartsWithSegments("/v1/open"),


            serverResponseCompleted = responseError is null,
            error = responseError
        });
    }

    private BodyCapture CaptureResponse(HttpContext context, byte[]? bytes, string contentType)
    {
        if (bytes is null) return BodyCapture.Empty;

        if (context.Request.Path.StartsWithSegments("/v1/open"))
        {
            try
            {
                bytes = Encoding.UTF8.GetBytes(ProtocolAudit.Redact(JsonNode.Parse(bytes))!.ToJsonString());
            }
            catch (System.Text.Json.JsonException) { return BodyCapture.Empty; }
        }
        return BodyCapture.Create(bytes[..Math.Min(bytes.Length, capture.MaxCapturedBodyBytes)],
            bytes.Length, forceBinary: BodyContentType.IsBinary(contentType));
    }

    private async Task HandleWebSocketAsync(HttpContext context)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var trafficId = context.Items[DetailedTrafficMiddleware.ContextKey] as string ?? correlationId;
        context.Response.Headers["X-Capture-Id"] = correlationId;
        var receivedAt = DateTimeOffset.UtcNow;

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await log.WriteAsync(new
        {
            type = "httpRequest",
            timestampUtc = receivedAt,
            completedAtUtc = DateTimeOffset.UtcNow,
            correlationId,
            method = context.Request.Method,
            path = context.Request.Path.Value,
            rawQuery = context.Request.QueryString.Value,
            rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget,
            scheme = context.Request.Scheme,
            host = context.Request.Host.Value,
            protocol = context.Request.Protocol,
            remoteAddress = context.Connection.RemoteIpAddress?.ToString(),
            headers = RedactedHeaders(context),
            sensitiveHeadersRedacted = capture.RedactSensitiveHeaders,
            body = BodyCapture.Empty,
            responseStatusCode = StatusCodes.Status101SwitchingProtocols,
            error = (string?)null
        });

        var buffer = new byte[32 * 1024];
        long messageBytes = 0;
        var messageCapturedBytes = 0;
        long messageIndex = 0;





        var suppressEchoForMessage = false;



        var commandPrefix = new byte[256];
        var commandPrefixLength = 0;
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                detailed.Write("webSocketFrame", new
                {
                    id = trafficId, direction = "clientToBackend", remoteAddress = context.Connection.RemoteIpAddress?.ToString(),
                    path = context.Request.Path.Value, messageType = result.MessageType.ToString(), result.EndOfMessage,
                    closeStatus = result.CloseStatus?.ToString(), result.CloseStatusDescription,
                    payload = DetailedTrafficLog.Payload(buffer.AsSpan(0, result.Count))
                });
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await log.WriteAsync(new
                    {
                        type = "webSocketClose",
                        timestampUtc = DateTimeOffset.UtcNow,
                        correlationId,
                        path = context.Request.Path.Value,
                        closeStatus = result.CloseStatus?.ToString(),
                        closeDescription = result.CloseStatusDescription
                    });
                    await socket.CloseOutputAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription, context.RequestAborted);
                    break;
                }

                var keep = Math.Min(result.Count, capture.MaxCapturedBodyBytes - messageCapturedBytes);
                messageBytes += result.Count;
                messageCapturedBytes += keep;






                if (result.MessageType == WebSocketMessageType.Text && messageBytes == result.Count)
                {
                    suppressEchoForMessage = IsClientCommand(buffer.AsSpan(0, result.Count));
                    commandPrefixLength = Math.Min(result.Count, commandPrefix.Length);
                    Buffer.BlockCopy(buffer, 0, commandPrefix, 0, commandPrefixLength);
                }

                await log.WriteAsync(new
                {
                    type = "webSocketReceive",
                    timestampUtc = DateTimeOffset.UtcNow,
                    correlationId,
                    path = context.Request.Path.Value,
                    messageIndex,
                    messageType = result.MessageType.ToString(),
                    result.EndOfMessage,
                    messageBytesReceived = messageBytes,
                    messageCapturedBytes,
                    body = BodyCapture.Create(buffer.AsSpan(0, keep).ToArray(), result.Count,
                        forceBinary: result.MessageType == WebSocketMessageType.Binary)
                });

                if (result.MessageType == WebSocketMessageType.Text && result.EndOfMessage && !suppressEchoForMessage)
                {


                    await socket.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count),
                        result.MessageType, result.EndOfMessage, context.RequestAborted);
                    detailed.Write("webSocketFrame", new
                    {
                        id = trafficId, direction = "backendToClient", remoteAddress = context.Connection.RemoteIpAddress?.ToString(),
                        path = context.Request.Path.Value, messageType = result.MessageType.ToString(), result.EndOfMessage,
                        payload = DetailedTrafficLog.Payload(buffer.AsSpan(0, result.Count))
                    });
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    await socket.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count),
                        result.MessageType, result.EndOfMessage, context.RequestAborted);
                    detailed.Write("webSocketFrame", new
                    {
                        id = trafficId, direction = "backendToClient", remoteAddress = context.Connection.RemoteIpAddress?.ToString(),
                        path = context.Request.Path.Value, messageType = result.MessageType.ToString(), result.EndOfMessage,
                        payload = DetailedTrafficLog.Payload(buffer.AsSpan(0, result.Count))
                    });
                }

                if (result.EndOfMessage)
                {











                    if (result.MessageType == WebSocketMessageType.Text)
                        await TrySendCommandAckAsync(socket,
                            commandPrefix, commandPrefixLength,
                            context.RequestAborted, correlationId, trafficId);

                    messageIndex++;
                    messageBytes = 0;
                    messageCapturedBytes = 0;
                    suppressEchoForMessage = false;
                    commandPrefixLength = 0;
                }
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or IOException)
        {
            await log.WriteAsync(new
            {
                type = "webSocketError",
                timestampUtc = DateTimeOffset.UtcNow,
                correlationId,
                path = context.Request.Path.Value,
                error = exception.GetType().Name
            });
        }
    }

    private async Task<(int Status, string ContentType, string? Body, string Source)> ResolveResponseAsync(
        HttpContext context, BodyCapture body)
    {
        if (body.ReadError is not null)
            return (StatusCodes.Status400BadRequest, capture.ResponseContentType, null, "request-body-error");

        var overrideEntry = overrides.GetEntry(context.Request.Path.Value);
        if (overrideEntry is not null)
        {
            logger.LogInformation("Path override matched: {Path}", context.Request.Path.Value);
            return (overrideEntry.StatusCode, overrideEntry.ContentType ?? capture.ResponseContentType,
                overrideEntry.Body, "responses.json");
        }

        var dynamicBody = await routes.MatchAsync(context);
        if (dynamicBody is not null)
        {
            logger.LogInformation("Dynamic route matched: {Method} {Path}", context.Request.Method, context.Request.Path.Value);
            return (StatusCodes.Status200OK, capture.ResponseContentType, dynamicBody, "dynamic-route");
        }

        var contractEntry = contract.GetEntry(context.Request.Path.Value);
        if (contractEntry is not null)
            return (StatusCodes.Status200OK, capture.ResponseContentType, contractEntry.Body, "contract.json");

        return (capture.ResponseStatusCode, capture.ResponseContentType, null, "fallback");
    }

    private Dictionary<string, string[]> RedactedHeaders(HttpContext context) =>
        context.Request.Headers.ToDictionary(
            pair => pair.Key,
            pair => capture.RedactSensitiveHeaders && CaptureOptions.IsSensitiveHeader(pair.Key)
                ? new[] { "[REDACTED]" }
                : pair.Value.Select(value => value ?? "").ToArray(),
            StringComparer.OrdinalIgnoreCase);







    private static bool IsClientCommand(ReadOnlySpan<byte> chunk)
    {
        ReadOnlySpan<byte> prefix = "{\"command\":\""u8;
        if (chunk.Length < prefix.Length)
            return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (chunk[i] != prefix[i])
                return false;
        }
        return true;
    }











    private async Task TrySendCommandAckAsync(WebSocket socket, byte[] firstChunk, int firstChunkLength,
        CancellationToken cancellationToken, string correlationId, string trafficId)
    {
        string? command = ExtractCommandName(firstChunk.AsSpan(0, firstChunkLength));
        string? ack = command switch
        {
            "session_assign" => "{\"dataFormatType\":\"CMD_RESPONSE\",\"command\":\"session_assign\"}",
            "session_refresh" => "{\"dataFormatType\":\"CMD_RESPONSE\",\"command\":\"session_refresh\"}",



            "retransmission" => "{\"dataFormatType\":\"CMD_RESPONSE\",\"command\":\"retransmission\"}",
            _ => null,
        };
        if (ack is null)
            return;

        var bytes = System.Text.Encoding.UTF8.GetBytes(ack);
        try
        {
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
                endOfMessage: true, cancellationToken);
            detailed.Write("webSocketFrame", new
            {
                id = trafficId, direction = "backendToClient", messageType = "Text", endOfMessage = true,
                payload = DetailedTrafficLog.Payload(bytes)
            });
            await log.WriteAsync(new
            {
                type = "webSocketSend",
                timestampUtc = DateTimeOffset.UtcNow,
                correlationId,
                path = (string?)null,
                messageType = "Text",
                endOfMessage = true,
                body = BodyCapture.Create(bytes, bytes.Length)
            });
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or IOException)
        {
            await log.WriteAsync(new
            {
                type = "webSocketError",
                timestampUtc = DateTimeOffset.UtcNow,
                correlationId,
                path = (string?)null,
                error = exception.GetType().Name
            });
        }
    }

    private static string? ExtractCommandName(ReadOnlySpan<byte> frame)
    {


        ReadOnlySpan<byte> key = "\"command\""u8;
        for (var i = 0; i + key.Length + 3 <= frame.Length; i++)
        {
            var match = true;
            for (var k = 0; k < key.Length; k++)
            {
                if (frame[i + k] != key[k]) { match = false; break; }
            }
            if (!match)
                continue;
            var j = i + key.Length;
            while (j < frame.Length && (frame[j] == (byte)' ' || frame[j] == (byte)'\t')) j++;
            if (j >= frame.Length || frame[j] != (byte)':') continue;
            j++;
            while (j < frame.Length && (frame[j] == (byte)' ' || frame[j] == (byte)'\t')) j++;
            if (j >= frame.Length || frame[j] != (byte)'"') continue;
            j++;
            var start = j;
            while (j < frame.Length && frame[j] != (byte)'"') j++;
            if (j >= frame.Length) return null;
            return System.Text.Encoding.UTF8.GetString(frame.Slice(start, j - start));
        }
        return null;
    }
}
