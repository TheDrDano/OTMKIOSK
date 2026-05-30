using System.ServiceProcess;

namespace Otm.Kiosk.Service;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Contains("--configure-startup-updates", StringComparer.OrdinalIgnoreCase))
        {
            var enabled = args.Contains("true", StringComparer.OrdinalIgnoreCase)
                || args.Contains("--enable", StringComparer.OrdinalIgnoreCase);
            using var runtime = new KioskRuntime();
            var policy = runtime.GetPolicy();
            policy.Updates ??= new Otm.Kiosk.Shared.Models.UpdatePolicy();
            if (enabled)
            {
                policy.Updates.Enabled = true;
            }
            policy.Updates.CheckOnStartup = enabled;
            policy.Updates.AutoDownload = enabled;
            policy.Updates.AutoInstall = false;
            policy.Updates.HoldEnforcementDuringStartupUpdate = true;
            runtime.SavePolicy(policy, enabled
                ? "GitHub startup update checks enabled from installer."
                : "GitHub startup update checks disabled from installer.");
            Console.WriteLine(enabled
                ? "SimpleKioskOS startup update checks are enabled."
                : "SimpleKioskOS startup update checks are disabled.");
            return;
        }

        if (args.Contains("--console", StringComparer.OrdinalIgnoreCase))
        {
            using var runtime = new KioskRuntime();
            await runtime.StartAsync();
            Console.WriteLine("SimpleKioskOS service runtime is running. Press Ctrl+C to stop.");

            var stop = new TaskCompletionSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                stop.TrySetResult();
            };

            await stop.Task;
            await runtime.StopAsync();
            return;
        }

        ServiceBase.Run(new OtmKioskWindowsService());
    }
}
