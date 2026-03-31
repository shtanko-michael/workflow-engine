using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.State;
using WorkflowEngine.Core.Supervisor;

namespace WorkflowEngine.Core.Graph;

public sealed class SupervisorGraph<TSupervisorState>
    where TSupervisorState : WorkflowStateBase, ISupervisorState
{
    private const string IntentNodeName = SupervisorNodeNames.Intent;
    private const string ApplyNodeName = SupervisorNodeNames.StackApply;
    private const string DispatchNodeName = SupervisorNodeNames.TaskDispatch;
    private const string MenuNodeName = SupervisorNodeNames.Menu;

    private readonly Dictionary<string, TaskRegistrationBase> _taskRegistrations = new(StringComparer.OrdinalIgnoreCase);
    private WorkflowNode<TSupervisorState>? _menuNode;
    private Func<TSupervisorState, WorkflowRunnableContext, WorkflowRunnableConfig, Task<SupervisorDecision>> _intentResolver;
    private readonly TaskStackReducerOptions _stackOptions = new();

    public SupervisorGraph()
    {
        _intentResolver = static (_, _, _) => Task.FromResult(SupervisorDecision.Continue("default"));
    }

    public SupervisorGraph<TSupervisorState> SetMenuNode(WorkflowNode<TSupervisorState> menuNode)
    {
        _menuNode = menuNode ?? throw new ArgumentNullException(nameof(menuNode));
        return this;
    }

    public SupervisorGraph<TSupervisorState> SetMenuNode(string menuTaskType, WorkflowNode<TSupervisorState> menuNode)
    {
        if (string.IsNullOrWhiteSpace(menuTaskType))
            throw new ArgumentException("Menu task type cannot be null or empty.", nameof(menuTaskType));
        _stackOptions.MenuTaskType = menuTaskType;
        _menuNode = menuNode ?? throw new ArgumentNullException(nameof(menuNode));
        return this;
    }

    public SupervisorGraph<TSupervisorState> SetIntentResolver(
        Func<TSupervisorState, WorkflowRunnableContext, WorkflowRunnableConfig, Task<SupervisorDecision>> resolver)
    {
        _intentResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        return this;
    }

    public SupervisorGraph<TSupervisorState> SetMenuTaskType(string menuTaskType)
    {
        if (string.IsNullOrWhiteSpace(menuTaskType))
            throw new ArgumentException("Menu task type cannot be null or empty.", nameof(menuTaskType));
        _stackOptions.MenuTaskType = menuTaskType;
        return this;
    }

    public SupervisorGraph<TSupervisorState> SetTaskFactory(Func<string, TaskInstance> taskFactory)
    {
        _stackOptions.TaskFactory = taskFactory ?? throw new ArgumentNullException(nameof(taskFactory));
        return this;
    }

    public SupervisorGraph<TSupervisorState> RegisterTask(
        string taskType,
        CompiledWorkflowGraph<TSupervisorState> taskGraph,
        string? taskName = null,
        string? taskDescription = null)
    {
        ValidateTaskType(taskType);
        ArgumentNullException.ThrowIfNull(taskGraph);
        _taskRegistrations[taskType] = new SameStateTaskRegistration(
            taskType,
            taskName ?? taskType,
            taskDescription ?? string.Empty,
            taskGraph,
            BuildTaskNodeName(taskType));
        return this;
    }

    public SupervisorGraph<TSupervisorState> RegisterTask<TTaskState>(
        string taskType,
        CompiledWorkflowGraph<TTaskState> taskGraph,
        Func<TSupervisorState, TaskInstance, TTaskState> initialStateMapping,
        Func<TSupervisorState, TTaskState, TaskInstance, TSupervisorState> completeStateMapping,
        string? taskName = null,
        string? taskDescription = null)
        where TTaskState : WorkflowStateBase
    {
        ValidateTaskType(taskType);
        ArgumentNullException.ThrowIfNull(taskGraph);
        ArgumentNullException.ThrowIfNull(initialStateMapping);
        ArgumentNullException.ThrowIfNull(completeStateMapping);
        _taskRegistrations[taskType] = new MappedTaskRegistration<TTaskState>(
            taskType,
            taskName ?? taskType,
            taskDescription ?? string.Empty,
            taskGraph,
            BuildTaskNodeName(taskType),
            initialStateMapping,
            completeStateMapping);
        return this;
    }

    public WorkflowGraph<TSupervisorState> Build()
    {
        if (_taskRegistrations.Count == 0)
            throw new InvalidOperationException("At least one task must be registered in supervisor graph.");

        var taskDescriptors = _taskRegistrations.Values
            .Select(x => x.Descriptor)
            .ToArray();
        var menuNode = _menuNode ?? DefaultSupervisorMenuNode.Create<TSupervisorState>(MenuNodeName, IntentNodeName);
        var nodeByTaskType = _taskRegistrations.ToDictionary(k => k.Key, v => v.Value.NodeName, StringComparer.OrdinalIgnoreCase);

        var graph = new WorkflowGraph<TSupervisorState>()
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<TSupervisorState>())
            .AddNode(WorkflowEdges.ErrorHandler, ErrorHandlerNode.Create<TSupervisorState>())
            .AddNode(IntentNodeName, SupervisorIntentNode.Create(_intentResolver))
            .AddNode(ApplyNodeName, SupervisorApplyDecisionNode.Create<TSupervisorState>(_stackOptions.MenuTaskType, MenuNodeName, DispatchNodeName, _stackOptions))
            .AddNode(DispatchNodeName, TaskDispatchNode.Create<TSupervisorState>(nodeByTaskType, MenuNodeName))
            .AddNode(MenuNodeName, WrapMenuNode(menuNode, taskDescriptors))
            .AddEdge(WorkflowEdges.Start, IntentNodeName)
            .AddEdge(IntentNodeName, ApplyNodeName)
            .AddEdge(ApplyNodeName, DispatchNodeName)
            .AddEdge(ApplyNodeName, MenuNodeName)
            .AddEdge(DispatchNodeName, MenuNodeName)
            .AddEdge(MenuNodeName, IntentNodeName);

        foreach (var registration in _taskRegistrations.Values)
        {
            registration.AddNode(graph);
            graph.AddEdge(registration.NodeName, IntentNodeName);
        }

        return graph;
    }

    public CompiledWorkflowGraph<TSupervisorState> Compile(ICheckpointSaverFactory checkpointer, Microsoft.Extensions.Logging.ILogger? logger = null) =>
        Build().Compile(checkpointer, logger);

    private static string BuildTaskNodeName(string taskType)
    {
        var normalized = taskType.Replace(':', '_').Replace(' ', '_');
        return $"__task__{normalized}";
    }

    private static void ValidateTaskType(string taskType)
    {
        if (string.IsNullOrWhiteSpace(taskType))
            throw new ArgumentException("Task type cannot be null or empty.", nameof(taskType));
    }

    private static WorkflowNode<TSupervisorState> WrapMenuNode(
        WorkflowNode<TSupervisorState> menuNode,
        IReadOnlyCollection<SupervisorTaskDescriptor> descriptors)
    {
        return async (state, context, errorHandler, config) =>
        {
            config.Configurable[SupervisorConfigKeys.AvailableTasks] = descriptors;
            return await menuNode(state, context, errorHandler, config).ConfigureAwait(false);
        };
    }

    private abstract class TaskRegistrationBase(string taskType, string taskName, string taskDescription, string nodeName)
    {
        public string TaskType { get; } = taskType;
        public string NodeName { get; } = nodeName;
        public SupervisorTaskDescriptor Descriptor { get; } = new()
        {
            TaskType = taskType,
            Name = taskName,
            Description = taskDescription
        };
        public abstract void AddNode(WorkflowGraph<TSupervisorState> graph);
    }

    private sealed class SameStateTaskRegistration(
        string taskType,
        string taskName,
        string taskDescription,
        CompiledWorkflowGraph<TSupervisorState> taskGraph,
        string nodeName) : TaskRegistrationBase(taskType, taskName, taskDescription, nodeName)
    {
        public override void AddNode(WorkflowGraph<TSupervisorState> graph)
        {
            graph.AddNode(NodeName, TaskAsNode.Create(taskGraph, NodeName, TaskType));
        }
    }

    private sealed class MappedTaskRegistration<TTaskState>(
        string taskType,
        string taskName,
        string taskDescription,
        CompiledWorkflowGraph<TTaskState> taskGraph,
        string nodeName,
        Func<TSupervisorState, TaskInstance, TTaskState> initialStateMapping,
        Func<TSupervisorState, TTaskState, TaskInstance, TSupervisorState> completeStateMapping)
        : TaskRegistrationBase(taskType, taskName, taskDescription, nodeName)
        where TTaskState : WorkflowStateBase
    {
        public override void AddNode(WorkflowGraph<TSupervisorState> graph)
        {
            graph.AddNode(NodeName, TaskAsNode.CreateWithMapping(
                taskGraph,
                NodeName,
                TaskType,
                initialStateMapping,
                completeStateMapping));
        }
    }
}

