namespace Otm.Kiosk.Shared.Models;

public sealed class RuntimeState
{
    public bool ServiceRunning { get; set; }
    public bool EnforcementEnabled { get; set; }
    public bool TemporaryUnlockActive { get; set; }
    public DateTimeOffset? TemporaryUnlockUntil { get; set; }
    public bool MaintenanceHoldActive { get; set; }
    public DateTimeOffset? MaintenanceHoldUntil { get; set; }
    public string MaintenanceHoldReason { get; set; } = "";
    public string PolicyName { get; set; } = "";
    public DateTimeOffset CurrentTime { get; set; } = DateTimeOffset.UtcNow;
}
