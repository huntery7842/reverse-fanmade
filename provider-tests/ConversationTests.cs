using ReVerse.Capture.Signaling;

namespace Provider.Tests;

internal static class ConversationTests
{
    private static readonly byte[] Readiness = Convert.FromHexString(
        "0006726F6F6D2D310100030005616C6963650001020003626F6200020200056361726F6C000302");

    internal static void Startup()
    {
        using var room = new Room();
        var conversation = room.Conversations["alice"];
        Assert.Throws<WireFormatException>(() => room.Register("alice"), "registration before startup accepted");
        Assert.Throws<WireFormatException>(() => conversation.Control(new WireSettings([new(0, 1), new(1, 1), new(2, 1)])), "settings before probe");
        Assert.Throws<WireFormatException>(() => conversation.Control(new WireControl(0x14, [1])), "request20 before reply19");
        Assert.Throws<WireFormatException>(() => conversation.Control(new WireControl(0x12, [1, 1])), "request18 before settings");
        Assert.That(conversation.Control(new WireControl(3, [])) is [WireControl { Type: 4, Values.Count: 0 }], "probe response");
        foreach (var entries in new WireSetting[][] { [], [new(0, 1), new(1, 1)], [new(0, 1), new(0, 1), new(2, 1)], [new(0, 1), new(1, 1), new(3, 1)] })
            Assert.Throws<WireFormatException>(() => conversation.Control(new WireSettings(entries)), "invalid setting keys");
        var response = conversation.Control(new WireSettings([new(2, 99), new(0, 42), new(1, 7)]));
        Assert.That(response is [WireSettings], "settings response type");
        Assert.That(((WireSettings)response[0]).Entries.SequenceEqual(new WireSetting[] { new(0, 5000), new(1, 3), new(2, 25) }), "settings reply defaults");
        Assert.Throws<WireFormatException>(() => conversation.Control(new WireControl(0x12, [1, 2])), "request18 invalid values");
        AssertControl(conversation.Control(new WireControl(0x12, [1, 1])), 0x13, 1);
        Assert.Throws<WireFormatException>(() => room.Register("alice"), "registration before reply21 accepted");
        Assert.Throws<WireFormatException>(() => conversation.Control(new WireControl(0x14, [2])), "request20 invalid values");
        response = conversation.Control(new WireControl(0x14, [1]));
        Assert.That(response.Count == 2 && response[1] is WireControl { Type: 5, Values.Count: 0 }, "missing gate5");
        AssertControl([response[0]], 0x15, 1);
        Assert.Throws<WireFormatException>(() => conversation.Control(new WireControl(0x7f, [])), "unsupported control accepted");
        room.Empty();

        var observing = Room.NewOptions();
        observing.ExperimentalReplies = false;
        observing.Reply19 = observing.Reply21 = null;
        using var observed = new Room(observing);
        var client = observed.Conversations["alice"];
        client.Control(new WireControl(3, []));
        client.Control(new WireSettings([new(0, 1), new(1, 1), new(2, 1)]));
        Assert.That(client.Control(new WireControl(0x12, [1, 1])).Count == 0
            && client.Control(new WireControl(0x12, [1, 1])).Count == 0, "observing mode fabricated reply19");
        Assert.That(observed.Stages.Count(s => s.StartsWith("blocked_unknown", StringComparison.Ordinal)) == 1, "unknown stage repeated");
        Assert.Throws<WireFormatException>(() => observed.Register("alice"), "observing mode admitted application");
        observed.Empty();
    }

    private static void AssertControl(IReadOnlyList<WireContent> contents, byte type, ulong value) =>
        Assert.That(contents.Count == 1 && contents[0] is WireControl control && control.Type == type
            && control.Values.SequenceEqual(new[] { value }), "startup response value/type");

