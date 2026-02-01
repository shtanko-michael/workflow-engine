namespace WorkflowEngine.Tests.UI.Backend.Models;

public class Dialog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ThreadId { get; set; } = string.Empty;
    public string WorkflowType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LastCheckpointId { get; set; }
    public string? LastInterruptRequestId { get; set; }
}
