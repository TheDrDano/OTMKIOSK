using System.ServiceProcess;

namespace Otm.Kiosk.Service;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Contains("--console", StringComparer.OrdinalIgnoreCase))
        {
            using var runtime = new KioskRuntime();
            await runtime.StartAsync();
            Console.WriteLine("OTM Kiosk service runtime is running. Press Ctrl+C to stop.");

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
