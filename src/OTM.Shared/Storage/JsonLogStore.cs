using System.Text.Json;
using Otm.Kiosk.Shared.Models;

namespace Otm.Kiosk.Shared.Storage;

public sealed class JsonLogStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    private readonly object _lock = new();

    public void Append(LogEntry entry)
    {
        Directory.CreateDirectory(KioskPaths.RootDirectory);
        var json = JsonSerializer.Serialize(entry, Options);
        lock (_lock)
        {
            File.AppendAllText(KioskPaths.LogPath, json + Environment.NewLine);
        }
    }

    public IReadOnlyList<LogEntry> ReadLatest(int count)
    {
        if (!File.Exists(KioskPaths.LogPath))
        {
            return [];
        }

        var lines = File.ReadLines(KioskPaths.LogPath).Reverse().Take(count).Reverse();
        var entries = new List<LogEntry>();
        foreach (var line in lines)
        {
            try
            {
                var entry = JsonSerializer.Deserialize<LogEntry>(line, Options);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch
            {
                // Ignore malformed log lines so one bad write does not break local management.
            }
        }

        return entries;
    }
}
