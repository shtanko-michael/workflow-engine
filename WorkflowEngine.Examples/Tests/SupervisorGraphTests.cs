using Microsoft.Extensions.Logging.Abstractions;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.State;
using WorkflowEngine.Core.Supervisor;
using WorkflowEngine.Persistence.Memory;
using Xunit;
using SupervisorTaskStatus = WorkflowEngine.Core.Supervisor.TaskStatus;

namespace WorkflowEngine.Examples.Tests;

public class SupervisorGraphTests
{
    private sealed class MemoryCheckpointSaveFactory : ICheckpointSaverFactory
    {
        private readonly MemoryCheckpointSaver _saver = new();

        public async Task<ICheckpointSaver> Build()
        {
            await _saver.SetupAsync();
            return _saver;
        }
    }

    private sealed class TestSupervisorState : SupervisorStateBase
    {
        public string Flow { get; set; } = string.Empty;
    }

    private static WorkflowRunnableConfig BaseConfig(string threadId, string? checkpointId = null)
    {
        var config = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object> { ["thread_id"] = threadId },
            Context = new WorkflowRunnableContext
            {
                Logger = NullLogger.Instance
            }
        };
        if (!string.IsNullOrWhiteSpace(checkpointId))
            config.Configurable["checkpoint_id"] = checkpointId;
        return config;
    }

    [Fact]
    public void TaskStackReducer_StartSwitchCancelResume_WorksAsExpected()
    {
        var state = new TestSupervisorState();

        TaskStackReducer.StartNew(state, "create_site");
        Assert.Single(state.TaskStack);
        Assert.Equal("create_site", state.TaskStack[^1].TaskType);
        Assert.Equal(SupervisorTaskStatus.Active, state.TaskStack[^1].Status);

        TaskStackReducer.StartNew(state, "export_contacts");
        Assert.Equal(2, state.TaskStack.Count);
        Assert.Equal(SupervisorTaskStatus.Suspended, state.TaskStack[0].Status);
        Assert.Equal("export_contacts", state.TaskStack[^1].TaskType);
        var firstTaskId = state.TaskStack[0].TaskId;

        TaskStackReducer.SwitchTo(state, "create_site");
        Assert.Equal("create_site", state.TaskStack[^1].TaskType);
        Assert.Equal(SupervisorTaskStatus.Active, state.TaskStack[^1].Status);
        Assert.Equal(SupervisorTaskStatus.Suspended, state.TaskStack[0].Status);

        TaskStackReducer.ResumeTask(state, firstTaskId);
        Assert.Equal(firstTaskId, state.TaskStack[^1].TaskId);
        Assert.Equal(SupervisorTaskStatus.Active, state.TaskStack[^1].Status);

        TaskStackReducer.CancelCurrent(state);
        Assert.Single(state.TaskStack);
        Assert.Equal("export_contacts", state.TaskStack[^1].TaskType);
        Assert.Equal(SupervisorTaskStatus.Active, state.TaskStack[^1].Status);
    }

    [Fact]
    public void TaskStackReducer_CancelAll_CreatesMenuTask()
    {
        var state = new TestSupervisorState();
        TaskStackReducer.StartNew(state, "create_site");
        TaskStackReducer.StartNew(state, "export_contacts");

        TaskStackReducer.CancelAll(state, new TaskStackReducerOptions { MenuTaskType = "menu" });

        Assert.Single(state.TaskStack);
        Assert.Equal("menu", state.TaskStack[^1].TaskType);
        Assert.Equal(SupervisorTaskStatus.Active, state.TaskStack[^1].Status);
        Assert.Equal(state.TaskStack[^1].TaskId, state.CurrentTaskId);
    }

    [Fact]
    public async Task SupervisorGraph_UsesCustomMenuNode_WhenConfigured()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();
        var taskGraph = BuildSimpleTaskGraph(checkpointer);
        var supervisor = new SupervisorGraph<TestSupervisorState>()
            .RegisterTask("worker", taskGraph)
            .SetIntentResolver((state, _, _) => Task.FromResult(SupervisorDecision.CancelAll("force-menu")))
            .SetMenuNode(WithContextNode.Wrap<TestSupervisorState>("custom_menu", (state, _, _, _) =>
            {
                state.Flow = "custom-menu";
                return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(gotoNode: WorkflowEdges.End, update: state));
            }))
            .Compile(checkpointer);

        var result = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(update: new TestSupervisorState()),
            BaseConfig("supervisor-custom-menu"));

        Assert.True(result.WorkflowCompleted);
        Assert.Equal("custom-menu", result.Flow);
        Assert.Equal("menu", result.TaskStack[^1].TaskType);
    }

    [Fact]
    public async Task SupervisorGraph_UsesDefaultMenuNode_WhenNotOverridden()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();
        var taskGraph = BuildSimpleTaskGraph(checkpointer);
        var supervisor = new SupervisorGraph<TestSupervisorState>()
            .RegisterTask("worker", taskGraph)
            .SetIntentResolver((state, _, _) => Task.FromResult(SupervisorDecision.CancelAll("force-menu")))
            .Compile(checkpointer);

        var result = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(update: new TestSupervisorState()),
            BaseConfig("supervisor-default-menu"));

        Assert.False(result.WorkflowCompleted);
        Assert.Equal(WorkflowInterruptReason.AskHuman, result.InterruptReason);
        Assert.Equal("__supervisor_menu__", result.InterruptCaller);
        Assert.NotNull(result.LastCheckpointId);
        Assert.Contains(result.Messages, m => m is AIMessage ai && ai.Content == "What would you like to do next?");
    }

    [Fact]
    public async Task SupervisorGraph_TaskInterruptAndResume_PreservesTaskCheckpoint()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();
        var taskGraph = BuildInterruptingTaskGraph(checkpointer);
        var supervisor = new SupervisorGraph<TestSupervisorState>()
            .RegisterTask("worker", taskGraph)
            .SetIntentResolver((state, _, _) =>
            {
                var hasWorker = state.TaskStack.Any(x => x.TaskType == "worker");
                var decision = hasWorker
                    ? SupervisorDecision.Continue("worker-running")
                    : SupervisorDecision.StartNew("worker", "boot");
                return Task.FromResult(decision);
            })
            .SetMenuNode(WithContextNode.Wrap<TestSupervisorState>("custom_menu", (state, _, _, _) =>
            {
                state.Flow += ":menu";
                return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(gotoNode: WorkflowEdges.End, update: state));
            }))
            .Compile(checkpointer);

        var firstRun = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(update: new TestSupervisorState()),
            BaseConfig("supervisor-interrupt"));

        Assert.False(firstRun.WorkflowCompleted);
        Assert.Equal(WorkflowInterruptReason.AskHuman, firstRun.InterruptReason);
        var interruptedTask = firstRun.TaskStack.Last();
        Assert.Equal("worker", interruptedTask.TaskType);
        Assert.NotNull(interruptedTask.CheckpointNs);
        Assert.NotNull(interruptedTask.CheckpointId);
        Assert.NotNull(firstRun.LastCheckpointId);

        var resumeConfig = BaseConfig("supervisor-interrupt", firstRun.LastCheckpointId);
        var resumed = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(resume: new HumanMessage { Content = "done" }),
            resumeConfig);

        Assert.True(resumed.WorkflowCompleted);
        Assert.Contains(":task-done", resumed.Flow);
        Assert.Equal(SupervisorTaskStatus.Completed, resumed.TaskStack.Last(x => x.TaskType == "worker").Status);
    }

    private static CompiledWorkflowGraph<TestSupervisorState> BuildSimpleTaskGraph(ICheckpointSaverFactory checkpointer)
    {
        var graph = new WorkflowGraph<TestSupervisorState>()
            .AddNode("task", (state, _, _, _) =>
            {
                state.Flow += ":task";
                return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "task")
            .AddEdge("task", WorkflowEdges.End);

        return graph.Compile(checkpointer);
    }

    private static CompiledWorkflowGraph<TestSupervisorState> BuildInterruptingTaskGraph(ICheckpointSaverFactory checkpointer)
    {
        var graph = new WorkflowGraph<TestSupervisorState>()
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<TestSupervisorState>())
            .AddNode("task", (state, _, _, _) =>
            {
                var hasHuman = state.Messages.OfType<HumanMessage>().Any();
                if (!hasHuman)
                {
                    state.Messages.Add(new AIMessage { Content = "Need confirmation." });
                    state.InterruptCaller = "task";
                    return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(
                        gotoNode: WorkflowEdges.AskHuman,
                        update: state));
                }

                state.Flow += ":task-done";
                return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "task")
            .AddEdge("task", WorkflowEdges.AskHuman)
            .AddEdge("task", WorkflowEdges.End)
            .AddEdge(WorkflowEdges.AskHuman, "task");

        return graph.Compile(checkpointer);
    }
}
