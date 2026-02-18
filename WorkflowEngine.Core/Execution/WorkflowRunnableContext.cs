using Microsoft.Extensions.Logging;

namespace WorkflowEngine.Core.Execution;

/// <summary>
/// Context for workflow execution
/// </summary>
public class WorkflowRunnableContext
{
    public WorkflowController? Controller { get; set; }
    public IServiceProvider? Container { get; set; }

    /// <summary>
    /// Gateway for creating assistant messages and streaming. Always set by the controller (bridge or default).
    /// Nodes use this instead of touching config/bridge directly.
    /// </summary>
    public IWorkflowRunGateway Gateway { get; set; } = null!;
    public ClientTrackingContext Tracking { get; set; } = new();
    public ILogger Logger { get; set; } = null!;
}

/// <summary>
/// Tracking context for AI client requests
/// </summary>
public class ClientTrackingContext
{
    public string? Comment { get; set; }
    public string? UserId { get; set; }
    public string? AccountId { get; set; }
    public string? ThreadId { get; set; }
    public int? UserNumber { get; set; }
    public string? WorkspaceId { get; set; }
    public string? WorkspaceItemId { get; set; }
    public string? ChatMessageId { get; set; }
    public string? ArtifactId { get; set; }
    public string? WorkflowType { get; set; }
    public string? NodeName { get; set; }
    public string? IpAddress { get; set; }
    public ToolTrackingContext? Tool { get; set; }
}

/// <summary>
/// Tool tracking context
/// </summary>
public class ToolTrackingContext
{
    public string Name { get; set; } = string.Empty;
    public object? Input { get; set; }
}
