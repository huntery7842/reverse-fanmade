using System.Text.Json.Serialization;

namespace ReVerse.Capture.Persistence;


public sealed record SaveWrite
{
    public required string RequestId { get; init; }
    public required long ExpectedRevision { get; init; }
    public required byte[] Payload { get; init; }
    public required string BuildId { get; init; }
    public required string FormatVersion { get; init; }
    public required string CatalogueId { get; init; }
    public string? ClientSaveHash { get; init; }
    public BattlePassState? BattlePass { get; init; }
}

public sealed record SaveSnapshot(string Slot, long Revision, string RequestId, string PayloadDigest,
    byte[] Payload, string BuildId, string FormatVersion, string CatalogueId, string? ClientSaveHash,
    BattlePassState? BattlePass, long CreatedAt);

public sealed record BattlePassState
{
    public required List<BattlePassQuest> ChallengeQuestList { get; init; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public required long AccumulatedCP { get; init; }
    public required int FreeRewardCPLevel { get; init; }
    public required int PremiumRewardCPLevel { get; init; }
    public required bool IsCurrentPremiumBattlePassBought { get; init; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public required long AccumulatedStarCP { get; init; }
    public required int LastUsedStarLevel { get; init; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public required long StatStarLevelAccumlatedCPTotal { get; init; }
    public required int StatStarLevelCompleteCountTUReset { get; init; }
    public required int StatStarLevelCompleteCountTotal { get; init; }
    public required int StatStarLevelGotRPBoosterFree { get; init; }
    public required int StatStarLevelGotRPBoosterPremium { get; init; }

    public required int LastPatchTime { get; init; }
}

public sealed record BattlePassQuest
{
    public required int UIIndex { get; init; }

    public required string QuestID { get; init; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public required long QuestBeginTime { get; init; }
    public required string CurrentQuestPoint { get; init; }
    public required bool IsPremium { get; init; }
}
