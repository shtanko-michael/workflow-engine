using WorkflowEngine.Core.Execution;

namespace WorkflowEngine.Core.Persistence;

/// <summary>
/// Interface for checkpoint persistence
/// </summary>
public interface ICheckpointSaver
{
    /// <summary>
    /// Gets a checkpoint by config
    /// </summary>
    Task<CheckpointTuple?> GetAsync(WorkflowRunnableConfig config);

    /// <summary>
    /// Saves a checkpoint
    /// </summary>
    Task<WorkflowRunnableConfig> PutAsync(
        WorkflowRunnableConfig config,
        Checkpoint checkpoint,
        object metadata,
        Dictionary<string, string> newVersions);

    /// <summary>
    /// Lists checkpoints
    /// </summary>
    IAsyncEnumerable<CheckpointTuple> ListAsync(
        WorkflowRunnableConfig config,
        CheckpointListOptions? options = null);

    /// <summary>
    /// Sets up the checkpointer (e.g., creates database tables)
    /// </summary>
    Task SetupAsync();
}

/// <summary>
/// Checkpoint tuple returned by checkpointer
/// </summary>
public class CheckpointTuple
{
    public WorkflowRunnableConfig Config { get; set; } = null!;
    public Checkpoint Checkpoint { get; set; } = null!;
    public object Metadata { get; set; } = null!;
    public WorkflowRunnableConfig? ParentConfig { get; set; }
}

/// <summary>
/// Checkpoint data
/// </summary>
public class Checkpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public Dictionary<string, string> ChannelVersions { get; set; } = new();
    public Dictionary<string, object> ChannelValues { get; set; } = new();
}

/// <summary>
/// Options for listing checkpoints
/// </summary>
public class CheckpointListOptions
{
    public Dictionary<string, object>? Filter { get; set; }
    public WorkflowRunnableConfig? Before { get; set; }
    public int? Limit { get; set; }
}
