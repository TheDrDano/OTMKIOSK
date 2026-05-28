using Otm.Kiosk.Shared.Storage;

namespace Otm.Kiosk.Service;

public sealed class DownloadsGuard : IDisposable
{
    private readonly KioskRuntime _runtime;
    private readonly SqliteKioskStore _logs;
    private readonly List<FileSystemWatcher> _watchers = [];

    public DownloadsGuard(KioskRuntime runtime, SqliteKioskStore logs)
    {
        _runtime = runtime;
        _logs = logs;
    }

    public void Start()
    {
        Reload();
    }

    public void Reload()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();

        foreach (var path in GetDownloadDirectories())
        {
            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                watcher.Created += (_, args) => HandlePath(args.FullPath);
                watcher.Renamed += (_, args) => HandlePath(args.FullPath);
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                _runtime.Log("Error", "DownloadWatcherFailed", $"Could not watch {path}: {ex.Message}", path: path);
            }
        }
    }

    private void HandlePath(string path)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(750);
            GuardFile(path);
        });
    }

    private void GuardFile(string path)
    {
        var policy = _runtime.GetPolicy();
        if (!_runtime.IsEnforcementActive() || !policy.Restrictions.BlockDownloads || !File.Exists(path))
        {
            return;
        }

        var extension = Path.GetExtension(path);
        var blocked = policy.Restrictions.BlockedDownloadExtensions.Any(value =>
            string.Equals(value, extension, StringComparison.OrdinalIgnoreCase));

        if (!blocked)
        {
            return;
        }

        try
        {
            if (policy.Restrictions.DeleteBlockedDownloads)
            {
                File.Delete(path);
                _runtime.Log("Warning", "DownloadDeleted", $"Blocked download deleted: {Path.GetFileName(path)}", path: path);
                return;
            }

            if (policy.Restrictions.QuarantineBlockedDownloads)
            {
                Directory.CreateDirectory(KioskPaths.QuarantineDirectory);
                var destination = Path.Combine(KioskPaths.QuarantineDirectory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{Path.GetFileName(path)}");
                File.Move(path, destination, overwrite: true);
                _runtime.Log("Warning", "DownloadQuarantined", $"Blocked download quarantined: {Path.GetFileName(path)}", path: destination);
            }
        }
        catch (Exception ex)
        {
            _runtime.Log("Error", "DownloadBlockFailed", $"Could not block download {path}: {ex.Message}", path: path);
        }
    }

    private static IEnumerable<string> GetDownloadDirectories()
    {
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var usersRoot = Path.Combine(systemDrive, "Users");
        if (!Directory.Exists(usersRoot))
        {
            yield break;
        }

        foreach (var userDirectory in Directory.EnumerateDirectories(usersRoot))
        {
            var name = Path.GetFileName(userDirectory);
            if (name.StartsWith("Default", StringComparison.OrdinalIgnoreCase) || name.Equals("Public", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var downloads = Path.Combine(userDirectory, "Downloads");
            if (Directory.Exists(downloads))
            {
                yield return downloads;
            }
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
    }
}
