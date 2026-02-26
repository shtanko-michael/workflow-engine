using Microsoft.Extensions.Logging;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Execution;

/// <summary>
/// Context for workflow execution
/// </summary>
public class WorkflowRunnableContext
{
    public WorkflowController? Controller { get; set; }
    public IServiceProvider? Container { get; set; }

    /// <summary>
    /// Optional interceptor for workflow execution events. When set, the engine notifies it at graph/subgraph/node lifecycle and completion.
    /// </summary>
    public IWorkflowRunInterceptor? Interceptor { get; set; }
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
