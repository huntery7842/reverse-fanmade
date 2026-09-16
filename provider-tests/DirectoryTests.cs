using System.Text;
using System.Text.Json.Nodes;
using ReVerse.Capture.Matchmaking;
using ReVerse.Capture.Signaling;

namespace Provider.Tests;

internal static class DirectoryTests
{
    internal static void Options()
    {
        new SignalingOptions().Validate();
        Room.NewOptions().Validate();
        Action<SignalingOptions>[] invalid =
        [
            o => o.BindAddress = "localhost", o => o.Port = 0, o => o.Port = 65536,
            o => o.PublicPort = 0, o => o.PublicPort = 65536, o => o.PublicHost = "",
            o => o.PublicHost = "0.0.0.0", o => o.PublicHost = "::", o => o.PublicHost = "http://127.0.0.1/",
            o => o.PublicHost = new string('a', 254), o => o.MaxConnections = 1, o => o.MaxConnections = 129,
            o => o.HandshakeTimeoutSeconds = 0, o => o.HandshakeTimeoutSeconds = 31,
            o => o.IdleTimeoutSeconds = 4, o => o.IdleTimeoutSeconds = 601,
            o => o.MaxSessionSeconds = 59, o => o.MaxSessionSeconds = 86401,
            o => o.Reply19 = null, o => o.Reply19 = 0, o => o.Reply21 = null, o => o.Reply21 = 0,
            o => o.RegistrationFieldBIsPeerNumber = false, o => o.RegistrationFieldB = 7,
            o => o.NegativeControlReply = 0, o => o.NegativeControlReply = 20,
            o => { o.NegativeControlReply = 19; o.Enabled = false; },
            o => { o.NegativeControlReply = 21; o.ExperimentalReplies = false; }
        ];
        for (var i = 0; i < invalid.Length; i++)
        {
            var options = Room.NewOptions();
            invalid[i](options);
            Assert.Throws<ArgumentException>(options.Validate, $"invalid option case {i} accepted");
        }
        foreach (var reply in new[] { 19, 21 })
        {
            var options = Room.NewOptions();
            options.NegativeControlReply = reply;
            options.Validate();
            options.Reply19 = 0;
            Assert.Throws<ArgumentException>(options.Validate, "negative mode must not weaken normal reply-setting validation");
        }
        foreach (var upper in new[] { false, true })
        {
            var options = Room.NewOptions();
            options.Port = options.PublicPort = upper ? 65535 : 1;
            options.MaxConnections = upper ? 128 : 2;
            options.HandshakeTimeoutSeconds = upper ? 30 : 1;
            options.IdleTimeoutSeconds = upper ? 600 : 5;
            options.MaxSessionSeconds = upper ? 86400 : 60;
            options.RegistrationFieldBIsPeerNumber = false;
            options.RegistrationFieldB = 7;
            options.Validate();
        }
    }

