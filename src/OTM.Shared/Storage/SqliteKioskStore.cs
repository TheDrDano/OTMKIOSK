using System.Text.Json;
using Microsoft.Data.Sqlite;
using Otm.Kiosk.Shared.Models;

namespace Otm.Kiosk.Shared.Storage;

public sealed class SqliteKioskStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _lock = new();

    public SqliteKioskStore()
    {
        Directory.CreateDirectory(KioskPaths.RootDirectory);
        Directory.CreateDirectory(KioskPaths.QuarantineDirectory);
        Initialize();
    }

    public KioskPolicy LoadOrCreate()
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            var json = ReadSetting(connection, "policy");
            if (!string.IsNullOrWhiteSpace(json))
            {
                return JsonSerializer.Deserialize<KioskPolicy>(json, JsonOptions) ?? PolicyTemplates.Default();
            }

            var policy = TryMigrateJsonPolicy() ?? PolicyTemplates.Default();
            SavePolicy(connection, policy);
            return policy;
        }
    }

    public void Save(KioskPolicy policy)
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            SavePolicy(connection, policy);
        }
    }

    public void Append(LogEntry entry)
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO logs (timestamp, level, event_type, message, process_name, path, user_name)
                VALUES ($timestamp, $level, $event_type, $message, $process_name, $path, $user_name);
                """;
            command.Parameters.AddWithValue("$timestamp", entry.Timestamp.ToString("O"));
            command.Parameters.AddWithValue("$level", entry.Level);
            command.Parameters.AddWithValue("$event_type", entry.EventType);
            command.Parameters.AddWithValue("$message", entry.Message);
            command.Parameters.AddWithValue("$process_name", (object?)entry.ProcessName ?? DBNull.Value);
            command.Parameters.AddWithValue("$path", (object?)entry.Path ?? DBNull.Value);
            command.Parameters.AddWithValue("$user_name", (object?)entry.UserName ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<LogEntry> ReadLatest(int count)
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT timestamp, level, event_type, message, process_name, path, user_name
                FROM logs
                ORDER BY id DESC
                LIMIT $count;
                """;
            command.Parameters.AddWithValue("$count", Math.Clamp(count, 1, 1000));

            using var reader = command.ExecuteReader();
            var entries = new List<LogEntry>();
            while (reader.Read())
            {
                entries.Add(new LogEntry
                {
                    Timestamp = DateTimeOffset.TryParse(reader.GetString(0), out var timestamp)
                        ? timestamp
                        : DateTimeOffset.UtcNow,
                    Level = reader.GetString(1),
                    EventType = reader.GetString(2),
                    Message = reader.GetString(3),
                    ProcessName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Path = reader.IsDBNull(5) ? null : reader.GetString(5),
                    UserName = reader.IsDBNull(6) ? null : reader.GetString(6)
                });
            }

            entries.Reverse();
            return entries;
        }
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={KioskPaths.DatabasePath}");
        connection.Open();
        return connection;
    }

    private static string? ReadSetting(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static void SavePolicy(SqliteConnection connection, KioskPolicy policy)
    {
        policy.UpdatedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(policy.Admin.InitialRecoveryKey))
        {
            File.WriteAllText(Path.Combine(KioskPaths.RootDirectory, "first-run-recovery-key.txt"),
                $"OTM Kiosk recovery key{Environment.NewLine}{policy.Admin.InitialRecoveryKey}{Environment.NewLine}");
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings (key, value, updated_at)
            VALUES ('policy', $value, $updated_at)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(policy, JsonOptions));
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static KioskPolicy? TryMigrateJsonPolicy()
    {
        if (!File.Exists(KioskPaths.PolicyPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(KioskPaths.PolicyPath);
            return JsonSerializer.Deserialize<KioskPolicy>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void Initialize()
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS logs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    level TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    message TEXT NOT NULL,
                    process_name TEXT NULL,
                    path TEXT NULL,
                    user_name TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_logs_timestamp ON logs(timestamp);
                """;
            command.ExecuteNonQuery();
        }
    }
}