    internal static void NegativeControls()
    {
        foreach (var reply in new[] { 19, 21 })
        {
            var options = Room.NewOptions();
            options.NegativeControlReply = reply;
            using var room = new Room(options);
            var client = room.Conversations["alice"];
            client.Control(new WireControl(3, []));
            client.Control(new WireSettings([new(0, 5000), new(1, 3), new(2, 25)]));
            var response = client.Control(new WireControl(18, [1, 1]));
            if (reply == 21)
            {
                AssertControl(response, 19, 1);
                response = client.Control(new WireControl(20, [1]));
            }
            AssertControl(response, (byte)reply, 0);
            Assert.That(room.Stages.Contains($"negative_control_reply{reply}_zero"), "negative-control stage missing");
            Assert.Throws<WireFormatException>(() => room.Register("alice"), "negative control admitted registration");
            Assert.Throws<WireFormatException>(() => client.Control(new WireControl(3, [])), "negative control reset into working startup");
            Assert.Throws<WireFormatException>(() => client.Control(new WireControl(20, [1])), "negative control continued startup");
            Assert.That(!room.Directory.IsActive(Room.Session), "negative control activated session");
            room.Empty();
        }
    }

    internal static void PayloadDiagnostics()
    {
        using var room = new Room();
        room.Ready();
        var client = room.Conversations["alice"];
        var block = Bytes.Block(0x01020304, 0xaabbccdd);
        void Reject(byte[] body)
        {
            var count = room.PayloadAudits.Count;
            Assert.Throws<WireFormatException>(() => client.Application(4, 1, 0x10, body), "diagnosed invalid payload accepted");
            Assert.That(room.PayloadAudits.Count == count + 1 && room.PayloadAudits[^1].Outcome == "rejected", "rejection audit missing or repeated");
            room.Empty();
        }
        foreach (var secondary in new[] { false, true })
        {
            var body = Bytes.Route([2], Bytes.Block(0x01020304, 0xaabbccdd, secondary));
            client.Application(4, 1, 0x10, body);
            var audit = room.PayloadAudits[^1];
            Assert.That(audit is { Tag: 2, Version: 1, DestinationCount: 1, PrimaryLength: 4, EnvelopeValid: true, Outcome: "queued" }
                && audit.BodyBytes == body.Length && audit.SecondaryLength == (secondary ? 3 : 0)
                && audit.SecondaryMetadata == (secondary ? (ushort?)0xbeef : null), "successful metadata fields");
            Assert.That(room.Take("bob") is not null, "diagnostics prevented forwarding");
            room.Empty();
        }

        foreach (var tag in new byte[] { 1, 3, 255 })
        {
            Reject(Bytes.Route([2], [tag, .. room.Credentials["alice"].SecretBytes]));
            Assert.That(room.PayloadAudits[^1] is { Version: null, PrimaryLength: null, SecondaryLength: null,
                SecondaryMetadata: null, EnvelopeValid: false } && room.PayloadAudits[^1].Tag == tag, "opaque unknown-tag bytes were interpreted");
        }
        Reject(Bytes.Route([2], [2, 9]));
        Assert.That(room.PayloadAudits[^1] is { Tag: 2, Version: 9, PrimaryLength: null }, "unknown version missing");
        Reject(Bytes.Route([2], block[..^1]));
        Assert.That(room.PayloadAudits[^1] is { PrimaryLength: 4, SecondaryLength: 3, SecondaryMetadata: 0xbeef, EnvelopeValid: false }, "truncated secondary diagnostics");
        Reject(Bytes.Route([2], Bytes.Block(0x01020305, 0xaabbccdd)));
        Assert.That(room.PayloadAudits[^1].EnvelopeValid, "nonce rejection confused with invalid envelope length");
        foreach (var length in Enumerable.Range(0, block.Length)) Reject(Bytes.Route([2], block[..length]));
        Reject([]);
        Assert.That(room.PayloadAudits[^1] is { Tag: null, Version: null, SecondaryMetadata: null }, "stale metadata leaked across records");
        var fields = typeof(SignalingPayloadAudit).GetProperties().Select(p => p.Name).Order().ToArray();
        Assert.That(fields.SequenceEqual(new[] { "BodyBytes", "DestinationCount", "EnvelopeValid", "Outcome", "PeerPrimary", "PrimaryLength", "SecondaryLength", "SecondaryMetadata", "Tag", "Version" }.Order()),
            "diagnostic schema contains an unreviewed field");
        var serialized = System.Text.Json.JsonSerializer.Serialize(room.PayloadAudits);
        foreach (var key in room.Credentials.Values)
            Assert.That(!serialized.Contains(key.Secret, StringComparison.Ordinal) && !serialized.Contains(key.Psk, StringComparison.Ordinal), "diagnostics leaked credentials");
    }

