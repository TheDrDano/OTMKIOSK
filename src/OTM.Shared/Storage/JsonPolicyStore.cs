using System.Text.Json;
using Otm.Kiosk.Shared.Models;

namespace Otm.Kiosk.Shared.Storage;

public sealed class JsonPolicyStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonPolicyStore()
    {
        Directory.CreateDirectory(KioskPaths.RootDirectory);
        Directory.CreateDirectory(KioskPaths.QuarantineDirectory);
    }

    public KioskPolicy LoadOrCreate()
    {
        if (!File.Exists(KioskPaths.PolicyPath))
        {
            var policy = PolicyPresets.Default();
            Save(policy);
            return policy;
        }

        var json = File.ReadAllText(KioskPaths.PolicyPath);
        return JsonSerializer.Deserialize<KioskPolicy>(json, Options) ?? PolicyPresets.Default();
    }

    public void Save(KioskPolicy policy)
    {
        policy.UpdatedAt = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(KioskPaths.RootDirectory);
        if (!string.IsNullOrWhiteSpace(policy.Admin.InitialRecoveryKey))
        {
            File.WriteAllText(Path.Combine(KioskPaths.RootDirectory, "first-run-recovery-key.txt"),
                $"OTM Kiosk recovery key{Environment.NewLine}{policy.Admin.InitialRecoveryKey}{Environment.NewLine}");
        }

        var json = JsonSerializer.Serialize(policy, Options);
        File.WriteAllText(KioskPaths.PolicyPath, json);
    }
}