    internal static void Credentials()
    {
        var options = Room.NewOptions();
        var directory = new SignalingDirectory(options);
        var deadline = DateTimeOffset.UtcNow.AddMinutes(1);
        Assert.That(!directory.Allocate(Room.Session, "carol", Room.Members, deadline), "allocation before listening");
        Assert.That(directory.Descriptor(Room.Session, "alice") is null, "descriptor before listening");
        directory.SetListening(true);
        Assert.That(directory.Allocate(Room.Session, "carol", Room.Members, deadline), "first allocation");
        var descriptors = Room.Members.Select(m => Assert.NotNull(directory.Descriptor(Room.Session, m.Account), "descriptor")).ToArray();
        var credentials = descriptors.Select(Credential.From).ToArray();
        Assert.That(credentials.Select(c => c.Identity).Distinct().Count() == 3, "shared identity");
        Assert.That(credentials.Select(c => c.Psk).Distinct().Count() == 3, "shared PSK");
        Assert.That(credentials.Select(c => c.Secret).Distinct().Count() == 3, "shared secret");
        foreach (var descriptor in descriptors)
        {
            Assert.That(descriptor.Select(p => p.Key).Order().SequenceEqual(new[] { "endpoints", "secret" }), "descriptor root shape");
            Assert.That(descriptor["endpoints"]!.AsArray().Count == 1, "endpoint count");
            var endpoint = descriptor["endpoints"]![0]!;
            Assert.That(endpoint["protocol"]!.GetValue<string>() == "DTLS", "endpoint protocol");
            Assert.That(endpoint["server"]!["host"]!.GetValue<string>() == "127.0.0.1"
                && endpoint["server"]!["port"]!.GetValue<int>() == options.PublicPort, "public endpoint");
            var credential = Credential.From(descriptor);
            Assert.That(credential.Identity.Length is > 0 and <= 39 && credential.Identity.All(c => c is >= '!' and <= '~'), "identity wire bound");
            Assert.That(Convert.FromHexString(credential.Psk).Length == 32 && credential.SecretBytes.Length == 64, "credential lengths");
            var copy = Assert.NotNull(directory.LookupPsk(credential.IdentityBytes), "PSK lookup");
            Assert.Bytes(Convert.FromHexString(credential.Psk), copy, "wrong PSK lookup");
            copy[0] ^= 0xff;
            Assert.Bytes(Convert.FromHexString(credential.Psk), Assert.NotNull(directory.LookupPsk(credential.IdentityBytes), "second lookup"), "lookup exposed mutable key");
        }
        Assert.That(directory.Allocate(Room.Session, "carol", Room.Members.Reverse().ToArray(), deadline.AddMinutes(1)), "allocation retry");
        for (var i = 0; i < Room.Members.Length; i++)
            Assert.That(JsonNode.DeepEquals(descriptors[i], directory.Descriptor(Room.Session, Room.Members[i].Account)), "retry rotated credentials");
        descriptors[0]["secret"] = "caller mutation";
        descriptors[0]["endpoints"]![0]!["psk"] = "caller mutation";
        Assert.That(Credential.From(directory.Descriptor(Room.Session, "carol")!) == credentials[0], "descriptor aliases internal state");
        Assert.That(directory.Descriptor(Room.Session, "outsider") is null && directory.Descriptor("other", "alice") is null, "foreign descriptor");
        foreach (var identity in new byte[][] { [], new byte[40], [0], [0x7f], [0x80], Encoding.ASCII.GetBytes("missing") })
            Assert.That(directory.LookupPsk(identity) is null && directory.Attach(identity, "foreign") is null, "malformed/unknown identity accepted");
        Assert.That(directory.Allocate("other", "carol", Room.Members, deadline), "second session allocation");
        var other = Credential.From(directory.Descriptor("other", "carol")!);
        Assert.That(other.Identity != credentials[0].Identity && other.Psk != credentials[0].Psk && other.Secret != credentials[0].Secret, "credentials reused between sessions");
        directory.Remove(Room.Session);
        foreach (var credential in credentials) Assert.That(directory.LookupPsk(credential.IdentityBytes) is null, "removed key still valid");
        Assert.That(directory.LookupPsk(other.IdentityBytes) is not null, "removing session revoked other session");
        directory.Remove(Room.Session);
        directory.Remove("other");
    }

    internal static void Bounds()
    {
        var options = Room.NewOptions();
        options.MaxConnections = 10;
        var directory = new SignalingDirectory(options);
        directory.SetListening(true);
        var deadline = DateTimeOffset.UtcNow.AddMinutes(1);
        IReadOnlyList<SignalingMember>[] invalid =
        [[], [new("carol", 1)], [new("carol", 1), new("carol", 2)], [new("alice", 1), new("bob", 2)],
            Enumerable.Range(0, 11).Select(i => new SignalingMember(i == 0 ? "carol" : $"p{i}", (uint)i)).ToArray()];
        foreach (var members in invalid)
            Assert.Throws<InvalidDataException>(() => directory.Allocate("bad", "carol", members, deadline), "invalid membership accepted");
        var ten = Enumerable.Range(0, 10).Select(i => new SignalingMember(i == 0 ? "carol" : $"p{i}", (uint)i)).ToArray();
        Assert.That(directory.Allocate("full", "carol", ten, deadline), "10 actual members rejected");
        Assert.That(!directory.Allocate("overflow", "carol", Room.Members, deadline), "capacity exceeded");
        Assert.That(directory.Descriptor("overflow", "alice") is null, "partial allocation leaked");
        Assert.That(directory.IsAlive("full"), "capacity rejection damaged existing allocation");
        directory.Remove("full");
        Assert.That(directory.Allocate("minimum", "carol", [new("carol", 0), new("alice", uint.MaxValue)], deadline), "minimum members or nonce bounds rejected");
        directory.Remove("minimum");
    }

