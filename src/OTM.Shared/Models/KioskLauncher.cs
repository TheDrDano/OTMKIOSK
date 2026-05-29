namespace Otm.Kiosk.Shared.Models;

public sealed class KioskLauncher
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "";
    public string Type { get; set; } = KioskLauncherTypes.App;
    public string WorkspaceMode { get; set; } = KioskWorkspaceModes.Lab;
    public string? Url { get; set; }
    public string ProcessName { get; set; } = "";
    public string? Path { get; set; }
    public string? Arguments { get; set; }
    public bool Required { get; set; }
    public bool AllowMultiMonitorOwnership { get; set; }
    public List<string> AllowedSites { get; set; } = [];
}

public static class KioskLauncherTypes
{
    public const string Web = "web";
    public const string App = "app";
}

public static class KioskWorkspaceModes
{
    public const string Exam = "exam";
    public const string Lab = "lab";
    public const string AppOwner = "appOwner";
    public const string DedicatedKiosk = "dedicatedKiosk";
}
