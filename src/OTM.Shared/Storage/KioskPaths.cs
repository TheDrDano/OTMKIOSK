namespace Otm.Kiosk.Shared.Storage;

public static class KioskPaths
{
    public static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OTM Kiosk");

    public static string PolicyPath => Path.Combine(RootDirectory, "policy.json");
    public static string DatabasePath => Path.Combine(RootDirectory, "otm-kiosk.db");
    public static string RuntimePath => Path.Combine(RootDirectory, "runtime.json");
    public static string DeviceIdentityPath => Path.Combine(RootDirectory, "device-identity.json");
    public static string QuarantineDirectory => Path.Combine(RootDirectory, "Quarantine");
    public static string WebView2UserDataDirectory => Path.Combine(RootDirectory, "WebView2");
}
