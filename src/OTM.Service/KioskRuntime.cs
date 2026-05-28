using Otm.Kiosk.Shared.Models;
using Otm.Kiosk.Shared.Storage;

namespace Otm.Kiosk.Service;

public sealed class KioskRuntime : IDisposable
{
    private readonly SqliteKioskStore _store = new();
    private readonly object _policyLock = new();
    private CancellationTokenSource? _cts;
    private ProcessEnforcer? _processEnforcer;
    private DownloadsGuard? _downloadsGuard;
    private LocalManagementServer? _managementServer;

    public KioskPolicy Policy { get; private set; }
    public DateTimeOffset? TemporaryUnlockUntil { get; private set; }

    public KioskRuntime()
    {
        Policy = _store.LoadOrCreate();
    }

    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _processEnforcer = new ProcessEnforcer(this, _store);
        _downloadsGuard = new DownloadsGuard(this, _store);
        _managementServer = new LocalManagementServer(this, _store);

        _downloadsGuard.Start();
        _ = _processEnforcer.RunAsync(_cts.Token);
        _ = _managementServer.RunAsync(_cts.Token);

        Log("Info", "ServiceStarted", "OTM Kiosk Service started.");
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _downloadsGuard?.Dispose();
        _managementServer?.Stop();
        Log("Info", "ServiceStopped", "OTM Kiosk Service stopped.");
        await Task.CompletedTask;
    }

    public KioskPolicy GetPolicy()
    {
        lock (_policyLock)
        {
            return Policy;
        }
    }

    public void SavePolicy(KioskPolicy policy, string eventMessage)
    {
        lock (_policyLock)
        {
            policy.Version = Math.Max(Policy.Version + 1, policy.Version);
            Policy = policy;
            _store.Save(policy);
            _downloadsGuard?.Reload();
        }

        Log("Info", "PolicyChanged", eventMessage);
    }

    public RuntimeState GetState()
    {
        var policy = GetPolicy();
        var now = DateTimeOffset.UtcNow;
        var unlockActive = TemporaryUnlockUntil is not null && TemporaryUnlockUntil > now;
        return new RuntimeState
        {
            ServiceRunning = true,
            EnforcementEnabled = policy.Enforcement.Enabled && !unlockActive,
            TemporaryUnlockActive = unlockActive,
            TemporaryUnlockUntil = TemporaryUnlockUntil,
            PolicyName = policy.Name,
            CurrentTime = now
        };
    }

    public bool IsEnforcementActive()
    {
        var policy = GetPolicy();
        if (!policy.Enforcement.Enabled)
        {
            return false;
        }

        if (TemporaryUnlockUntil is null)
        {
            return true;
        }

        return TemporaryUnlockUntil <= DateTimeOffset.UtcNow;
    }

    public void TemporaryUnlock(TimeSpan duration)
    {
        TemporaryUnlockUntil = DateTimeOffset.UtcNow.Add(duration);
        Log("Warning", "TemporaryUnlock", $"Kiosk enforcement unlocked until {TemporaryUnlockUntil:O}.");
    }

    public void Relock()
    {
        TemporaryUnlockUntil = null;
        var policy = GetPolicy();
        policy.Enforcement.Enabled = true;
        SavePolicy(policy, "Kiosk enforcement locked.");
    }

    public void Log(string level, string eventType, string message, string? processName = null, string? path = null)
    {
        _store.Append(new LogEntry
        {
            Level = level,
            EventType = eventType,
            Message = message,
            ProcessName = processName,
            Path = path,
            UserName = Environment.UserName
        });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _downloadsGuard?.Dispose();
        _managementServer?.Stop();
    }
}
