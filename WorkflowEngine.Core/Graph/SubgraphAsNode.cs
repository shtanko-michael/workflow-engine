using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Exceptions;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Extensions;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Graph;

/// <summary>
/// Wraps a compiled subgraph so it can be used as a node in a parent graph.
/// Subgraph runs in a child checkpoint namespace (parentNs + ":" + nodeName).
/// When the subgraph is interrupted (human-in-the-loop), propagates the interrupt to the parent.
/// </summary>
public static class SubgraphAsNode
{
    /// <summary>
    /// Creates a node delegate that runs the given subgraph with a child checkpoint namespace.
    /// </summary>
    /// <param name="subgraph">Compiled subgraph to run when this node is executed.</param>
    /// <param name="nodeName">Name of this node (used for child namespace and interrupt return).</param>
    /// <returns>A WorkflowNode that invokes the subgraph and handles completion/interrupt.</returns>
    public static WorkflowNode<TState> Create<TState>(
        CompiledWorkflowGraph<TState> subgraph,
        string nodeName) where TState : WorkflowStateBase
    {
        ArgumentNullException.ThrowIfNull(subgraph);
        if (string.IsNullOrWhiteSpace(nodeName))
            throw new ArgumentException("Node name is required for subgraph namespace.", nameof(nodeName));
        if (nodeName.Contains(':'))
            throw new ArgumentException("Node name cannot contain ':' (reserved for checkpoint namespace).", nameof(nodeName));

        return async (state, _, _, parentConfig) =>
        {
            var childConfig = BuildChildConfig(parentConfig, nodeName);
            var parentCommand = parentConfig.Configurable.TryGetValue(WorkflowConfigKeys.WorkflowCommandKey, out var cmdObj)
                ? cmdObj as WorkflowCommand<TState>
                : null;
            var commandToChild = WorkflowCommand<TState>.Create(
                update: state,
                resume: parentCommand?.Resume);
            // remove parent's command
            parentConfig.Configurable.Remove(WorkflowConfigKeys.WorkflowCommandKey);

            await SafeNotifySubgraphAsync(parentConfig, state, (i, c, s) => i.OnSubgraphStartedAsync(nodeName, c, s));

            var childState = await subgraph.InvokeAsync(commandToChild, childConfig);

            await SafeNotifySubgraphAsync(parentConfig, state, (i, c, s) => i.OnSubgraphCompletedAsync(nodeName, c, s));

            if (childState.WorkflowCompleted)
            {
                parentConfig.SubgraphCheckpointId = null;
                parentConfig.SubgraphCheckpointNs = null;
                return WorkflowCommand<TState>.Create(update: null);
            }

            if (childState.InterruptReason == WorkflowInterruptReason.AskHuman && !string.IsNullOrEmpty(childState.InterruptCaller))
            {
                // store subgrapg data in parent config
                parentConfig.SubgraphCheckpointId = childState.LastCheckpointId;
                parentConfig.SubgraphCheckpointNs = childConfig.CheckpointNs;
                throw new SubgraphWorkflowInterruptException(childState.InterruptCaller, nodeName);
            }

            return SubGraphWorkflowCommand<TState, TState>.Create(update: null);
        };
    }

