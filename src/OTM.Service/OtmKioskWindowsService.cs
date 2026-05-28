using System.ServiceProcess;

namespace Otm.Kiosk.Service;

public sealed class OtmKioskWindowsService : ServiceBase
{
    private KioskRuntime? _runtime;

    public OtmKioskWindowsService()
    {
        ServiceName = "OTMKioskService";
        CanStop = true;
        CanPauseAndContinue = false;
        AutoLog = true;
    }

    protected override void OnStart(string[] args)
    {
        _runtime = new KioskRuntime();
        _runtime.StartAsync().GetAwaiter().GetResult();
    }

    protected override void OnStop()
    {
        if (_runtime is null)
        {
            return;
        }

        _runtime.StopAsync().GetAwaiter().GetResult();
        _runtime.Dispose();
        _runtime = null;
    }
}