    internal static void AttachAndRevoke()
    {
        using var room = new Room();
        var directory = room.Directory;
        var alice = room.Credentials["alice"];
        var peer = room.Peers["alice"];
        Assert.That(peer.Session == Room.Session && peer.Account == "alice" && peer.Number == 1
            && peer.Representative == 3 && peer.Nonce == 0x01020304, "authenticated snapshot context");
        Assert.That(peer.Peers.Select(p => p.Account).SequenceEqual(new[] { "alice", "bob", "carol" }), "member ordering");
        Assert.That(directory.Attach(alice.IdentityBytes, "duplicate") is null
            && directory.Attach(alice.IdentityBytes, Room.Connection("alice")) is null, "duplicate attachment accepted");
        var reply = new SignalingDelivery(1, 1, 2, []);
        Assert.That(!directory.Register("foreign", "alice", Room.Connection("alice"), alice.SecretBytes, reply), "cross-session register");
        Assert.That(!directory.Register(Room.Session, "bob", Room.Connection("alice"), alice.SecretBytes, reply), "cross-account register");
        Assert.That(!directory.Register(Room.Session, "alice", "duplicate", alice.SecretBytes, reply), "cross-connection register");
        Assert.That(!directory.Touch(Room.Session, "alice", "duplicate")
            && !directory.IsAttached(Room.Session, "alice", "duplicate")
            && directory.Dequeue(Room.Session, "alice", "duplicate") is null, "foreign connection access");
        Assert.That(!directory.Activate(Room.Session), "activation without registration");
        directory.Detach(Room.Session, "alice", "duplicate");
        Assert.That(directory.IsAlive(Room.Session), "foreign detach revoked session");
        directory.Detach(Room.Session, "alice", Room.Connection("alice"));
        AssertRevoked(room);
        directory.Detach(Room.Session, "alice", Room.Connection("alice"));
        directory.Remove(Room.Session);
        directory.Remove(Room.Session);
        Assert.That(directory.Allocate(Room.Session, "carol", Room.Members, DateTimeOffset.UtcNow.AddMinutes(1)), "reallocation after revocation");
        Assert.That(Credential.From(directory.Descriptor(Room.Session, "alice")!).Identity != alice.Identity, "reallocation reused revoked identity");
        Assert.That(directory.LookupPsk(alice.IdentityBytes) is null, "old identity accepted after reallocation");
        directory.SetListening(false);
        Assert.That(directory.Descriptor(Room.Session, "alice") is null && !directory.IsAlive(Room.Session), "unbound directory still ready");
    }

    internal static void AssertRevoked(Room room)
    {
        var directory = room.Directory;
        Assert.That(!directory.IsAlive(Room.Session) && !directory.IsActive(Room.Session) && !directory.AllRegistered(Room.Session), "revoked session remains alive");
        foreach (var (account, credential) in room.Credentials)
        {
            Assert.That(directory.Descriptor(Room.Session, account) is null && directory.LookupPsk(credential.IdentityBytes) is null
                && directory.Attach(credential.IdentityBytes, "new") is null && room.Take(account) is null
                && !directory.Touch(Room.Session, account, Room.Connection(account))
                && !directory.Queue(Room.Session, account, new(4, 1, 0x10, [])), "revocation did not cover every access path");
        }
    }

    internal static void QueueBounds()
    {
        using var room = new Room();
        Assert.That(!room.Directory.Queue(Room.Session, "alice", new(3, 7, 0x10, [])), "queue before registration");
        room.Ready();
        for (var i = 0; i < 128; i++)
            Assert.That(room.Directory.Queue(Room.Session, "alice", new(4, 1, 0x10, [(byte)i])), "queue rejected within bound");
        Assert.That(!room.Directory.Queue(Room.Session, "alice", new(4, 1, 0x10, [0xff])), "queue exceeded 128 deliveries");
        AssertRevoked(room);
    }

    internal static void MembershipExpansion()
    {
        using var room = new Room(members: [new("carol", 3), new("alice", 1)]);
        room.Ready();
        var oldAlice = room.Credentials["alice"];
        var oldConnection = Room.Connection("alice");
        SignalingMember[] expanded = [new("carol", 3), new("alice", 1), new("bob", 2)];
        Assert.That(room.Directory.Replace(Room.Session, "carol", expanded, DateTimeOffset.UtcNow.AddMinutes(1)), "membership expansion");
        var newCredentials = expanded.ToDictionary(member => member.Account,
            member => Credential.From(Assert.NotNull(room.Directory.Descriptor(Room.Session, member.Account), "expanded descriptor")));
        Assert.That(newCredentials["alice"] == oldAlice, "membership expansion rotated existing credentials");
        Assert.That(room.Directory.LookupPsk(oldAlice.IdentityBytes) is not null
            && room.Directory.IsAttached(Room.Session, "alice", oldConnection)
            && !room.Directory.AllRegistered(Room.Session, oldConnection), "new member did not enter the active allocation");
        Assert.That(room.Directory.Queue(Room.Session, "carol", new(4, 1, 0x10, [1]), oldConnection), "active traffic stopped during expansion");
        Assert.That(room.Take("carol") is { Family: 4, Operation: 1, Qualifier: 0x10 }, "active delivery failed during expansion");

        var connection = "expanded/bob";
        var credential = newCredentials["bob"];
        var snapshot = Assert.NotNull(room.Directory.Attach(credential.IdentityBytes, connection), "new member attachment");
        var conversation = new SignalingConversation(room.Options, room.Directory, snapshot, connection, _ => { });
        conversation.Control(new WireControl(3, []));
        conversation.Control(new WireSettings([new(0, 5000), new(1, 3), new(2, 25)]));
        conversation.Control(new WireControl(0x12, [1, 1]));
        conversation.Control(new WireControl(0x14, [1]));
        conversation.Application(1, 1, 1, Bytes.Sized(credential.SecretBytes));
        Assert.That(room.Take("alice") is { Family: 3, Operation: 7, Qualifier: 0x10 }
            && room.Take("carol") is { Family: 3, Operation: 7, Qualifier: 0x10 }, "existing peers missed expanded readiness");
        Assert.That(room.Directory.Dequeue(Room.Session, "bob", connection) is { Family: 1, Operation: 1, Qualifier: 2 }
            && room.Directory.Dequeue(Room.Session, "bob", connection) is { Family: 3, Operation: 7, Qualifier: 0x10 },
            "new peer missed registration or expanded readiness");

        var current = Assert.NotNull(room.Directory.Current(Room.Session, "alice", oldConnection), "expanded snapshot");
        Assert.That(room.Directory.LookupPsk(oldAlice.IdentityBytes) is not null
            && room.Directory.IsAttached(Room.Session, "alice", oldConnection)
            && room.Directory.IsActive(Room.Session) && room.Directory.AllRegistered(Room.Session, oldConnection)
            && current.Peers.Count == 3, "expanded topology did not preserve the active allocation");
    }

