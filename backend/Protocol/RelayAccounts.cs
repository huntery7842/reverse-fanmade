using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ReVerse.Capture.Protocol;


public sealed class RelayAccounts
{
    public const int Iterations = 600_000;
    private readonly string connectionString;
    private readonly object gate = new();

    public RelayAccounts(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var path = Path.GetFullPath(configuration["Relay:DatabasePath"] ?? "data/accounts.db", environment.ContentRootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS accounts (
                id TEXT PRIMARY KEY, username TEXT NOT NULL COLLATE BINARY,
                salt BLOB NOT NULL, hash BLOB NOT NULL,
                algorithm TEXT NOT NULL, iterations INTEGER NOT NULL,
                created_at INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS accounts_username ON accounts(username);
            CREATE TABLE IF NOT EXISTS relay_sessions (
                token_hash BLOB PRIMARY KEY, account_id TEXT NOT NULL REFERENCES accounts(id),
                expires_at INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    internal SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    public LoginResult Login(string? username, string? secret)
    {
        username = username?.Trim();
        if (string.IsNullOrEmpty(username) || username.Length > 64 || username.Any(char.IsControl) ||
            string.IsNullOrEmpty(secret) || secret.Length > 1024)
            throw new BadHttpRequestException("Username (1–64 characters) and secret key (1–1024 characters) are required.", 400);
        lock (gate)
        {
            using var connection = Open();

            using var transaction = connection.BeginTransaction(deferred: false);
            string? accountId = null;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT id, salt, hash, algorithm, iterations FROM accounts WHERE username = $name";
                command.Parameters.AddWithValue("$name", username);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.GetString(3) != "PBKDF2-SHA256") throw new InvalidDataException("Unsupported account hash algorithm.");
                    var computed = Rfc2898DeriveBytes.Pbkdf2(secret, (byte[])reader[1], reader.GetInt32(4), HashAlgorithmName.SHA256, 32);
                    var matches = CryptographicOperations.FixedTimeEquals(computed, (byte[])reader[2]);
                    CryptographicOperations.ZeroMemory(computed);
                    if (matches) { accountId = reader.GetString(0); break; }
                }
            }
            var created = accountId is null;
            if (created)
            {
                accountId = Guid.NewGuid().ToString("N");
                var salt = RandomNumberGenerator.GetBytes(32);
                var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, 32);
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO accounts VALUES ($id, $name, $salt, $hash, 'PBKDF2-SHA256', $iterations, $now)";
                insert.Parameters.AddWithValue("$id", accountId);
                insert.Parameters.AddWithValue("$name", username);
                insert.Parameters.AddWithValue("$salt", salt);
                insert.Parameters.AddWithValue("$hash", hash);
                insert.Parameters.AddWithValue("$iterations", Iterations);
                insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                insert.ExecuteNonQuery();
            }
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expires = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
            using var session = connection.CreateCommand();
            session.Transaction = transaction;
            session.CommandText = """
                DELETE FROM relay_sessions WHERE expires_at <= $now;
                INSERT INTO relay_sessions VALUES ($token, $id, $expires);
                """;
            session.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            session.Parameters.AddWithValue("$token", HashToken(token));
            session.Parameters.AddWithValue("$id", accountId);
            session.Parameters.AddWithValue("$expires", expires);
            session.ExecuteNonQuery();
            transaction.Commit();
            return new LoginResult(accountId!, username, token, expires, created);
        }
    }

    public SteamIdentity Resolve(string? token)
    {
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit))
            throw new BadHttpRequestException("Start the relay to sign in.", 401);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, a.username FROM relay_sessions s JOIN accounts a ON a.id = s.account_id
            WHERE s.token_hash = $token AND s.expires_at > $now
            """;
        command.Parameters.AddWithValue("$token", HashToken(token));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new BadHttpRequestException("Relay session expired or invalid; stop and start the relay.", 401);



        return new SteamIdentity(reader.GetString(0), reader.GetString(1));
    }

    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));
    public sealed record LoginResult(string AccountId, string Username, string SessionToken, long ExpiresAt, bool Created);
}
