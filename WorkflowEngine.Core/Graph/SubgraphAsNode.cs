using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Exceptions;
using WorkflowEngine.Core.Execution;
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

        return async (state, _, _, config) =>
        {
            var childConfig = BuildChildConfig(config, nodeName);
            var parentCommand = config.Configurable.TryGetValue(WorkflowConfigKeys.WorkflowCommandKey, out var cmdObj)
                ? cmdObj as WorkflowCommand<TState>
                : null;
            var commandToChild = WorkflowCommand<TState>.Create(
                update: state,
                resume: parentCommand?.Resume);

            var childState = await subgraph.InvokeAsync(commandToChild, childConfig);

            if (childState.WorkflowCompleted)
            {
                // prevent merging the subgraph complete flag into the parent state
                // childState.WorkflowCompleted = false;
                return WorkflowCommand<TState>.Create(update: childState);
            }

            if (childState.InterruptReason == WorkflowInterruptReason.AskHuman && !string.IsNullOrEmpty(state.InterruptCaller))
                throw new SubgraphWorkflowInterruptException(childState.InterruptCaller, nodeName);

            return WorkflowCommand<TState>.Create(update: childState);
            // return SubGraphWorkflowCommand<TSubState, TParentState>.Create(childState);
        };
    }

    private static WorkflowRunnableConfig BuildChildConfig(WorkflowRunnableConfig config, string nodeName)
    {
        var parentNs = config.Configurable.TryGetValue("checkpoint_ns", out var ns)
            ? ns?.ToString() ?? string.Empty
            : string.Empty;
        var childNs = string.IsNullOrEmpty(parentNs)
            ? nodeName
            : parentNs + ":" + nodeName;

        var configurable = new Dictionary<string, object>(config.Configurable)
        {
            ["checkpoint_ns"] = config.SubgraphCheckpointNs ?? childNs,
            // Parent checkpoint_id does not apply to child namespaces.
            ["checkpoint_id"] = config.SubgraphCheckpointId,
        };
        return new WorkflowRunnableConfig
        {
            Configurable = configurable,
            Context = config.Context
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

            var childState = await subgraph.InvokeAsync(commandToChild, childConfig);
            if (childState.WorkflowCompleted)
            {
                parentConfig.SubgraphCheckpointId = null;
                parentConfig.SubgraphCheckpointNs = null;
                var merged = completeStateMapping(parentState, childState);
                // return WorkflowCommand<TParentState>.Create(update: merged);
                return SubGraphWorkflowCommand<TSubState, TParentState>.Create(childState, update: merged);
            }

            parentConfig.SubgraphCheckpointId = childState.LastCheckpointId;
            parentConfig.SubgraphCheckpointNs = childConfig.CheckpointNs;

            if (!string.IsNullOrEmpty(childState.InterruptCaller))
                throw new SubgraphWorkflowInterruptException(childState.InterruptCaller, nodeName);

            return SubGraphWorkflowCommand<TSubState, TParentState>.Create(childState, update: null);
        };
    }
}
