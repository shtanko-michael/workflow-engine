using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Execution;

/// <summary>
/// Controller for executing workflows
/// </summary>
public class WorkflowController
{
    private readonly WorkflowRegistry _registry;
    private readonly ILogger<WorkflowController> _logger;
    private readonly IServiceProvider _serviceProvider;

    public WorkflowController(
        WorkflowRegistry registry,
        ILogger<WorkflowController> logger,
        IServiceProvider serviceProvider)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// Executes a workflow
    /// </summary>
    public async Task<TState> ExecuteAsync<TState>(
        WorkflowControllerExecuteConfig config) where TState : WorkflowStateBase
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var workflowItem = _registry.Get(config.WorkflowType);
        if (workflowItem == null)
            throw new InvalidOperationException($"Workflow '{config.WorkflowType}' not found");

        var checkpointer = await BuildCheckpointerAsync();

        var graph = workflowItem.Compile<TState>(checkpointer, _logger);

        var runnableConfig = BuildRunnableConfig(config);
        var command = BuildInitialCommand<TState>(config);

        _logger.LogInformation("Executing workflow: {WorkflowType}, ThreadId: {ThreadId}",
            config.WorkflowType, runnableConfig.Configurable.TryGetValue("thread_id", out var tid) ? tid : "unknown");

        var result = await graph.InvokeAsync(command, runnableConfig);

        _logger.LogInformation("Workflow execution completed: {WorkflowType}", config.WorkflowType);

        return result;
    }

    private Task<ICheckpointSaverFactory> BuildCheckpointerAsync()
    {
        var checkpointer = _serviceProvider.GetService<ICheckpointSaverFactory>();
        if (checkpointer != null)
            return Task.FromResult(checkpointer);

        throw new ArgumentException("Checkpointer is not available. Register it via DI.");
    }

    private WorkflowRunnableConfig BuildRunnableConfig(WorkflowControllerExecuteConfig config)
    {
        var gateway = config.Gateway ?? new DefaultWorkflowRunGateway(config.StreamChunkCallback);

        var runnableConfig = new WorkflowRunnableConfig
        {
            Configurable = [],
            Context = new WorkflowRunnableContext
            {
                Controller = this,
                Container = _serviceProvider,
                Gateway = gateway,
                Tracking = new ClientTrackingContext
                {
                    UserId = config.UserId ?? string.Empty,
                    ThreadId = config.ThreadId,
                    WorkspaceId = config.WorkspaceId ?? string.Empty,
                    WorkflowType = config.WorkflowType
                },
                Logger = _logger
            }
        };

        runnableConfig.ThreadId = config.ThreadId;
        if (!string.IsNullOrWhiteSpace(config.CheckpointId))
            runnableConfig.CheckpointId = config.CheckpointId;
        runnableConfig.CheckpointNs = !string.IsNullOrWhiteSpace(config.CheckpointNs) ? config.CheckpointNs : Guid.NewGuid().ToString();

        return runnableConfig;
    }

    private WorkflowCommand<TState> BuildInitialCommand<TState>(WorkflowControllerExecuteConfig config) where TState : WorkflowStateBase
    {
        var update = config.InitialState as TState;
        var resume = config.ResumeMessage;

        return WorkflowCommand<TState>.Create(
            update: update,
            resume: resume
        );
    }
}

/// <summary>
/// Configuration for workflow execution
/// </summary>
public class WorkflowControllerExecuteConfig
{
    public string WorkflowType { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? WorkspaceId { get; set; }
    public object? InitialState { get; set; }
    public HumanMessage? ResumeMessage { get; set; }

    // Additional checkpoint and streaming parameters
    public string? CheckpointId { get; set; }
    public string? CheckpointNs { get; set; }

    /// <summary>
    /// Optional gateway (e.g. bridge to external system). When set, nodes create messages and stream through it; otherwise default in-memory + legacy StreamChunkCallback is used.
    /// </summary>
    public IWorkflowRunGateway? Gateway { get; set; }

    /// <summary>
    /// Legacy: callback invoked per chunk when no Gateway is set. Ignored when Gateway is provided.
    /// </summary>
    public Func<string, Task>? StreamChunkCallback { get; set; }
}
