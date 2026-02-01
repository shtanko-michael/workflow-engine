using Microsoft.Extensions.Logging;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Graph;

/// <summary>
/// Workflow graph builder
/// </summary>
public class WorkflowGraph<TState> where TState : WorkflowStateBase
{
    private readonly Dictionary<string, WorkflowNode<TState>> _nodes = new();
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

        _nodes[name] = node;
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

        return new CompiledWorkflowGraph<TState>(_nodes, _edges, _nodeEnds, checkpointer, logger);
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
