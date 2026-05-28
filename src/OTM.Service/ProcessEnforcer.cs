using System.Diagnostics;
using Otm.Kiosk.Shared.Models;
using Otm.Kiosk.Shared.Storage;

namespace Otm.Kiosk.Service;

public sealed class ProcessEnforcer
{
    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services",
        "lsass", "svchost", "fontdrvhost", "dwm", "explorer", "sihost", "taskhostw",
        "RuntimeBroker", "SearchIndexer", "StartMenuExperienceHost", "ShellExperienceHost",
        "SecurityHealthSystray", "OTM.Service", "OTM.ControlPanel", "OTM.RecoveryTool"
    };

    private readonly KioskRuntime _runtime;
    private readonly JsonLogStore _logs;

    public ProcessEnforcer(KioskRuntime runtime, JsonLogStore logs)
    {
        _runtime = runtime;
        _logs = logs;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                EnforceProcesses();
                RestartRequiredApps();
            }
            catch (Exception ex)
            {
                _runtime.Log("Error", "EnforcementError", ex.Message);
            }

            var interval = Math.Clamp(_runtime.GetPolicy().Enforcement.ProcessScanIntervalMs, 500, 10_000);
            await Task.Delay(interval, cancellationToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    private void EnforceProcesses()
    {
        if (!_runtime.IsEnforcementActive())
        {
            return;
        }

        var policy = _runtime.GetPolicy();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var name = SafeProcessName(process);
                if (string.IsNullOrWhiteSpace(name) || ProtectedProcesses.Contains(name))
                {
                    continue;
                }

                var path = SafeMainModulePath(process);
                if (IsBlocked(policy, name, path))
                {
                    Kill(process, "BlockedProcess", $"Blocked process killed: {name}", path);
                    continue;
                }

                if (policy.Enforcement.StrictApplicationWhitelist && ShouldCheckWhitelist(process, path) && !IsAllowed(policy, name, path))
                {
                    Kill(process, "WhitelistViolation", $"Unapproved process killed: {name}", path);
                }
            }
        }
    }

    private void RestartRequiredApps()
    {
        var policy = _runtime.GetPolicy();
        if (!_runtime.IsEnforcementActive() || !policy.Enforcement.RestartRequiredApps)
        {
            return;
        }

        foreach (var app in policy.RequiredApps.Where(static app => app.Required && !string.IsNullOrWhiteSpace(app.Path)))
        {
            var expectedName = Path.GetFileNameWithoutExtension(app.ProcessName);
            var isRunning = Process.GetProcessesByName(expectedName).Any();
            if (isRunning)
            {
                continue;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = app.Path!,
                    Arguments = app.Arguments ?? "",
                    UseShellExecute = true
                });
                _runtime.Log("Info", "RequiredAppRestarted", $"Required app restarted: {app.DisplayName}", app.ProcessName, app.Path);
            }
            catch (Exception ex)
            {
                _runtime.Log("Error", "RequiredAppRestartFailed", $"Could not restart {app.DisplayName}: {ex.Message}", app.ProcessName, app.Path);
            }
        }
    }

    private static bool IsBlocked(KioskPolicy policy, string processName, string? path)
    {
        if (policy.Restrictions.BlockSystemTools && PolicyPresets.DefaultBlockedApps().Any(rule => Matches(rule, processName, path)))
        {
            return true;
        }

        return policy.BlockedApps.Any(rule => Matches(rule, processName, path));
    }

    private static bool IsAllowed(KioskPolicy policy, string processName, string? path)
    {
        return policy.AllowedApps.Any(rule => Matches(rule, processName, path));
    }

    private static bool Matches(AppRule rule, string processName, string? path)
    {
        var ruleProcess = Path.GetFileNameWithoutExtension(rule.ProcessName);
        var candidateProcess = Path.GetFileNameWithoutExtension(processName);
        if (!string.IsNullOrWhiteSpace(ruleProcess) && string.Equals(ruleProcess, candidateProcess, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(rule.Path)
            && !string.IsNullOrWhiteSpace(path)
            && string.Equals(Path.GetFullPath(rule.Path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldCheckWhitelist(Process process, string? path)
    {
        if (process.MainWindowHandle != IntPtr.Zero)
        {
            return true;
        }

        return path is not null
            && (path.Contains(@"\Downloads\", StringComparison.OrdinalIgnoreCase)
                || path.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase));
    }

    private void Kill(Process process, string eventType, string message, string? path)
    {
        try
        {
            var name = SafeProcessName(process);
            process.Kill(entireProcessTree: true);
            _runtime.Log("Warning", eventType, message, name, path);
        }
        catch (Exception ex)
        {
            _runtime.Log("Error", "ProcessKillFailed", ex.Message, SafeProcessName(process), path);
        }
    }

    private static string SafeProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    private static string? SafeMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
