using Otm.Kiosk.Shared.Models;

namespace Otm.Kiosk.Shared.Storage;

public static class PolicyTemplates
{
    public static KioskPolicy Default()
    {
        return new KioskPolicy
        {
            Name = "Default Local Policy",
            Enforcement = new EnforcementPolicy
            {
                Enabled = false,
                StrictApplicationWhitelist = false,
                RestartRequiredApps = true,
                FullscreenOverlay = false
            },
            BlockedApps = DefaultBlockedApps()
        };
    }

    public static KioskPolicy ExamMode()
    {
        var policy = Default();
        policy.Name = "Exam Mode";
        policy.Enforcement.Enabled = true;
        policy.Enforcement.StrictApplicationWhitelist = true;
        policy.Browser.WhitelistOnly = true;
        policy.AllowedApps =
        [
            new AppRule { DisplayName = "Approved Testing Browser", ProcessName = "msedge.exe" },
            new AppRule { DisplayName = "Testing App", ProcessName = "testing.exe" }
        ];
        policy.Browser.AllowedSites =
        [
            "https://testing.example.edu/*"
        ];
        return policy;
    }

    public static KioskPolicy LabLockdown()
    {
        var policy = Default();
        policy.Name = "Lab Lockdown";
        policy.Enforcement.Enabled = true;
        policy.Enforcement.StrictApplicationWhitelist = true;
        policy.Browser.WhitelistOnly = false;
        policy.AllowedApps =
        [
            new AppRule { DisplayName = "Microsoft Edge", ProcessName = "msedge.exe" },
            new AppRule { DisplayName = "Google Chrome", ProcessName = "chrome.exe" },
            new AppRule { DisplayName = "Office", ProcessName = "winword.exe" },
            new AppRule { DisplayName = "Excel", ProcessName = "excel.exe" },
            new AppRule { DisplayName = "PowerPoint", ProcessName = "powerpnt.exe" },
            new AppRule { DisplayName = "Approved Lab App", ProcessName = "labapp.exe" }
        ];
        return policy;
    }

    public static List<AppRule> DefaultBlockedApps() =>
    [
        new AppRule { DisplayName = "Task Manager", ProcessName = "Taskmgr.exe" },
        new AppRule { DisplayName = "Command Prompt", ProcessName = "cmd.exe" },
        new AppRule { DisplayName = "PowerShell", ProcessName = "powershell.exe" },
        new AppRule { DisplayName = "PowerShell 7", ProcessName = "pwsh.exe" },
        new AppRule { DisplayName = "Registry Editor", ProcessName = "regedit.exe" },
        new AppRule { DisplayName = "Control Panel", ProcessName = "control.exe" },
        new AppRule { DisplayName = "Windows Settings", ProcessName = "SystemSettings.exe" },
        new AppRule { DisplayName = "Microsoft Store", ProcessName = "WinStore.App.exe" },
        new AppRule { DisplayName = "Windows Terminal", ProcessName = "WindowsTerminal.exe" },
        new AppRule { DisplayName = "Installer", ProcessName = "msiexec.exe" }
    ];
}
