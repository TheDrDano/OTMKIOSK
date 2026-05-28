using Otm.Kiosk.Shared.Models;

namespace Otm.Kiosk.Shared.Storage;

public static class PolicyPresets
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

    public static KioskPolicy FlightSimulator()
    {
        var policy = Default();
        policy.Name = "Flight Simulator Lockdown";
        policy.Enforcement.Enabled = true;
        policy.Enforcement.StrictApplicationWhitelist = true;
        policy.Browser.WhitelistOnly = true;
        policy.AllowedApps =
        [
            new AppRule { DisplayName = "Microsoft Flight Simulator", ProcessName = "FlightSimulator.exe" },
            new AppRule { DisplayName = "Microsoft Flight Simulator Launcher", ProcessName = "FlightSimulator2024.exe" },
            new AppRule { DisplayName = "Steam", ProcessName = "steam.exe" },
            new AppRule { DisplayName = "Xbox App", ProcessName = "XboxPcApp.exe" },
            new AppRule { DisplayName = "Joystick/HOTAS Utility", ProcessName = "joy.cpl" }
        ];
        policy.RequiredApps =
        [
            new AppRule { DisplayName = "Microsoft Flight Simulator", ProcessName = "FlightSimulator.exe", Required = true }
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
