namespace Otm.Kiosk.Shared.Models;

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Level { get; set; } = "Info";
    public string EventType { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ProcessName { get; set; }
    public string? Path { get; set; }
    public string? UserName { get; set; }
}
