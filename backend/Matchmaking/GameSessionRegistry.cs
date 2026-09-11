using System.Text.Json.Nodes;

namespace ReVerse.Capture.Matchmaking;


public sealed class GameSessionRegistry
{
    internal Dictionary<string, Session> Sessions { get; } = new(StringComparer.Ordinal);

    internal Session Create(string[] accounts, int capacity, DateTimeOffset deadline)
    {
        var session = new Session(accounts, capacity, deadline);
        Sessions.Add(session.Id, session);
        return session;
    }

    internal sealed class Session(string[] accounts, int capacity, DateTimeOffset deadline)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public HashSet<string> Reserved { get; } = new(accounts, StringComparer.Ordinal);
        public Dictionary<string, JsonObject> Players { get; } = new(StringComparer.Ordinal);
        public string Representative { get; set; } = accounts[0];
        public int Capacity { get; } = capacity;
        public DateTimeOffset Deadline { get; } = deadline;
        public long Created { get; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public long Sequence { get; set; } = 1;
        public bool JoinDisabled { get; set; }
        public bool UseCrossPlay { get; set; }
        public bool UsePlayerSession => false;
        public string OfferId { get; set; } = "";
        public string ServiceEncryptionKey => "";
        public int? SignalingTimeoutSeconds { get; private set; }
        public DateTimeOffset? SignalingDeadline { get; private set; }
        public bool ProviderAllocated { get; set; }


        public string SignalingStatus => ProviderAllocated ? "COMPLETED" : SignalingDeadline is null ? "NONE" : "IN_PROGRESS";

        public void RequestSignaling(int timeoutSeconds, bool providerEnabled = false)
        {
            if (SignalingTimeoutSeconds is { } existing)
            {
                if (existing != timeoutSeconds)
                    throw MatchmakingCoordinator.Error(409, "A signaling request with a different timeout is already pending.");
                return;
            }
            var requestedDeadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
            SignalingDeadline = providerEnabled || requestedDeadline < Deadline ? requestedDeadline : Deadline;
            SignalingTimeoutSeconds = timeoutSeconds;




        }
        public string CustomData1 { get; set; } = "";
        public string CustomData2 { get; set; } = "";
    }
}
