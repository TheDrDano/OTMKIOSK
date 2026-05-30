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
    public DateTimeOffset? LastShellHeartbeat { get; private set; }
    public DateTimeOffset? MaintenanceHoldUntil { get; private set; }
    public string MaintenanceHoldReason { get; private set; } = "";

    public KioskRuntime()
    {
        Policy = NormalizePolicy(_store.LoadOrCreate());
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

        Log("Info", "ServiceStarted", "SimpleKioskOS service started.");
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _downloadsGuard?.Dispose();
        _managementServer?.Stop();
        Log("Info", "ServiceStopped", "SimpleKioskOS service stopped.");
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
            policy = NormalizePolicy(policy);
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
        var maintenanceHoldActive = MaintenanceHoldUntil is not null && MaintenanceHoldUntil > now;
        return new RuntimeState
        {
            ServiceRunning = true,
            EnforcementEnabled = policy.Enforcement.Enabled && !unlockActive && !maintenanceHoldActive,
            TemporaryUnlockActive = unlockActive || maintenanceHoldActive,
            TemporaryUnlockUntil = unlockActive ? TemporaryUnlockUntil : this.MaintenanceHoldUntil,
            MaintenanceHoldActive = maintenanceHoldActive,
            MaintenanceHoldUntil = this.MaintenanceHoldUntil,
            MaintenanceHoldReason = maintenanceHoldActive ? this.MaintenanceHoldReason : "",
            PolicyName = policy.Name,
            BrandingCompanyName = policy.Branding.CompanyName,
            BrandingFooterText = policy.Branding.FooterText,
            BrandingShowFooter = policy.Branding.ShowFooter,
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

        if (MaintenanceHoldUntil is not null && MaintenanceHoldUntil > DateTimeOffset.UtcNow)
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
        MaintenanceHoldUntil = null;
        MaintenanceHoldReason = "";
        var policy = GetPolicy();
        policy.Enforcement.Enabled = true;
        SavePolicy(policy, "Kiosk enforcement locked.");
    }

    public void BeginMaintenanceHold(TimeSpan duration, string reason)
    {
        MaintenanceHoldUntil = DateTimeOffset.UtcNow.Add(duration);
        MaintenanceHoldReason = reason;
        Log("Info", "MaintenanceHoldStarted", $"{reason} Enforcement is paused until {MaintenanceHoldUntil:O}.");
    }

    public void EndMaintenanceHold(string reason)
    {
        if (MaintenanceHoldUntil is null)
        {
            return;
        }

        MaintenanceHoldUntil = null;
        MaintenanceHoldReason = "";
        Log("Info", "MaintenanceHoldEnded", reason);
    }

    public void MarkShellHeartbeat()
    {
        LastShellHeartbeat = DateTimeOffset.UtcNow;
    }

    public bool IsShellReady(KioskPolicy? policy = null)
    {
        policy ??= GetPolicy();
        var graceSeconds = Math.Clamp(policy.Enforcement.ShellHeartbeatGraceSeconds, 3, 120);
        return LastShellHeartbeat is not null
            && LastShellHeartbeat.Value >= DateTimeOffset.UtcNow.AddSeconds(-graceSeconds);
    }

    public void EmergencyDisableEnforcement(string reason)
    {
        TemporaryUnlockUntil = DateTimeOffset.UtcNow.AddHours(24);
        var policy = GetPolicy();
        policy.Enforcement.Enabled = false;
        SavePolicy(policy, reason);
        Log("Warning", "EmergencyDisableEnforcement", reason);
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

    private static KioskPolicy NormalizePolicy(KioskPolicy policy)
    {
        policy.Enforcement ??= new EnforcementPolicy();
        policy.Restrictions ??= new RestrictionPolicy();
        policy.Browser ??= new BrowserPolicy();
        policy.Branding ??= new BrandingPolicy();
        if (string.IsNullOrWhiteSpace(policy.Branding.CompanyName))
        {
            policy.Branding.CompanyName = "OTM";
        }

        if (string.IsNullOrWhiteSpace(policy.Branding.FooterText))
        {
            policy.Branding.FooterText = $"Powered by {policy.Branding.CompanyName}";
        }

        policy.DedicatedKiosk ??= new DedicatedKioskPolicy();
        policy.Remote ??= new RemoteManagementPolicy();
        policy.Monitoring ??= new RemoteMonitoringPolicy();
        policy.Updates ??= new UpdatePolicy();
        policy.Launchers ??= [];
        policy.AllowedApps ??= [];
        policy.BackgroundApps ??= [];
        policy.BlockedApps ??= [];
        policy.RequiredApps ??= [];
        policy.Admin ??= AdminCredential.CreateDefault();
        return policy;
    }
}
