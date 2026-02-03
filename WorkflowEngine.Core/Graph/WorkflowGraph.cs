using Microsoft.Extensions.Logging;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Graph;

internal interface ISubgraphNodeFactory<TState> where TState : WorkflowStateBase
{
    WorkflowNode<TState> Create(string nodeName);
}

internal sealed class SubgraphNodeFactory<TParentState, TSubState> : ISubgraphNodeFactory<TParentState>
    where TParentState : WorkflowStateBase
    where TSubState : WorkflowStateBase
{
    private readonly CompiledWorkflowGraph<TSubState> _subgraph;
    private readonly Func<TParentState, TSubState> _map;
    private readonly Func<TParentState, TSubState, TParentState> _merge;

    public SubgraphNodeFactory(
        CompiledWorkflowGraph<TSubState> subgraph,
        Func<TParentState, TSubState> mapParentToSubgraph,
        Func<TParentState, TSubState, TParentState> mergeSubgraphIntoParent)
    {
        _subgraph = subgraph;
        _map = mapParentToSubgraph;
        _merge = mergeSubgraphIntoParent;
    }

    public WorkflowNode<TParentState> Create(string nodeName) =>
        SubgraphAsNode.CreateWithMapping(_subgraph, nodeName, _map, _merge);
}

/// <summary>
/// Workflow graph builder
/// </summary>
public class WorkflowGraph<TState> where TState : WorkflowStateBase
{
    private readonly Dictionary<string, WorkflowNode<TState>> _nodes = new();
    private readonly Dictionary<string, CompiledWorkflowGraph<TState>> _subgraphNodes = new();
    private readonly Dictionary<string, ISubgraphNodeFactory<TState>> _subgraphNodesWithMapping = new();
    private readonly List<WorkflowEdge> _edges = new();
    private readonly Dictionary<string, List<string>> _nodeEnds = new();

    /// <summary>
    /// Adds a node to the graph
    /// </summary>
    public WorkflowGraph<TState> AddNode(
        string name,
        WorkflowNode<TState> node,
        List<string>? ends = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Node name cannot be null or empty", nameof(name));
        if (node == null)
            throw new ArgumentNullException(nameof(node));
        if (_subgraphNodes.ContainsKey(name))
            throw new ArgumentException($"A subgraph node with name '{name}' already exists.", nameof(name));
        if (_subgraphNodesWithMapping.ContainsKey(name))
            throw new ArgumentException($"A subgraph node with mapping with name '{name}' already exists.", nameof(name));

        _nodes[name] = node;
        if (ends != null && ends.Count > 0)
            _nodeEnds[name] = ends;
        return this;
    }

    /// <summary>
    /// Adds a compiled subgraph as a node. The subgraph runs in a child checkpoint namespace (parentNs + ":" + nodeName).
    /// On subgraph interrupt (human-in-the-loop), the parent saves current node as this node and returns; on resume, the subgraph continues.
    /// </summary>
    public WorkflowGraph<TState> AddNode(
        string name,
        CompiledWorkflowGraph<TState> subgraph,
        List<string>? ends = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Node name cannot be null or empty", nameof(name));
        if (subgraph == null)
            throw new ArgumentNullException(nameof(subgraph));
        if (_nodes.ContainsKey(name))
            throw new ArgumentException($"A node with name '{name}' already exists.", nameof(name));
        if (_subgraphNodesWithMapping.ContainsKey(name))
            throw new ArgumentException($"A subgraph node with mapping with name '{name}' already exists.", nameof(name));

        _subgraphNodes[name] = subgraph;
        if (ends != null && ends.Count > 0)
            _nodeEnds[name] = ends;
        return this;
    }

    /// <summary>
    /// Adds a compiled subgraph as a node with a different state type; uses map/merge to convert between parent and subgraph state.
    /// </summary>
    public WorkflowGraph<TState> AddNode<TSubState>(
        string name,
        CompiledWorkflowGraph<TSubState> subgraph,
        Func<TState, TSubState> mapParentToSubgraph,
        Func<TState, TSubState, TState> mergeSubgraphIntoParent,
        List<string>? ends = null)
        where TSubState : WorkflowStateBase
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Node name cannot be null or empty", nameof(name));
        if (subgraph == null)
            throw new ArgumentNullException(nameof(subgraph));
        if (mapParentToSubgraph == null)
            throw new ArgumentNullException(nameof(mapParentToSubgraph));
        if (mergeSubgraphIntoParent == null)
            throw new ArgumentNullException(nameof(mergeSubgraphIntoParent));
        if (_nodes.ContainsKey(name))
            throw new ArgumentException($"A node with name '{name}' already exists.", nameof(name));
        if (_subgraphNodes.ContainsKey(name))
            throw new ArgumentException($"A subgraph node with name '{name}' already exists.", nameof(name));

        _subgraphNodesWithMapping[name] = new SubgraphNodeFactory<TState, TSubState>(
            subgraph, mapParentToSubgraph, mergeSubgraphIntoParent);
        if (ends != null && ends.Count > 0)
            _nodeEnds[name] = ends;
        return this;
    }

    /// <summary>
    /// Adds an edge to the graph
    /// </summary>
    public WorkflowGraph<TState> AddEdge(string from, string to)
    {
        if (string.IsNullOrWhiteSpace(from))
            throw new ArgumentException("From node cannot be null or empty", nameof(from));
        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("To node cannot be null or empty", nameof(to));

        _edges.Add(new WorkflowEdge { From = from, To = to });
        return this;
    }

    /// <summary>
    /// Adds an edge to the graph
    /// </summary>
    public WorkflowGraph<TState> AddEdge(string from, Func<object, string>? condition)
    {
        if (string.IsNullOrWhiteSpace(from))
            throw new ArgumentException("From node cannot be null or empty", nameof(from));
        if (condition == null)
            throw new ArgumentException("Condition cannot be null", nameof(condition));

        _edges.Add(new WorkflowEdge { From = from, Condition = condition });
        return this;
    }

    /// <summary>
    /// Compiles the graph with a checkpointer
    /// </summary>
    public CompiledWorkflowGraph<TState> Compile(ICheckpointSaver checkpointer, ILogger? logger = null)
    {
        if (checkpointer == null)
            throw new ArgumentNullException(nameof(checkpointer));

        var nodes = new Dictionary<string, WorkflowNode<TState>>(_nodes);
        foreach (var kv in _subgraphNodes)
            nodes[kv.Key] = SubgraphAsNode.Create(kv.Value, kv.Key);
        foreach (var kv in _subgraphNodesWithMapping)
            nodes[kv.Key] = kv.Value.Create(kv.Key);

        return new CompiledWorkflowGraph<TState>(nodes, _edges, _nodeEnds, checkpointer, logger);
    }

    /// <summary>
    /// Gets all nodes
    /// </summary>
    internal IReadOnlyDictionary<string, WorkflowNode<TState>> Nodes => _nodes;

    /// <summary>
    /// Gets all edges
    /// </summary>
    internal IReadOnlyList<WorkflowEdge> Edges => _edges;

    /// <summary>
    /// Gets node ends configuration
    /// </summary>
    internal IReadOnlyDictionary<string, List<string>> NodeEnds => _nodeEnds;
}

public class WorkflowGlobals
{
    /// <summary>
    /// Key in config.Configurable used to pass the current workflow command to nodes (e.g. for resume).
    /// Set by CompiledWorkflowGraph at the start of InvokeAsync.
    /// </summary>
    public const string WorkflowCommandKey = "__workflow_command__";

}