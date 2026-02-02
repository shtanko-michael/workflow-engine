using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Exceptions;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Graph;

/// <summary>
/// Compiled workflow graph ready for execution
/// </summary>
public class CompiledWorkflowGraph<TState> where TState : WorkflowStateBase
{
    private const string StateChannel = "state";
    private const string CurrentNodeChannel = "current_node";
    private static readonly string DebugLogPath = Path.Combine(AppContext.BaseDirectory, "workflow-debug.log");
    private static long _versionCounter = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly Dictionary<string, WorkflowNode<TState>> _nodes;
    private readonly List<WorkflowEdge> _edges;
    private readonly Dictionary<string, List<string>> _nodeEnds;
    private readonly ICheckpointSaver _checkpointer;
    private readonly ILogger? _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public CompiledWorkflowGraph(
        Dictionary<string, WorkflowNode<TState>> nodes,
        List<WorkflowEdge> edges,
        Dictionary<string, List<string>> nodeEnds,
        ICheckpointSaver checkpointer,
        ILogger? logger = null)
    {
        _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        _edges = edges ?? throw new ArgumentNullException(nameof(edges));
        _nodeEnds = nodeEnds ?? throw new ArgumentNullException(nameof(nodeEnds));
        _checkpointer = checkpointer ?? throw new ArgumentNullException(nameof(checkpointer));
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            PropertyNameCaseInsensitive = true,
        };
    }

    /// <summary>
    /// Invokes the workflow graph
    /// </summary>
    public async Task<TState> InvokeAsync(
        WorkflowCommand<TState> command,
        WorkflowRunnableConfig config)
    {
        if (command == null)
            throw new ArgumentNullException(nameof(command));
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        if (config.Context == null)
            throw new ArgumentException("Context is required", nameof(config));


        var (state, resumeNode) = await GetOrCreateStateAsync(config, command);
        if (state.WorkflowCompleted)
            return state;

        var currentNode = resumeNode ?? GetStartNode();
        var errorHandler = CreateErrorHandler(config);
        #region agent log
        DebugLog(
            location: "CompiledWorkflowGraph.InvokeAsync",
            message: "Invoke starting",
            data: new { resumeNode, currentNode },
            hypothesisId: "A");
        #endregion

        try
        {
            while (currentNode != null && currentNode != WorkflowEdges.End)
            {
                _logger?.LogDebug("Executing node: {NodeName}", currentNode);

                if (!_nodes.TryGetValue(currentNode, out var node))
                {
                    throw new InvalidOperationException($"Node '{currentNode}' not found");
                }

                // Execute node
                var nodeCommand = await node(state, config.Context, errorHandler, config);

                // Apply state update
                if (nodeCommand.Update != null)
                {
                    state = MergeState(state, nodeCommand.Update);
                }

                // Determine next node
                var nextNode = DetermineNextNode(currentNode, nodeCommand, state);
                if (nextNode == WorkflowEdges.End && state is WorkflowStateBase baseState)
                {
                    baseState.WorkflowCompleted = true;
                }

                // double-check interrupt caller is set
                if (nextNode == WorkflowEdges.AskHuman
                    && state is WorkflowStateBase interruptState
                    && string.IsNullOrEmpty(interruptState.InterruptCaller))
                {
                    interruptState.InterruptCaller = currentNode;
                }

                // Save checkpoint (skip if going to AskHuman, as it will be saved in catch block)
                if (nextNode != WorkflowEdges.AskHuman)
                {
                    await SaveCheckpointAsync(config, state, nextNode);
                }

                currentNode = nextNode;
            }

            return state;
        }
        catch (WorkflowInterruptException interruptEx)
        {
            _logger?.LogInformation("Workflow interrupted: {Message}", interruptEx.Message);

            // var interruptResumeNode = state.InterruptCaller ?? currentNode;
            state.InterruptRequestId = interruptEx.RequestId;
            state.InterruptCaller = interruptEx.ReturnToNode;
            await SaveCheckpointAsync(config, state, state.InterruptCaller);

            // Return current state - workflow will resume later
            return state;
        }
    }

    /// <summary>
    /// Gets checkpoint state or creates new state
    /// </summary>
    private async Task<(TState state, string? resumeNode)> GetOrCreateStateAsync(
        WorkflowRunnableConfig config,
        WorkflowCommand<TState> command)
    {
        var checkpoint = await _checkpointer.GetAsync(config);
        #region agent log
        DebugLog(
            location: "CompiledWorkflowGraph.GetOrCreateStateAsync",
            message: "Checkpoint loaded",
            data: new { hasCheckpoint = checkpoint != null },
            hypothesisId: "A");
        #endregion

        if (checkpoint != null)
        {
            // Restore state from checkpoint
            var restoredState = TryGetStateFromCheckpoint(checkpoint.Checkpoint);
            var resumeNode = TryGetStringChannel(checkpoint.Checkpoint, CurrentNodeChannel);
            if (restoredState != null)
            {
                #region agent log
                DebugLog(
                    location: "CompiledWorkflowGraph.GetOrCreateStateAsync",
                    message: "Restored state",
                    data: new { resumeNode, restoredType = restoredState.GetType().Name },
                    hypothesisId: "B");
                #endregion
                // Apply resume if it's a human message
                if (command.Resume is HumanMessage restoredResumeMessage && restoredState is WorkflowStateBase restoredResumeBaseState)
                {
                    restoredResumeBaseState.Messages.Add(restoredResumeMessage);
                }

                return (restoredState, resumeNode);
            }
        }

        // Create new state
        var newState = command.Update ?? Activator.CreateInstance<TState>();

        // Apply resume if it's a human message
        if (command.Resume is HumanMessage newResumeMessage && newState is WorkflowStateBase newResumeBaseState)
        {
            newResumeBaseState.Messages.Add(newResumeMessage);
        }

        return (newState, null);
    }

    /// <summary>
    /// Merges two states
    /// </summary>
    private TState MergeState(TState current, TState update)
    {
        // Simple merge using JSON serialization
        // In production, you might want a more sophisticated merge strategy
        var currentJson = JsonSerializer.Serialize(current, _jsonOptions);
        var currentDict = JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson, _jsonOptions);
        var updateJson = JsonSerializer.Serialize(update, _jsonOptions);
        var updateDict = JsonSerializer.Deserialize<Dictionary<string, object>>(updateJson, _jsonOptions);

        if (currentDict != null && updateDict != null)
        {
            foreach (var kvp in updateDict)
            {
                currentDict[kvp.Key] = kvp.Value;
            }
        }

        var mergedJson = JsonSerializer.Serialize(currentDict, _jsonOptions);
        return JsonSerializer.Deserialize<TState>(mergedJson, _jsonOptions) ?? current;
    }

    /// <summary>
    /// Saves checkpoint
    /// </summary>
    private async Task SaveCheckpointAsync(WorkflowRunnableConfig config, TState state, string? currentNode)
    {
        var newVersions = new Dictionary<string, string>
        {
            [StateChannel] = NextVersion()
        };

        if (!string.IsNullOrWhiteSpace(currentNode))
        {
            newVersions[CurrentNodeChannel] = NextVersion();
        }

        var checkpoint = new Checkpoint
        {
            Id = Guid.NewGuid().ToString(),
            ChannelValues = new Dictionary<string, object>
            {
                [StateChannel] = state
            },
            ChannelVersions = newVersions
        };

        if (!string.IsNullOrWhiteSpace(currentNode))
        {
            checkpoint.ChannelValues[CurrentNodeChannel] = currentNode;
        }

        if (state is WorkflowStateBase baseState)
        {
            baseState.LastCheckpointId = checkpoint.Id;
        }

        await _checkpointer.PutAsync(config, checkpoint, new { }, newVersions);
        #region agent log
        DebugLog(
            location: "CompiledWorkflowGraph.SaveCheckpointAsync",
            message: "Checkpoint saved",
            data: new { currentNode, channels = newVersions.Keys.ToArray() },
            hypothesisId: "A");
        #endregion
    }

    /// <summary>
    /// Gets the start node
    /// </summary>
    private string GetStartNode()
    {
        var startEdge = _edges.FirstOrDefault(e => e.From == WorkflowEdges.Start);
        return startEdge?.To ?? throw new InvalidOperationException("No start node found");
    }

    /// <summary>
    /// Determines the next node to execute
    /// </summary>
    private string? DetermineNextNode(
        string currentNode,
        WorkflowCommand<TState> command,
        TState state)
    {
        // If command specifies goto, use it
        if (!string.IsNullOrEmpty(command.Goto))
        {
            return command.Goto == WorkflowEdges.End ? WorkflowEdges.End : command.Goto;
        }

        // Check if current node has ends configuration
        if (_nodeEnds.TryGetValue(currentNode, out var ends) && ends.Count > 0)
        {
            // For now, return first end (in production, you'd evaluate conditions)
            return ends[0];
        }

        // Find edge from current node
        var edge = _edges.FirstOrDefault(e => e.From == currentNode);
        return edge?.Condition != null ? edge.Condition(state) : edge?.To ?? WorkflowEdges.End;
    }

    /// <summary>
    /// Creates error handler for nodes
    /// </summary>
    private Func<Exception, WorkflowCommand<TState>> CreateErrorHandler(WorkflowRunnableConfig config)
    {
        return (ex) =>
        {
            _logger?.LogError(ex, "Error in workflow node");

            var errorState = Activator.CreateInstance<TState>();
            if (errorState is WorkflowStateBase baseState)
            {
                baseState.ErrorName = ex.GetType().Name;
                baseState.ErrorMessage = ex.Message;
                baseState.InterruptCaller = config.Context?.Tracking?.NodeName;
            }

            return WorkflowCommand<TState>.Create(
                gotoNode: "errorHandler",
                update: errorState
            );
        };
    }

    private TState? TryGetStateFromCheckpoint(Checkpoint checkpoint)
    {
        if (!checkpoint.ChannelValues.TryGetValue(StateChannel, out var stateObj) || stateObj == null)
            return null;

        #region agent log
        DebugLog(
            location: "CompiledWorkflowGraph.TryGetStateFromCheckpoint",
            message: "State channel type",
            data: new { stateType = stateObj.GetType().Name },
            hypothesisId: "B");
        #endregion
        if (stateObj is TState typedState)
            return typedState;

        if (stateObj is JsonElement jsonElement)
        {
            return jsonElement.Deserialize<TState>(_jsonOptions);
        }

        var json = JsonSerializer.Serialize(stateObj, _jsonOptions);
        return JsonSerializer.Deserialize<TState>(json, _jsonOptions);
    }

    private string? TryGetStringChannel(Checkpoint checkpoint, string channel)
    {
        if (!checkpoint.ChannelValues.TryGetValue(channel, out var value) || value == null)
            return null;

        if (value is string stringValue)
            return stringValue;

        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind == JsonValueKind.String ? jsonElement.GetString() : jsonElement.ToString();
        }

        return value.ToString();
    }

    /// <summary>
    /// Gets checkpoint state
    /// </summary>
    public async Task<CheckpointTuple?> GetCheckpointAsync(WorkflowRunnableConfig config)
    {
        return await _checkpointer.GetAsync(config);
    }

    private static void DebugLog(string location, string message, object data, string hypothesisId, string runId = "run1")
    {
        try
        {
            var payload = new
            {
                sessionId = "debug-session",
                runId,
                hypothesisId,
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            var line = JsonSerializer.Serialize(payload);
            File.AppendAllText(DebugLogPath, line + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static string NextVersion()
    {
        var version = Interlocked.Increment(ref _versionCounter);
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var rawHint = BitConverter.ToInt64(bytes);
        var hint = Math.Abs(rawHint) % 10_000_000_000_000_000;
        return $"{version:D32}.{hint:D16}";
    }
}
