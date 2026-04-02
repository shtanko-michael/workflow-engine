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
	private readonly ICheckpointSaverFactory _checkpointerFactory;
	private readonly ILogger? _logger;
	private readonly JsonSerializerOptions _jsonOptions;

	public CompiledWorkflowGraph(
		Dictionary<string, WorkflowNode<TState>> nodes,
		List<WorkflowEdge> edges,
		Dictionary<string, List<string>> nodeEnds,
		ICheckpointSaverFactory checkpointerFactory,
		ILogger? logger = null)
	{
		_nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
		_edges = edges ?? throw new ArgumentNullException(nameof(edges));
		_nodeEnds = nodeEnds ?? throw new ArgumentNullException(nameof(nodeEnds));
		_checkpointerFactory = checkpointerFactory ?? throw new ArgumentNullException(nameof(_checkpointerFactory));
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
		ArgumentNullException.ThrowIfNull(command);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(config.Context);

		// store command in config for nodes to access
		config.Configurable[WorkflowConfigKeys.WorkflowCommandKey] = command;

		var (state, resumeNode) = await GetOrCreateStateAsync(config, command);
		if (state.WorkflowCompleted)
			return state;

		var currentNode = resumeNode ?? GetStartNode();
		var errorHandler = CreateErrorHandler();
		#region agent log
		DebugLog(
			location: "CompiledWorkflowGraph.InvokeAsync",
			message: "Invoke starting",
			data: new { resumeNode, currentNode },
			hypothesisId: "A");
		#endregion

		await SafeNotifyInterceptorAsync(config, state, (i, c, s) => i.OnGraphStartedAsync(c, s));

		try
		{
			config.ParentCheckpointId = config.CheckpointId;
			// initially set next checkpoint id to be able to send proper checkpoint id to gateway
			config.CheckpointId = Guid.NewGuid().ToString();
			while (currentNode != null && currentNode != WorkflowEdges.End)
			{
				_logger?.LogDebug("Executing node: {NodeName}", currentNode);

				if (!_nodes.TryGetValue(currentNode, out var node))
				{
					throw new InvalidOperationException($"Node '{currentNode}' not found");
				}

				await SafeNotifyInterceptorAsync(config, state, (i, c, s) => i.OnNodeStartedAsync(currentNode, c, s));

				// Execute node
				var nodeCommand = await node(state, config.Context, (ex) => errorHandler((ex, config, state)), config);

				await SafeNotifyInterceptorAsync(config, state, (i, c, s) => i.OnNodeCompletedAsync(currentNode, c, s));

				// Apply state update
				if (nodeCommand.Update != null)
				{
					state = MergeState(state, nodeCommand.Update);
				}

				// Determine next node
				var nextNode = DetermineNextNode(currentNode, nodeCommand, state);
				switch (nextNode)
				{
					case WorkflowEdges.End:
						state.WorkflowCompleted = true;
						break;
					// double-check interrupt caller is set
					case WorkflowEdges.AskHuman or WorkflowEdges.ErrorHandler
						when string.IsNullOrEmpty(state.InterruptCaller):
						state.InterruptCaller = currentNode;
						break;
				}

				// Save checkpoint (skip if going to AskHuman/Error, those are handled in catch blocks)
				if (nextNode is not (WorkflowEdges.AskHuman or WorkflowEdges.ErrorHandler)
				    && !ShouldSkipCheckpoint(config, currentNode))
				{
					await SaveCheckpointAsync(config, state, nextNode);
				}

				currentNode = nextNode;
			}

			await SafeNotifyInterceptorAsync(config, state, (i, c, s) => i.OnGraphCompletedAsync(c, s, WorkflowGraphCompletionReason.Normal));
			return state;
		}
		catch (WorkflowInterruptErrorException interruptEx)
		{
			_logger?.LogInformation("Workflow interrupted due to error: {Message}", interruptEx.Message);

			state.InterruptRequestId = interruptEx.RequestId;
			state.InterruptCaller = interruptEx.Caller;
			state.InterruptReason = WorkflowInterruptReason.Error;

			await SafeNotifyInterceptorAsync(config, state, (i, c, s) => i.OnErrorAsync(c, s));
			await SafeNotifyInterceptorAsync(config, state, (i, c, s) => i.OnInterruptAsync(c, s));
			await SafeNotifyInterceptorAsync(config, state, (i, c, s) => i.OnGraphCompletedAsync(c, s, WorkflowGraphCompletionReason.Error));

			// save checkpoint at the node that interrupted the workflow basically AskHuman node
			await SaveCheckpointAsync(config, state, WorkflowEdges.AskHuman);

			// Return current state - workflow will resume later
			return state;
		}
		catch (SubgraphWorkflowInterruptException interruptEx)
		{
			_logger?.LogInformation("Subgraph workflow interrupted: {Message}", interruptEx.Message);

			// var interruptResumeNode = state.InterruptCaller ?? currentNode;
			state.InterruptRequestId = interruptEx.RequestId;
			state.InterruptCaller = interruptEx.Caller;
			state.InterruptReason = WorkflowInterruptReason.AskHuman;

			await SafeNotifyInterceptorAsync(config, state, (i, c, s) => i.OnInterruptAsync(c, s));
			await SafeNotifyInterceptorAsync(config, state, (i, c, s) => i.OnGraphCompletedAsync(c, s, WorkflowGraphCompletionReason.Interrupt));

			// save checkpoint at the subgraph node that interrupted the parent
			await SaveCheckpointAsync(config, state, state.InterruptCaller);

			// Return current state - workflow will resume later
			return state;
		}
		catch (WorkflowInterruptException interruptEx)
		{
			_logger?.LogInformation("Workflow interrupted: {Message}", interruptEx.Message);

			// var interruptResumeNode = state.InterruptCaller ?? currentNode;
			state.InterruptRequestId = interruptEx.RequestId;
			state.InterruptCaller = interruptEx.Caller;
			state.InterruptReason = WorkflowInterruptReason.AskHuman;
			// save checkpoint at the node that interrupted the workflow basically AskHuman node
			await SaveCheckpointAsync(config, state, WorkflowEdges.AskHuman);

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
		var checkpointer = await _checkpointerFactory.Build();
		var checkpoint = await checkpointer.GetAsync(config);
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
			config.SubgraphCheckpointId = TryGetStringChannel(checkpoint.Checkpoint, "subgraph_checkpoint_id");
			config.SubgraphCheckpointNs = TryGetStringChannel(checkpoint.Checkpoint, "subgraph_checkpoint_ns");
			if (restoredState != null)
			{
				#region agent log
				DebugLog(
					location: "CompiledWorkflowGraph.GetOrCreateStateAsync",
					message: "Restored state",
					data: new { resumeNode, restoredType = restoredState.GetType().Name },
					hypothesisId: "B");
				#endregion
				return (restoredState, resumeNode);
			}
		}

		// Create new state
		var newState = command.Update ?? Activator.CreateInstance<TState>();
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
			[StateChannel] = NextVersion(),
		};

		if (!string.IsNullOrWhiteSpace(currentNode))
		{
			newVersions[CurrentNodeChannel] = NextVersion();
		}

		var cid = config.CheckpointId ?? Guid.NewGuid().ToString();
		var checkpoint = new Checkpoint
		{
			Id = cid,
			ChannelValues = new Dictionary<string, object>
			{
				[StateChannel] = state
			},
			ChannelVersions = newVersions
		};

		if (!string.IsNullOrEmpty(config.SubgraphCheckpointId))
			checkpoint.ChannelValues.Add(WorkflowConfigKeys.SubgraphCheckpointId, config.SubgraphCheckpointId);
		if (!string.IsNullOrEmpty(config.SubgraphCheckpointNs))
			checkpoint.ChannelValues.Add(WorkflowConfigKeys.SubgraphCheckpointNs, config.SubgraphCheckpointNs);

		if (!string.IsNullOrWhiteSpace(currentNode))
		{
			checkpoint.ChannelValues[CurrentNodeChannel] = currentNode;
		}

		if (state is WorkflowStateBase baseState)
		{
			baseState.LastCheckpointId = checkpoint.Id;
		}

		var checkpointer = await _checkpointerFactory.Build();
		await checkpointer.PutAsync(config, checkpoint, new { }, newVersions);

		config.ParentCheckpointId = config.CheckpointId;
		// Update config so gateways (e.g. bridge) see current checkpoint when creating messages
		config.CheckpointId = Guid.NewGuid().ToString();

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
	private Func<(Exception, WorkflowRunnableConfig, TState), WorkflowCommand<TState>> CreateErrorHandler()
	{
		return (x) =>
		{
			var (ex, config, state) = x;

			state.ErrorName = ex.GetType().Name;
			state.ErrorMessage = ex.Message;
			state.InterruptCaller = config.Context?.Tracking?.NodeName;
			state.InterruptReason = WorkflowInterruptReason.Error;

			return WorkflowCommand<TState>.Create(
				gotoNode: WorkflowEdges.ErrorHandler,
				update: state
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
		var checkpointer = await _checkpointerFactory.Build();
		return await checkpointer.GetAsync(config);
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

	private static bool ShouldSkipCheckpoint(WorkflowRunnableConfig config, string currentNode)
	{
		if (!config.SkipSupervisorInternalCheckpoints)
			return false;

		return currentNode is SupervisorNodeNames.Intent
			or SupervisorNodeNames.StackApply
			or SupervisorNodeNames.TaskDispatch;
	}

	/// <summary>
	/// Invokes the interceptor without letting it break the engine. Logs and swallows any exception.
	/// </summary>
	private async Task SafeNotifyInterceptorAsync(
		WorkflowRunnableConfig config,
		TState state,
		Func<IWorkflowRunInterceptor, WorkflowRunnableConfig, TState, Task> invoke)
	{
		if (config.Context?.Interceptor is not IWorkflowRunInterceptor interceptor)
			return;
		try
		{
			await invoke(interceptor, config, state);
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "Workflow interceptor notification failed");
		}
	}
}