public static class SupervisorNodeNames
{
    public const string Intent = "__supervisor_intent__";
    public const string StackApply = "__supervisor_stack_apply__";
    public const string TaskDispatch = "__supervisor_task_dispatch__";
    public const string Menu = "__supervisor_menu__";
}

public static class SupervisorIntentNode
{
    public static WorkflowNode<TSupervisorState> Create<TSupervisorState>(
        Func<TSupervisorState, WorkflowRunnableContext, WorkflowRunnableConfig, Task<SupervisorDecision>> resolver)
        where TSupervisorState : WorkflowStateBase, ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return WithContextNode.Wrap<TSupervisorState>("supervisor_intent", async (state, context, _, config) =>
        {
            var decision = await resolver(state, context, config).ConfigureAwait(false) ?? SupervisorDecision.Continue();
            config.Configurable[SupervisorConfigKeys.SupervisorDecision] = decision;
            return WorkflowCommand<TSupervisorState>.Create(update: state);
        });
    }
}

public static class SupervisorApplyDecisionNode
{
    public static WorkflowNode<TSupervisorState> Create<TSupervisorState>(
        string menuTaskType,
        string menuNodeName,
        string dispatchNodeName,
        TaskStackReducerOptions stackOptions)
        where TSupervisorState : WorkflowStateBase, ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(stackOptions);
        return WithContextNode.Wrap<TSupervisorState>("supervisor_stack_apply", (state, _, _, config) =>
        {
            var decision = config.Configurable.TryGetValue(SupervisorConfigKeys.SupervisorDecision, out var decisionObj)
                               && decisionObj is SupervisorDecision typedDecision
                ? typedDecision
                : SupervisorDecision.Continue();
            TaskStackReducer.Apply(state, decision, stackOptions);
            config.Configurable.Remove(SupervisorConfigKeys.SupervisorDecision);

            var currentTask = TaskStackReducer.GetCurrentTask(state);
            var gotoNode = currentTask == null || string.Equals(currentTask.TaskType, menuTaskType, StringComparison.OrdinalIgnoreCase)
                ? menuNodeName
                : dispatchNodeName;
            return Task.FromResult(WorkflowCommand<TSupervisorState>.Create(gotoNode: gotoNode, update: state));
        });
    }
}

