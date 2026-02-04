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
        var runnableConfig = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>
            {
                ["thread_id"] = config.ThreadId
            },
            Context = new WorkflowRunnableContext
            {
                Controller = this,
                Container = _serviceProvider,
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
        
        // Add optional checkpoint parameters
        if (!string.IsNullOrWhiteSpace(config.CheckpointId))
            runnableConfig.Configurable["checkpoint_id"] = config.CheckpointId;
        if (!string.IsNullOrWhiteSpace(config.CheckpointNs))
            runnableConfig.Configurable["checkpoint_ns"] = config.CheckpointNs;
        if (config.StreamChunkCallback != null)
            runnableConfig.Configurable["stream_chunk_callback"] = config.StreamChunkCallback;
            
        return runnableConfig;
    }
    
    private WorkflowCommand<TState> BuildInitialCommand<TState>(WorkflowControllerExecuteConfig config) where TState : class
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
    public Func<string, Task>? StreamChunkCallback { get; set; }
}