    internal static void RegistrationOrders()
    {
        string[][] orders =
        [
            ["alice", "bob", "carol"], ["alice", "carol", "bob"], ["bob", "alice", "carol"],
            ["bob", "carol", "alice"], ["carol", "alice", "bob"], ["carol", "bob", "alice"]
        ];
        foreach (var order in orders)
        {
            using var room = new Room();
            foreach (var account in order) room.Startup(account);
            for (var i = 0; i < order.Length; i++)
            {
                var account = order[i];
                room.Register(account);
                var reply = Assert.NotNull(room.Take(account), "missing registration reply");
                Assert.That(reply is { Family: 1, Operation: 1, Qualifier: 2, Descriptor: 0x1c }, "registration envelope");
                Assert.Bytes([0, 0, .. Bytes.Text(Room.Session), .. Bytes.U16(room.Peers[account].Number), 0, 3], reply.Body, "registration fixed layout");
                if (i < order.Length - 1)
                {
                    Assert.That(!room.Directory.AllRegistered(Room.Session) && !room.Directory.IsActive(Room.Session)
                        && !room.Directory.Activate(Room.Session), "activated before every actual member registered");
                    room.Empty();
                }
            }
            Assert.That(room.Directory.IsActive(Room.Session) && room.Directory.AllRegistered(Room.Session), "all actual members did not activate");
            foreach (var account in order)
            {
                var ready = Assert.NotNull(room.Take(account), "readiness missing for actual member");
                Assert.That(ready is { Family: 3, Operation: 7, Qualifier: 0x10, Descriptor: 0x1c }, "readiness envelope");
                Assert.Bytes(Readiness, ready.Body, "readiness fixed bytes/count/order/state");
            }
            Assert.That(!room.Directory.Activate(Room.Session), "activation repeated");
            room.Register(order[0]);
            Assert.That(room.Take(order[0]) is { Family: 1, Operation: 1, Qualifier: 2 }, "registration retry reply");
            room.Empty();
            Assert.That(room.Stages.Count(s => s.StartsWith("all_members_registered", StringComparison.Ordinal)) == 1, "duplicate readiness transition");
        }
        var fixedOptions = Room.NewOptions();
        fixedOptions.RegistrationFieldBIsPeerNumber = false;
        fixedOptions.RegistrationFieldB = 0x1234;
        using var fixedRoom = new Room(fixedOptions);
        fixedRoom.Startup("alice");
        fixedRoom.Register("alice");
        Assert.Bytes(Convert.FromHexString("00000006726F6F6D2D3112340003"), fixedRoom.Take("alice")!.Body, "explicit registration fieldB");
    }

    internal static void BadRegistration()
    {
        using var room = new Room();
        room.Startup("alice");
        var valid = Bytes.Sized(room.Credentials["alice"].SecretBytes);
        byte[][] malformed =
        [[], [0], [0, 0], Bytes.Sized(room.Credentials["bob"].SecretBytes),
            Bytes.Change(valid, 2, (byte)'!'), valid[..^1], [.. valid, 0], Bytes.Change(valid, 1, 65), Bytes.Change(valid, 1, 63)];
        foreach (var body in malformed)
        {
            Assert.Throws<WireFormatException>(() => room.Conversations["alice"].Application(1, 1, 1, body), "bad registration accepted");
            Assert.That(!room.Directory.IsActive(Room.Session) && !room.Directory.AllRegistered(Room.Session), "bad registration advanced readiness");
            room.Empty();
        }
        Assert.Throws<WireFormatException>(() => room.Conversations["alice"].Application(4, 1, 0x10, ReadOnlyMemory<byte>.Empty), "unregistered payload accepted");
        room.Register("alice");
        Assert.That(room.Take("alice") is { Family: 1, Qualifier: 2 }, "bad attempt prevented valid registration");
        Assert.Throws<WireFormatException>(() => room.Conversations["alice"].Application(4, 1, 0x10,
            Bytes.Route([2], Bytes.Block(0x01020304, 0xaabbccdd))), "forwarded before all registered");
        Assert.Throws<WireFormatException>(() => room.Conversations["alice"].Application(7, 7, 7, ReadOnlyMemory<byte>.Empty), "unsupported application accepted");
        room.Empty();
    }