    /// <summary>
    /// Creates a node delegate that runs a subgraph with a different state type, mapping parent state to subgraph state and merging back on completion.
    /// </summary>
    /// <param name="subgraph">Compiled subgraph (with its own state type) to run when this node is executed.</param>
    /// <param name="nodeName">Name of this node (used for child namespace and interrupt return).</param>
    /// <param name="initialStateMapping">Maps parent state to subgraph input state.</param>
    /// <param name="completeStateMapping">Merges subgraph output state back into parent state.</param>
    /// <returns>A WorkflowNode that invokes the subgraph and handles completion/interrupt.</returns>
    public static WorkflowNode<TParentState> CreateWithMapping<TParentState, TSubState>(
        CompiledWorkflowGraph<TSubState> subgraph,
        string nodeName,
        Func<TParentState, TSubState> initialStateMapping,
        Func<TParentState, TSubState, TParentState> completeStateMapping)
        where TParentState : WorkflowStateBase
        where TSubState : WorkflowStateBase
    {
        ArgumentNullException.ThrowIfNull(subgraph);
        if (string.IsNullOrWhiteSpace(nodeName))
            throw new ArgumentException("Node name is required for subgraph namespace.", nameof(nodeName));
        if (nodeName.Contains(':'))
            throw new ArgumentException("Node name cannot contain ':' (reserved for checkpoint namespace).", nameof(nodeName));
        ArgumentNullException.ThrowIfNull(initialStateMapping);
        ArgumentNullException.ThrowIfNull(completeStateMapping);

        return async (parentState, _, _, parentConfig) =>
        {
            var childConfig = BuildChildConfig(parentConfig, nodeName);
            var parentCommand = parentConfig.Configurable.TryGetValue(WorkflowConfigKeys.WorkflowCommandKey, out var cmdObj)
                ? cmdObj as WorkflowCommand<TParentState>
                : null;
            var commandToChild = WorkflowCommand<TSubState>.Create(
                update: string.IsNullOrEmpty(childConfig.CheckpointId) ? initialStateMapping(parentState) : null,
                resume: parentCommand?.Resume);
            // remove parent's command
            parentConfig.Configurable.Remove(WorkflowConfigKeys.WorkflowCommandKey);

            await SafeNotifySubgraphAsync(parentConfig, parentState, (i, c, s) => i.OnSubgraphStartedAsync(nodeName, c, s));

            var childState = await subgraph.InvokeAsync(commandToChild, childConfig);

            await SafeNotifySubgraphAsync(parentConfig, parentState, (i, c, s) => i.OnSubgraphCompletedAsync(nodeName, c, s));

            if (childState.WorkflowCompleted)
            {
                parentConfig.SubgraphCheckpointId = null;
                parentConfig.SubgraphCheckpointNs = null;
                var merged = completeStateMapping(parentState, childState);
                return SubGraphWorkflowCommand<TSubState, TParentState>.Create(childState, update: merged);
            }

            if (childState.InterruptReason == WorkflowInterruptReason.AskHuman && !string.IsNullOrEmpty(childState.InterruptCaller))
            {
                // store subgrapg data in parent config
                parentConfig.SubgraphCheckpointId = childState.LastCheckpointId;
                parentConfig.SubgraphCheckpointNs = childConfig.CheckpointNs;
                throw new SubgraphWorkflowInterruptException(childState.InterruptCaller, nodeName);
            }

            return SubGraphWorkflowCommand<TSubState, TParentState>.Create(childState, update: null);
        };
    }

    private static WorkflowRunnableConfig BuildChildConfig(WorkflowRunnableConfig parentConfig, string nodeName)
    {
        var parentNs = parentConfig.Configurable.TryGetValue(WorkflowConfigKeys.CheckpointNs, out var ns)
            ? ns?.ToString() ?? string.Empty
            : string.Empty;
        var childNs = parentConfig.SubgraphCheckpointNs ?? string.Empty;
        if (string.IsNullOrEmpty(childNs))
            childNs = parentNs + ":" + nodeName + "-" + Guid.NewGuid().ToShortGuid();

        var configurable = new Dictionary<string, object>(parentConfig.Configurable)
        {
            [WorkflowConfigKeys.CheckpointNs] = childNs,
            // Parent checkpoint_id does not apply to child namespaces.
            [WorkflowConfigKeys.CheckpointId] = parentConfig.SubgraphCheckpointId,
        };
        configurable.Remove(WorkflowConfigKeys.ParentCheckpointId);
        return new WorkflowRunnableConfig
        {
            Configurable = configurable,
            Context = parentConfig.Context
        };
    }

    private static async Task SafeNotifySubgraphAsync<T>(WorkflowRunnableConfig config, T state,
        Func<IWorkflowRunInterceptor, WorkflowRunnableConfig, T, Task> invoke)
        where T : WorkflowStateBase
    {
        if (config.Context?.Interceptor is not IWorkflowRunInterceptor interceptor)
            return;
        try
        {
            await invoke(interceptor, config, state);
        }
        catch
        {
            // Do not let interceptor break the engine; swallow exceptions
        }
    }
}
