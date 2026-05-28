namespace Otm.Kiosk.Shared.Storage;

public static class KioskPaths
{
    public static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OTM Kiosk");

    public static string PolicyPath => Path.Combine(RootDirectory, "policy.json");
    public static string LogPath => Path.Combine(RootDirectory, "events.jsonl");
    public static string RuntimePath => Path.Combine(RootDirectory, "runtime.json");
    public static string QuarantineDirectory => Path.Combine(RootDirectory, "Quarantine");
}
