using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ReVerse.Capture.Protocol;

namespace ReVerse.Capture.Persistence;

public sealed class SaveRepository
{
    public const int MaxPayloadBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly RelayAccounts accounts;

    public SaveRepository(RelayAccounts accounts)
    {
        this.accounts = accounts;
        using var connection = accounts.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "CREATE TABLE IF NOT EXISTS save_schema_versions (version INTEGER PRIMARY KEY)";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT COUNT(*) FROM save_schema_versions WHERE version=1";
        if ((long)command.ExecuteScalar()! == 0)
        {
            using var stream = typeof(SaveRepository).Assembly.GetManifestResourceStream("ReVerse.Capture.Persistence.Schema.001_saves.sql")
                ?? throw new InvalidOperationException("Missing save schema resource.");
            using var reader = new StreamReader(stream);
            command.CommandText = reader.ReadToEnd();
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO save_schema_versions VALUES (1)";
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public static void ValidateSlot(string slot)
    {
        if (slot.Length is < 1 or > 64 || slot.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new BadHttpRequestException("Invalid slot name; use 1–64 ASCII letters, digits, hyphens or underscores.", 400);
    }

    private static void Validate(SaveWrite write)
    {
        if (!Guid.TryParseExact(write.RequestId, "N", out _) || write.ExpectedRevision < 0 || write.ExpectedRevision == long.MaxValue)
            throw new BadHttpRequestException("Use a requestId UUID without hyphens and a nonnegative expectedRevision.", 400);
        if (write.Payload is null || write.Payload.Length == 0)
            throw new BadHttpRequestException("A complete nonempty save-container payload is required.", 400);
        if (write.Payload.Length > MaxPayloadBytes) throw new BadHttpRequestException("Save payload exceeds 8 MiB.", 413);
        foreach (var label in new[] { write.BuildId, write.FormatVersion, write.CatalogueId })
            if (string.IsNullOrWhiteSpace(label) || label.Length > 128 || label.Any(char.IsControl))
                throw new BadHttpRequestException("Build, format and catalogue identifiers are required (up to 128 characters).", 400);
        if (write.ClientSaveHash?.Length > 1024) throw new BadHttpRequestException("Client save hash metadata is too long.", 400);
        if (write.BattlePass is not { } bp) return;
        if (bp.ChallengeQuestList is null || bp.ChallengeQuestList.Count != 9 || bp.ChallengeQuestList.Any(q => q is null))
            throw new BadHttpRequestException("Battle-pass projections require exactly nine non-null quests from a client-generated save.", 400);
        foreach (var quest in bp.ChallengeQuestList)
            foreach (var number in new[] { quest.QuestID, quest.CurrentQuestPoint })
                if (!ulong.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
                    value.ToString(CultureInfo.InvariantCulture) != number)
                    throw new BadHttpRequestException("Quest IDs and points must be canonical unsigned 64-bit decimal strings.", 400);
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Key, object? Value)[] values)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return command;
    }

    public SaveSnapshot Put(string accountId, string slot, SaveWrite write)
    {
        ValidateSlot(slot); Validate(write);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(write, Json)));
        using var connection = accounts.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        long? retryRevision = null;
        using (var retry = Command(connection, transaction,
            "SELECT revision, request_digest FROM save_snapshots WHERE account_id=$a AND slot=$s AND request_id=$r",
            ("$a", accountId), ("$s", slot), ("$r", write.RequestId)))
        using (var reader = retry.ExecuteReader())
        {
            if (reader.Read())
            {
                if (reader.GetString(1) != fingerprint)
                    throw new BadHttpRequestException("Request ID already used with different contents.", 409);
                retryRevision = reader.GetInt64(0);
            }
        }
        if (retryRevision is not null)
        {
            var retried = Read(connection, transaction, accountId, slot, retryRevision);
            transaction.Commit();
            return retried;
        }
        using var head = Command(connection, transaction,
            "SELECT revision FROM save_heads WHERE account_id=$a AND slot=$s", ("$a", accountId), ("$s", slot));
        var current = (long?)head.ExecuteScalar() ?? 0;
        if (current != write.ExpectedRevision)
            throw new BadHttpRequestException($"Stale save revision: current is {current}.", 409);
        var revision = checked(current + 1);
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var digest = Convert.ToHexString(SHA256.HashData(write.Payload));
        var bpJson = write.BattlePass is null ? null : JsonSerializer.Serialize(write.BattlePass, Json);
        using (var insert = Command(connection, transaction, """
            INSERT INTO save_snapshots VALUES ($a,$s,$v,$r,$fingerprint,$payload,$digest,$build,$format,$catalogue,$clienthash,$bp,$created)
            """, ("$a", accountId), ("$s", slot), ("$v", revision), ("$r", write.RequestId),
            ("$fingerprint", fingerprint), ("$payload", write.Payload), ("$digest", digest), ("$build", write.BuildId),
            ("$format", write.FormatVersion), ("$catalogue", write.CatalogueId), ("$clienthash", write.ClientSaveHash),
            ("$bp", bpJson), ("$created", created))) insert.ExecuteNonQuery();
        if (write.BattlePass is { } bp)
        {
            using (var progress = Command(connection, transaction, """
                INSERT INTO battlepass_progress VALUES ($a,$s,$v,$catalogue,$cp,$free,$premium,$bought,$star,$used,$total,$reset,$complete,$freebooster,$premiumbooster,$patch)
                """, ("$a", accountId), ("$s", slot), ("$v", revision), ("$catalogue", write.CatalogueId),
                ("$cp", bp.AccumulatedCP), ("$free", bp.FreeRewardCPLevel), ("$premium", bp.PremiumRewardCPLevel),
                ("$bought", bp.IsCurrentPremiumBattlePassBought), ("$star", bp.AccumulatedStarCP), ("$used", bp.LastUsedStarLevel),
                ("$total", bp.StatStarLevelAccumlatedCPTotal), ("$reset", bp.StatStarLevelCompleteCountTUReset),
                ("$complete", bp.StatStarLevelCompleteCountTotal), ("$freebooster", bp.StatStarLevelGotRPBoosterFree),
                ("$premiumbooster", bp.StatStarLevelGotRPBoosterPremium), ("$patch", bp.LastPatchTime))) progress.ExecuteNonQuery();
            for (var ordinal = 0; ordinal < bp.ChallengeQuestList.Count; ordinal++)
            {
                var quest = bp.ChallengeQuestList[ordinal];
                using var insertQuest = Command(connection, transaction,
                    "INSERT INTO battlepass_quests VALUES ($a,$s,$v,$ordinal,$ui,$id,$time,$points,$premium)",
                    ("$a", accountId), ("$s", slot), ("$v", revision), ("$ordinal", ordinal), ("$ui", quest.UIIndex),
                    ("$id", quest.QuestID), ("$time", quest.QuestBeginTime), ("$points", quest.CurrentQuestPoint), ("$premium", quest.IsPremium));
                insertQuest.ExecuteNonQuery();
            }
        }
        using var update = Command(connection, transaction, """
            INSERT INTO save_heads VALUES ($a,$s,$v) ON CONFLICT(account_id,slot) DO UPDATE SET revision=excluded.revision
            """, ("$a", accountId), ("$s", slot), ("$v", revision));
        update.ExecuteNonQuery();
        transaction.Commit();
        return new SaveSnapshot(slot, revision, write.RequestId, digest, write.Payload, write.BuildId,
            write.FormatVersion, write.CatalogueId, write.ClientSaveHash, write.BattlePass, created);
    }

    public SaveSnapshot Get(string accountId, string slot, long? revision)
    {
        ValidateSlot(slot);
        if (revision is <= 0) throw new BadHttpRequestException("Revision must be positive.", 400);
        using var connection = accounts.Open();
        return Read(connection, null, accountId, slot, revision);
    }

    private static SaveSnapshot Read(SqliteConnection connection, SqliteTransaction? transaction,
        string accountId, string slot, long? revision)
    {
        using var command = Command(connection, transaction, """
            SELECT revision,request_id,payload_digest,payload,build_id,format_version,catalogue_id,client_save_hash,battlepass_json,created_at
            FROM save_snapshots WHERE account_id=$a AND slot=$s AND revision=COALESCE($v,
                (SELECT revision FROM save_heads WHERE account_id=$a AND slot=$s))
            """, ("$a", accountId), ("$s", slot), ("$v", revision));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new BadHttpRequestException("Save not found; obtain a default save from the client.", 404);
        return new SaveSnapshot(slot, reader.GetInt64(0), reader.GetString(1), reader.GetString(2), (byte[])reader[3],
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : JsonSerializer.Deserialize<BattlePassState>(reader.GetString(8), Json), reader.GetInt64(9));
    }
}