public static class TaskDispatchNode
{
    public static WorkflowNode<TSupervisorState> Create<TSupervisorState>(
        IReadOnlyDictionary<string, string> nodeByTaskType,
        string menuNodeName)
        where TSupervisorState : WorkflowStateBase, ISupervisorState
    {
        ArgumentNullException.ThrowIfNull(nodeByTaskType);
        return WithContextNode.Wrap<TSupervisorState>("supervisor_task_dispatch", (state, _, _, _) =>
        {
            var currentTask = TaskStackReducer.GetCurrentTask(state);
            if (currentTask == null)
                return Task.FromResult(WorkflowCommand<TSupervisorState>.Create(gotoNode: menuNodeName, update: state));

            if (!nodeByTaskType.TryGetValue(currentTask.TaskType, out var taskNodeName))
                return Task.FromResult(WorkflowCommand<TSupervisorState>.Create(gotoNode: menuNodeName, update: state));

            return Task.FromResult(WorkflowCommand<TSupervisorState>.Create(gotoNode: taskNodeName, update: state));
        });
    }
}

public static class DefaultSupervisorMenuNode
{
    public static WorkflowNode<TSupervisorState> Create<TSupervisorState>(string menuNodeName, string intentNodeName)
        where TSupervisorState : WorkflowStateBase, ISupervisorState
    {
        return WithContextNode.Wrap<TSupervisorState>("supervisor_menu", (state, _, _, _) =>
        {
            var lastMessage = state.Messages.LastOrDefault();
            if (lastMessage is HumanMessage)
            {
                state.PendingQuestion = null;
                return Task.FromResult(WorkflowCommand<TSupervisorState>.Create(gotoNode: intentNodeName, update: state));
            }

            state.PendingQuestion ??= "What would you like to do next?";
            if (lastMessage is not AIMessage ai || !string.Equals(ai.Content, state.PendingQuestion, StringComparison.Ordinal))
            {
                state.Messages.Add(new AIMessage { Content = state.PendingQuestion });
            }

            state.InterruptCaller = menuNodeName;
            return Task.FromResult(WorkflowCommand<TSupervisorState>.Create(gotoNode: WorkflowEdges.AskHuman, update: state));
        });
    }
}