    internal static async Task Deadline()
    {
        var directory = new SignalingDirectory(Room.NewOptions());
        directory.SetListening(true);
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(400);
        Assert.That(directory.Allocate(Room.Session, "carol", Room.Members, deadline), "deadline allocation");
        var credential = Credential.From(Assert.NotNull(directory.Descriptor(Room.Session, "alice"), "deadline descriptor"));
        Assert.That(directory.Allocate(Room.Session, "carol", Room.Members, deadline.AddHours(1)), "deadline retry");
        var active = new SignalingDirectory(Room.NewOptions());
        active.SetListening(true);
        Assert.That(active.Allocate(Room.Session, "carol", Room.Members, deadline), "active deadline allocation");
        foreach (var member in Room.Members)
        {
            var key = Credential.From(Assert.NotNull(active.Descriptor(Room.Session, member.Account), "active credential"));
            var connection = "active-" + member.Account;
            Assert.That(active.Attach(key.IdentityBytes, connection) is not null, "active attachment");
            Assert.That(active.Register(Room.Session, member.Account, connection, key.SecretBytes, new(1, 1, 2, [])), "active registration");
        }
        Assert.That(active.Activate(Room.Session), "registered activation");
        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, (deadline - DateTimeOffset.UtcNow).TotalMilliseconds) + 50));
        Assert.That(!directory.IsAlive(Room.Session) && directory.Descriptor(Room.Session, "alice") is null
            && directory.LookupPsk(credential.IdentityBytes) is null && directory.Attach(credential.IdentityBytes, "late") is null,
            "retry extended original deadline");
        Assert.That(active.IsAlive(Room.Session) && active.IsActive(Room.Session)
            && active.Descriptor(Room.Session, "alice") is not null, "active transport expired with admission deadline");
        active.Remove(Room.Session);
        directory.Remove(Room.Session);
    }

    internal static void Redaction()
    {
        using var room = new Room();
        var descriptor = room.Directory.Descriptor(Room.Session, "alice")!;
        var input = new JsonObject
        {
            ["account"] = "alice", ["stage"] = "registered", ["signaling"] = descriptor,
            ["nested"] = new JsonArray(new JsonObject
            {
                ["PSK"] = "private-psk", ["IDENTITYHINT"] = "private-identity", ["Secret"] = "private-secret",
                ["customData1"] = "private-metadata", ["content"] = "private-content", ["sessionKey"] = "private-key"
            })
        };
        var original = input.ToJsonString();
        var output = Assert.NotNull(ProtocolAudit.Redact(input), "redacted output");
        Assert.That(input.ToJsonString() == original, "redaction mutated source credentials");
        Assert.That(output["account"]!.GetValue<string>() == "alice" && output["stage"]!.GetValue<string>() == "registered", "safe metadata removed");
        Assert.That(output["signaling"]!["endpoints"]![0]!["server"]!["port"]!.GetValue<int>() == 5070, "safe endpoint metadata removed");
        var serialized = output.ToJsonString();
        var credential = room.Credentials["alice"];
        foreach (var secret in new[] { credential.Identity, credential.Psk, credential.Secret, "private-" })
            Assert.That(!serialized.Contains(secret, StringComparison.Ordinal), "sensitive metadata leaked");
        foreach (var property in output["nested"]![0]!.AsObject())
            Assert.That(property.Value!.GetValue<string>() == "[REDACTED]", "sensitive field not redacted");
        Assert.That(ProtocolAudit.Redact(null) is null, "null redaction");
    }
}
