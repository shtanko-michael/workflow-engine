using WorkflowEngine.Core.Graph;

namespace WorkflowEngine.Core.Execution;

/// <summary>
/// Configuration for workflow execution
/// </summary>
public class WorkflowRunnableConfig
{
    public Dictionary<string, object> Configurable { get; set; } = new();
    public WorkflowRunnableContext? Context { get; set; }

    public string? ThreadId
    {
        get => Configurable.TryGetValue(WorkflowConfigKeys.ThreadId, out object? value) ? value?.ToString() : null;
        set => Configurable[WorkflowConfigKeys.ThreadId] = value;
    }

    public string? CheckpointId
    {
        get => Configurable.TryGetValue(WorkflowConfigKeys.CheckpointId, out object? value) ? value?.ToString() : null;
        set => Configurable[WorkflowConfigKeys.CheckpointId] = value;
    }

    public string? CheckpointNs
    {
        get => Configurable.TryGetValue(WorkflowConfigKeys.CheckpointNs, out object? value) ? value?.ToString() : null;
        set => Configurable[WorkflowConfigKeys.CheckpointNs] = value;
    }

    /// <summary>
    /// Id of the previous checkpoint (parent). Set by the graph before saving so persistence can link the new checkpoint correctly.
    /// </summary>
    public string? ParentCheckpointId
    {
        get => Configurable.TryGetValue(WorkflowConfigKeys.ParentCheckpointId, out object? value) ? value?.ToString() : null;
        set => Configurable[WorkflowConfigKeys.ParentCheckpointId] = value;
    }

    public string? LastMessageId
    {
        get => Configurable.TryGetValue(WorkflowConfigKeys.LastMessageId, out object? value) ? value?.ToString() : null;
        set => Configurable[WorkflowConfigKeys.LastMessageId] = value;
    }

    public string? SubgraphCheckpointId
    {
        get => Configurable.TryGetValue(WorkflowConfigKeys.SubgraphCheckpointId, out object? value) ? value?.ToString() : null;
        set => Configurable[WorkflowConfigKeys.SubgraphCheckpointId] = value;
    }

    public string? SubgraphCheckpointNs
    {
        get => Configurable.TryGetValue(WorkflowConfigKeys.SubgraphCheckpointNs, out object? value) ? value?.ToString() : null;
        set => Configurable[WorkflowConfigKeys.SubgraphCheckpointNs] = value;
    }
}
