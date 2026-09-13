using System.Text.Json.Nodes;
using ReVerse.Capture.Signaling;

namespace ReVerse.Capture.Matchmaking;


public sealed class MatchmakingCoordinator(
    MatchmakingOptions options, NotificationHub hub, GameSessionRegistry registry,
    SignalingOptions signalingOptions, SignalingDirectory signaling) : BackgroundService
{
    private readonly object gate = new();
    private readonly Dictionary<string, Ticket> tickets = new(StringComparer.Ordinal);
    private long order;

    private sealed class Ticket(string owner, JsonObject request, long order, DateTimeOffset expires)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Owner { get; } = owner;
        public JsonObject Request { get; } = (JsonObject)request.DeepClone();
        public string Ruleset { get; } = RequiredString(request, "rulesetName");
        public long Order { get; } = order;
        public DateTimeOffset Expires { get; } = expires;
        public long Sequence { get; set; } = 1;
        public string State { get; set; } = "queued";
        public string? SessionId { get; set; }
        public string? OfferId { get; set; }
        public JsonObject Wire() => new()
        {
            ["matchmakingTicketSequenceNo"] = Sequence, ["ticketId"] = Id,
            ["rulesetName"] = Ruleset, ["submitRetryWaitSeconds"] = 1
        };
    }

    public JsonObject Submit(string account, JsonObject request)
    {
        lock (gate)
        {
            Sweep();
            ValidateTicket(account, request);
            if (!hub.IsOnline(account)) throw Error(409, "Assign a notification socket before searching.");
            var existing = tickets.Values.FirstOrDefault(t => t.Owner == account && t.State is "queued" or "offered");



            if (existing?.SessionId is { } priorId && registry.Sessions.TryGetValue(priorId, out var prior)
                && prior.Players.ContainsKey(account))
            {
                EndSession(prior);
                existing = null;
            }
            if (existing is not null)
            {
                if (!JsonNode.DeepEquals(existing.Request, request)) throw Error(409, "Cancel the active ticket before changing search parameters.");
                return existing.Wire();
            }
            if (registry.Sessions.Values.Any(s => s.Reserved.Contains(account)))
                throw Error(409, "Leave the current offered/joined session before searching again.");
            var ticket = new Ticket(account, request, ++order, DateTimeOffset.UtcNow.AddSeconds(options.TicketLifetimeSeconds));
            tickets.Add(ticket.Id, ticket);
            TicketEvent(ticket, "submitted");

            return ticket.Wire();
        }
    }

    public JsonObject Cancel(string account, string id)
    {
        lock (gate)
        {
            if (!tickets.TryGetValue(id, out var ticket) || ticket.Owner != account) throw Error(404, "Ticket not found.");
            if (ticket.State is "canceled" or "expired" or "failed") return new();
            if (ticket.SessionId is { } sessionId && registry.Sessions.TryGetValue(sessionId, out var session))
                EndSession(session);
            ticket.State = "canceled";
            ticket.Sequence++;
            TicketEvent(ticket, "canceled");
            return new();
        }
    }

    public void Disconnected(string account)
    {
        lock (gate)
        {
            if (hub.IsOnline(account)) return;
            foreach (var ticket in tickets.Values.Where(t => t.Owner == account && t.State == "queued").ToArray())
            {
                ticket.State = "canceled";
                ticket.Sequence++;
            }
            foreach (var session in registry.Sessions.Values.Where(s => s.Reserved.Contains(account)).ToArray()) EndSession(session);
        }
    }

    public JsonObject SessionRequest(string account, string nickname, string path, string method, string[] ids, string? member, JsonObject body, GameSessionRead? read = null)
    {
        lock (gate)
        {
            Sweep();
            if (!options.ExperimentalSessionProtocol) throw Error(501, "Session protocol needs stock-client validation; experimental mode is disabled.");
            if (path == "/v1/gameSession" && method == "GET")
            {
                if (ids.Length == 0) throw Error(400, "X-Be-Session-Ids is required.");
                var sessions = ids.Select(id => Authorized(account, id)).ToArray();
                read ??= GameSessionRead.Parse(QueryCollection.Empty);


                var matching = sessions.Distinct().Where(s => read.UsePlayerSession is null || s.UsePlayerSession == read.UsePlayerSession).ToArray();
                if (matching.Length == 0) throw Error(404, "No sessions match the requested filter.");
                return new JsonObject { ["gameSessions"] = new JsonArray(matching.Select(s => (JsonNode)read.Serialize(s)).ToArray()) };
            }

            if (path == "/v1/gameSession" && method == "POST") throw Error(501, "Client-created session response schema is not yet recovered.");
            if (path.EndsWith("/invitation", StringComparison.Ordinal) || path.EndsWith("/spectators", StringComparison.Ordinal))
                throw Error(501, "Invitations and spectators are not implemented.");
            if (ids.Length != 1) throw Error(400, "One X-Be-Session-Id is required.");
            var session = Authorized(account, ids[0]);
            var representative = session.Representative == account;
            if (path == "/v1/gameSession")
            {
                if (!representative) throw Error(403, "Representative permission required.");
                if (method == "DELETE") { EndSession(session); return new(); }
                if (method != "PATCH") throw Error(405, "Use GET, POST, PATCH or DELETE.");
                var patch = body["gameSession"] as JsonObject ?? body;
                if (!ReferenceEquals(patch, body) && body.Any(p => p.Key != "gameSession"))
                    throw Error(400, "Unsupported session update field.");
                if (patch.Any(p => p.Key is not ("joinDisabled" or "customData1" or "customData2")))
                    throw Error(400, "Unsupported session update field.");

                var disabled = patch["joinDisabled"]?.GetValue<bool>() ?? session.JoinDisabled;
                var custom1 = patch["customData1"]?.GetValue<string>() ?? session.CustomData1;
                var custom2 = patch["customData2"]?.GetValue<string>() ?? session.CustomData2;
                var disabledChanged = disabled != session.JoinDisabled;
                if (!disabledChanged && custom1 == session.CustomData1 && custom2 == session.CustomData2)
                    return new();
                session.JoinDisabled = disabled;
                session.CustomData1 = custom1;
                session.CustomData2 = custom2;
                session.Sequence++;
                if (disabledChanged) SessionEvent(session, "joinDisabled:updated", new() { ["joinDisabled"] = disabled });
                else SessionSequenceEvent(session);
                return new();
            }
            if (path.EndsWith("/member/players", StringComparison.Ordinal))
            {
                if (method != "POST") throw Error(405, "Use POST to join.");
                var player = GameJoinProtocol.ParsePlayer(account, nickname, body);
                if (body["useCrossPlay"] is { } crossPlay && crossPlay.GetValue<bool>() != session.UseCrossPlay)
                    throw Error(409, "Join cross-play setting differs from the matched tickets.");
                if (session.Players.TryGetValue(account, out var joined))
                {
                    if (joined["customData1"]!.GetValue<string>() != player["customData1"]!.GetValue<string>())
                        throw Error(409, "A different join attempt already owns this reservation.");
                    return GameJoinProtocol.Reply(session.Id, joined);
                }
                if (session.JoinDisabled || session.Players.Count >= session.Capacity) throw Error(409, "Session is closed or full.");
                session.Players.Add(account, player);
                session.Sequence++;
                SessionEvent(session, "member:players:created", GameJoinProtocol.CreatedEvent(player));
                TryAllocateSignaling(session);
                return GameJoinProtocol.Reply(session.Id, player);
            }
            if (path.EndsWith("/members", StringComparison.Ordinal))
            {
                if (method is not ("PATCH" or "DELETE")) throw Error(405, "Use PATCH or DELETE.");
                var target = string.IsNullOrEmpty(member) ? account : member;
                if (target != account && !representative) throw Error(403, "Cannot change another member.");
                if (!session.Reserved.Contains(target)) throw Error(404, "Member not found.");
                if (method == "DELETE")
                {

                    EndSession(session);
                    return new();
                }
                if (!session.Players.TryGetValue(target, out var player)) throw Error(409, "Member has not joined.");
                if (body.Any(p => p.Key is not ("customData1" or "customData2"))) throw Error(400, "Unsupported member update field.");
                var values = body.ToDictionary(p => p.Key, p => p.Value?.GetValue<string>() ?? "");
                if (values.Values.Any(value => value.Length > 16384)) throw Error(400, "Member metadata is too large.");
                if (values.TryGetValue("customData1", out var metadata))
                {
                    _ = GameJoinProtocol.ReadNonce(metadata);
                    if (metadata != player["customData1"]!.GetValue<string>())
                        throw Error(409, "Peer metadata is bound to this join. Leave and rejoin to change it.");
                }
                if (values.All(value => value.Value == (player[value.Key]?.GetValue<string>() ?? "")))
                    return new();
                foreach (var value in values) player[value.Key] = value.Value;
                session.Sequence++;
                SessionSequenceEvent(session);
                return new();
            }
            if (path.EndsWith("/signaling", StringComparison.Ordinal))
            {
                if (method == "GET")
                {
                    if (!signalingOptions.Enabled) throw Error(503, "No signaling provider has supplied endpoints.");
                    if (!session.Players.ContainsKey(account)) throw Error(403, "Join before obtaining signaling credentials.");
                    TryAllocateSignaling(session);
                    return signaling.Descriptor(session.Id, account) ?? throw Error(503, "No signaling provider has supplied endpoints.");
                }
                if (method != "PATCH") throw Error(405, "Use GET or PATCH.");
                if (!representative || !session.Players.ContainsKey(account)) throw Error(403, "Joined representative permission required.");



                if (body.Count != 1 || body["signalingTimeoutSeconds"] is not JsonValue value
                    || !value.TryGetValue<int>(out var timeout) || timeout is < 1 or > 600)
                    throw Error(400, "signalingTimeoutSeconds must be an integer from 1 to 600.");
                session.RequestSignaling(timeout, signalingOptions.Enabled);
                TryAllocateSignaling(session);
                return new();
            }
            if (path.EndsWith("/sessionMessage", StringComparison.Ordinal))
            {
                if (method != "POST") throw Error(405, "Use POST.");
                if (!session.Players.ContainsKey(account)) throw Error(403, "Join before sending messages.");
                var content = RequiredString(body, "content");
                session.Sequence++;
                SessionEvent(session, "sessionMessage:created", new() { ["from"] = account, ["content"] = content });
                return new();
            }
            throw Error(404, "Unknown session route.");
        }
    }

    private GameSessionRegistry.Session Authorized(string account, string id)
    {
        if (!registry.Sessions.TryGetValue(id, out var session) || !session.Reserved.Contains(account)) throw Error(404, "Session not found.");
        return session;
    }

    private void EndSession(GameSessionRegistry.Session session)
    {
        registry.Sessions.Remove(session.Id);
        signaling.Remove(session.Id);
        foreach (var ticket in tickets.Values.Where(t => t.SessionId == session.Id && t.State == "offered"))
        {
            ticket.State = "failed";
            ticket.Sequence++;
            hub.Publish([ticket.Owner], "matchmaking:offers:failed", new()
            {
                ["offers"] = new JsonArray(new JsonObject { ["matchmakingOfferSequenceNo"] = 2, ["offerId"] = ticket.OfferId })
            });
            TicketEvent(ticket, "failed");
        }
    }

    private void TryAllocateSignaling(GameSessionRegistry.Session session)
    {
        if (session.ProviderAllocated || session.SignalingDeadline is not { } deadline
            || session.Players.Count != session.Reserved.Count || !signaling.IsListening) return;
        var members = session.Players.Select(p => new SignalingMember(p.Key,
            GameJoinProtocol.ReadNonce(p.Value["customData1"]!.GetValue<string>()))).ToArray();
        if (!signaling.Allocate(session.Id, session.Representative, members, deadline)) return;
        session.ProviderAllocated = true;
        session.Sequence++;

        foreach (var account in session.Reserved)
        {
            var descriptor = signaling.Descriptor(session.Id, account);
            if (descriptor is not null) hub.Publish([account], "gameSession:signaling:created", new()
            {
                ["sessionId"] = session.Id, ["gameSessionSequenceNo"] = session.Sequence, ["signaling"] = descriptor
            });
        }
    }




    private void SessionSequenceEvent(GameSessionRegistry.Session session) =>
        SessionEvent(session, "operateSequenceNo", new() { ["skip"] = 0 });

    private void SessionEvent(GameSessionRegistry.Session session, string suffix, JsonObject body)
    {
        body["sessionId"] = session.Id;
        body["gameSessionSequenceNo"] = session.Sequence;
        hub.Publish(session.Reserved, "gameSession:" + suffix, body);
    }

    private void TicketEvent(Ticket ticket, string suffix) => hub.Publish([ticket.Owner], "matchmaking:tickets:" + suffix,
        new() { ["tickets"] = new JsonArray(ticket.Wire()) });

    private static void ValidateTicket(string account, JsonObject request)
    {
        _ = RequiredString(request, "rulesetName");
        _ = RequiredString(request, "groupName");
        if (request["useCrossPlay"] is not JsonValue flag || !flag.TryGetValue<bool>(out _)) throw Error(400, "useCrossPlay must be boolean.");
        if (request["players"] is not JsonArray { Count: 1 } players || players[0] is not JsonObject player)
            throw Error(400, "Only authenticated single-player tickets are supported.");
        if (RequiredString(player, "accountId") != account) throw Error(403, "Ticket player must match the relay account.");
        var area = request["playarea"] as JsonObject ?? throw Error(400, "playarea is required.");
        _ = RequiredString(area, "name");
        if (area["regions"] is not JsonArray { Count: > 0 and <= 32 } regions) throw Error(400, "At least one region is required.");
        foreach (var region in regions)
        {
            if (region is not JsonObject obj) throw Error(400, "Invalid region.");
            _ = RequiredString(obj, "name");
        }
        ValidateAttributes(request["ticketAttributes"]);
        ValidateAttributes(player["playerAttributes"]);
    }

    private static void ValidateAttributes(JsonNode? node)
    {
        if (node is not JsonArray { Count: <= 64 } attrs) throw Error(400, "Typed attributes array is required.");
        foreach (var item in attrs)
        {
            if (item is not JsonObject attr) throw Error(400, "Invalid typed attribute.");
            _ = RequiredString(attr, "name");
            if (!attr.ContainsKey("type") || !attr.ContainsKey("value")) throw Error(400, "Attribute type/value are required.");
        }
    }



    private JsonObject Compatibility(Ticket ticket)
    {
        var copy = (JsonObject)ticket.Request.DeepClone();
        copy["players"]![0]!.AsObject().Remove("accountId");
        copy["playarea"]!.AsObject().Remove("regions");
        if (options.IgnorePlayerAttributes) copy["players"]![0]!.AsObject().Remove("playerAttributes");
        return copy;
    }

    private static HashSet<string> Regions(Ticket ticket) => new(
        ticket.Request["playarea"]!["regions"]!.AsArray().Select(n => n!["name"]!.GetValue<string>()), StringComparer.Ordinal);

    private void Match()
    {
        if (!options.ExperimentalSessionProtocol) return;
        var queue = tickets.Values.Where(t => t.State == "queued" && hub.IsOnline(t.Owner)).OrderBy(t => t.Order).ToArray();
        foreach (var first in queue)
        {
            if (first.State != "queued" || !options.Rulesets.TryGetValue(first.Ruleset, out var count)) continue;
            var selected = new List<Ticket> { first };
            var regions = Regions(first);
            var fingerprint = Compatibility(first);
            foreach (var next in queue.Where(t => t.Order > first.Order && t.State == "queued"))
            {
                if (!JsonNode.DeepEquals(fingerprint, Compatibility(next))) continue;
                var common = Regions(next);
                common.IntersectWith(regions);
                if (common.Count == 0) continue;
                selected.Add(next);
                regions = common;
                if (selected.Count == count) break;
            }
            if (selected.Count != count) continue;
            var session = registry.Create(selected.Select(t => t.Owner).ToArray(), count, DateTimeOffset.UtcNow.AddSeconds(options.JoinLifetimeSeconds));
            session.UseCrossPlay = first.Request["useCrossPlay"]!.GetValue<bool>();
            var offerId = Guid.NewGuid().ToString("N");
            session.OfferId = offerId;
            foreach (var ticket in selected)
            {
                ticket.State = "offered";
                ticket.SessionId = session.Id;
                ticket.OfferId = offerId;
                hub.Publish([ticket.Owner], "matchmaking:offers:created", new()
                {
                    ["offers"] = new JsonArray(new JsonObject
                    {
                        ["matchmakingOfferSequenceNo"] = 1, ["offerId"] = offerId, ["rulesetName"] = ticket.Ruleset,
                        ["location"] = new JsonObject { ["gameSessionId"] = session.Id }
                    })
                });
            }
        }
    }

    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var ticket in tickets.Values.Where(t => t.State == "queued" && t.Expires <= now))
        {
            ticket.State = "expired";
            ticket.Sequence++;
            TicketEvent(ticket, "timedOut");
        }
        foreach (var session in registry.Sessions.Values.ToArray())
        {
            if (session.ProviderAllocated)
            {

                if (!signaling.IsAlive(session.Id)) EndSession(session);
            }
            else if (signalingOptions.Enabled && session.SignalingDeadline is { } deadline
                && session.Players.Count == session.Reserved.Count)
            {
                if (deadline <= now) EndSession(session);
                else TryAllocateSignaling(session);
            }
            else if (session.Deadline <= now || session.SignalingDeadline is { } pending && pending <= now)
                EndSession(session);
        }
        foreach (var ticket in tickets.Values.Where(t => t.State is not ("queued" or "offered") && t.Expires.AddMinutes(5) < now).ToArray()) tickets.Remove(ticket.Id);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                lock (gate) { Sweep(); Match(); }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal static BadHttpRequestException Error(int status, string message) => new(message, status);
    internal static string RequiredString(JsonObject body, string name) =>
        body[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) && text.Length <= 16384
            ? text : throw Error(400, $"{name} must be a nonempty string.");
}
