using Otm.Kiosk.Shared.Models;

namespace Otm.Kiosk.Shared.Storage;

public static class PolicyDefaults
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
        new AppRule { DisplayName = "Windows Explorer", ProcessName = "explorer.exe" },
        new AppRule { DisplayName = "Windows Terminal", ProcessName = "WindowsTerminal.exe" },
        new AppRule { DisplayName = "Installer", ProcessName = "msiexec.exe" },
        new AppRule { DisplayName = "Windows Security", ProcessName = "SecurityHealthSystray.exe" },
        new AppRule { DisplayName = "Windows Security", ProcessName = "SecHealthUI.exe" },
        new AppRule { DisplayName = "System Configuration", ProcessName = "msconfig.exe" },
        new AppRule { DisplayName = "Services", ProcessName = "services.exe" },
        new AppRule { DisplayName = "Computer Management", ProcessName = "compmgmt.msc" },
        new AppRule { DisplayName = "Microsoft Management Console", ProcessName = "mmc.exe" }
    ];
}