    internal static void Forwarding()
    {
        using var room = new Room();
        using var foreign = new Room();
        room.Ready();
        foreign.Ready();
        foreach (var secondary in new[] { false, true })
        {
            var block = Bytes.Block(0x01020304, 0xaabbccdd, secondary);
            room.Conversations["alice"].Application(4, 1, 0x10, Bytes.Route([2], block));
            var delivery = Assert.NotNull(room.Take("bob"), "forward missing");
            Assert.That(delivery is { Family: 4, Operation: 1, Qualifier: 0x10, Descriptor: 0x42 }, "forward envelope");
            Assert.Bytes([.. Bytes.Text(Room.Session), 0, 1, .. Bytes.Sized(block)], delivery.Body, "authenticated session/sender rewrite or nested bytes changed");
            room.Empty();
            foreign.Empty();
        }

        using var collision = new Room(members: [new("alice", 1), new("bob", 2), new("carol", 2)]);
        collision.Ready();
        var multicast = Bytes.Block(1, 2);
        collision.Conversations["alice"].Application(4, 1, 0x10, Bytes.Route([3, 2], multicast));
        var bob = Assert.NotNull(collision.Take("bob"), "multicast bob");
        var carol = Assert.NotNull(collision.Take("carol"), "multicast carol");
        Assert.Bytes(bob.Body, carol.Body, "multicast bodies differ");
        collision.Empty();
    }

    internal static void BadForwarding()
    {
        using var room = new Room();
        room.Ready();
        var block = Bytes.Block(0x01020304, 0xaabbccdd);
        var route = Bytes.Route([2], block);
        byte[][] malformed =
        [
            [], [0], Bytes.Route([], block), Bytes.Route([2, 2], block), Bytes.Route([0], block),
            Bytes.Route([65535], block), Bytes.Route([1], Bytes.Block(0x01020304, 0x01020304)),
            Bytes.Route([2, 65535], block), Bytes.Route([65535, 2], block),
            Bytes.Route([2, 3], block), Bytes.Route([3, 2], block),
            Bytes.Route([1, 2, 3, 4], block), route[..^1], [.. route, 0], Bytes.Change(route, 5, 0xff),
            Bytes.Route([2], Bytes.Change(block, 0, 3)), Bytes.Route([2], Bytes.Change(block, 1, 2)),
            Bytes.Route([2], Bytes.Block(0xaabbccdd, 0x01020304)),
            Bytes.Route([2], Bytes.Block(0x01020305, 0xaabbccdd)),
            Bytes.Route([2], Bytes.Block(0x01020304, 0xaabbccde)),
            Bytes.Route([2], Bytes.Change(block, 10, 0xff)),
            Bytes.Route([2], Bytes.Change(block, 12, 0xff)),
            Bytes.Route([2], Bytes.Change(block, 13, 0)),
            Bytes.Route([2], [.. block, 0])
        ];

        malformed = [.. malformed, .. Enumerable.Range(0, block.Length).Select(n => Bytes.Route([2], block[..n]))];
        for (var i = 0; i < malformed.Length; i++)
        {
            var body = malformed[i];
            Assert.Throws<WireFormatException>(() => room.Conversations["alice"].Application(4, 1, 0x10, body), $"malformed route {i} accepted");
            room.Empty();
            Assert.That(room.Directory.IsActive(Room.Session), $"validation failure {i} changed directory state");
        }
        room.Conversations["alice"].Application(4, 1, 0x10, route);
        Assert.That(room.Take("bob") is not null, "rejections poisoned subsequent valid route");
        room.Empty();
    }
}
