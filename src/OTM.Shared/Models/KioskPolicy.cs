using System.Text.Json.Serialization;
using Otm.Kiosk.Shared.Security;

namespace Otm.Kiosk.Shared.Models;

public sealed class KioskPolicy
{
    public string PolicyId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default Local Policy";
    public int Version { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public EnforcementPolicy Enforcement { get; set; } = new();
    public RestrictionPolicy Restrictions { get; set; } = new();
    public BrowserPolicy Browser { get; set; } = new();
    public RemoteManagementPolicy Remote { get; set; } = new();
    public UpdatePolicy Updates { get; set; } = new();
    public List<KioskLauncher> Launchers { get; set; } = [];
    public List<AppRule> AllowedApps { get; set; } = [];
    public List<AppRule> BlockedApps { get; set; } = [];
    public List<AppRule> RequiredApps { get; set; } = [];
    public AdminCredential Admin { get; set; } = AdminCredential.CreateDefault();
}

public sealed class EnforcementPolicy
{
    public bool Enabled { get; set; }
    public bool StrictApplicationWhitelist { get; set; }
    public bool RestartRequiredApps { get; set; } = true;
    public bool FullscreenOverlay { get; set; }
    public int ProcessScanIntervalMs { get; set; } = 1000;
    public int TemporaryUnlockMinutes { get; set; } = 15;
}

public sealed class AppRule
{
    public string DisplayName { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string? Path { get; set; }
    public bool Required { get; set; }
    public string? Arguments { get; set; }
}

public sealed class RestrictionPolicy
{
    public bool BlockDownloads { get; set; } = true;
    public bool DeleteBlockedDownloads { get; set; } = false;
    public bool QuarantineBlockedDownloads { get; set; } = true;
    public bool BlockInstallers { get; set; } = true;
    public bool BlockSystemTools { get; set; } = true;
    public bool BlockMicrosoftStore { get; set; } = true;
    public bool BlockUsbStorage { get; set; }
    public List<string> BlockedDownloadExtensions { get; set; } =
    [
        ".exe", ".msi", ".msix", ".appx", ".bat", ".cmd", ".ps1", ".vbs", ".js",
        ".jar", ".zip", ".7z", ".rar", ".iso", ".scr", ".reg"
    ];
}

public sealed class BrowserPolicy
{
    public bool Enabled { get; set; } = true;
    public bool WhitelistOnly { get; set; }
    public bool BlockDownloads { get; set; } = true;
    public List<string> AllowedSites { get; set; } = [];
    public List<string> BlockedSites { get; set; } =
    [
        "youtube.com", "youtu.be", "tiktok.com", "instagram.com", "facebook.com",
        "x.com", "twitter.com", "reddit.com", "discord.com"
    ];
}

public sealed class RemoteManagementPolicy
{
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string DeviceAlias { get; set; } = "";
    public bool AllowRemotePolicyPush { get; set; }
    public bool AllowRemoteUnlock { get; set; }
    public bool AllowRemoteUpdate { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
}

public sealed class UpdatePolicy
{
    public bool Enabled { get; set; }
    public string Channel { get; set; } = "stable";
    public string ManifestUrl { get; set; } = "";
    public bool AutoDownload { get; set; }
    public bool AutoInstall { get; set; }
    public int CheckIntervalHours { get; set; } = 24;
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string LastCheckMessage { get; set; } = "";
    public string LastAvailableVersion { get; set; } = "";
}

public sealed class AdminCredential
{
    public string PasswordHash { get; set; } = "";
    public string RecoveryKeyHash { get; set; } = "";
    public bool RequirePasswordChange { get; set; } = true;

    [JsonIgnore]
    public string? InitialRecoveryKey { get; set; }

    public static AdminCredential CreateDefault()
    {
        var recovery = RecoveryKeyGenerator.CreateRecoveryKey();
        return new AdminCredential
        {
            PasswordHash = PasswordHasher.Hash("123456"),
            RecoveryKeyHash = PasswordHasher.Hash(recovery),
            InitialRecoveryKey = recovery,
            RequirePasswordChange = true
        };
    }
}
