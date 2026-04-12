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
        public SupervisorDecision? MenuDecision { get; set; }
        public string? LastMenuHandledHumanMessage { get; set; }
    }

    private sealed class EchoTaskState : WorkflowStateBase
    {
        public string? SeenHuman { get; set; }
    }

    private static WorkflowRunnableConfig BaseConfig(string threadId, string? checkpointId = null, bool skipSupervisorInternalCheckpoints = false)
    {
        var config = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object> { ["thread_id"] = threadId },
            Context = new WorkflowRunnableContext
            {
                Logger = NullLogger.Instance
            }
        };
        config.SkipSupervisorInternalCheckpoints = skipSupervisorInternalCheckpoints;
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
        Assert.Equal("create_site", TaskStackReducer.GetCurrentTask(state)?.TaskType);
        Assert.Equal(SupervisorTaskStatus.Active, TaskStackReducer.GetCurrentTask(state)?.Status);
        Assert.Single(state.TaskQueue);

        TaskStackReducer.StartNew(state, "export_contacts");
        Assert.Equal(2, state.TaskStack.Count);
        Assert.Equal("export_contacts", TaskStackReducer.GetCurrentTask(state)?.TaskType);
        Assert.Equal("export_contacts", state.TaskQueue[0].TaskType);
        Assert.Equal("create_site", state.TaskQueue[1].TaskType);
        var firstTaskId = state.TaskStack[0].TaskId;

        TaskStackReducer.SwitchTo(state, "create_site");
        Assert.Equal("create_site", TaskStackReducer.GetCurrentTask(state)?.TaskType);
        Assert.Equal(SupervisorTaskStatus.Active, TaskStackReducer.GetCurrentTask(state)?.Status);
        Assert.Equal("create_site", state.TaskQueue[0].TaskType);

        TaskStackReducer.ResumeTask(state, firstTaskId);
        Assert.Equal(firstTaskId, TaskStackReducer.GetCurrentTask(state)?.TaskId);
        Assert.Equal(SupervisorTaskStatus.Active, TaskStackReducer.GetCurrentTask(state)?.Status);

        TaskStackReducer.CancelCurrent(state);
        Assert.Equal("export_contacts", TaskStackReducer.GetCurrentTask(state)?.TaskType);
        Assert.DoesNotContain(state.TaskQueue, x => x.TaskType == "create_site");
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
        Assert.Empty(state.TaskQueue);
    }

    [Fact]
    public void TaskStackReducer_BatchIntents_QueuesTasksWithSameSourceMessage()
    {
        var state = new TestSupervisorState
        {
            Messages =
            [
                new HumanMessage { Id = "msg-1", Content = "weather and onboarding please" }
            ]
        };

        var decision = SupervisorDecision.Batch(
        [
            new SupervisorIntentItem
            {
                IntentType = SupervisorIntentType.StartNew,
                TaskType = "weather",
                SourceUserMessageId = "msg-1",
                Reason = "batch-start-weather",
            },
            new SupervisorIntentItem
            {
                IntentType = SupervisorIntentType.StartNew,
                TaskType = "onboarding",
                SourceUserMessageId = "msg-1",
                Reason = "batch-start-onboarding",
            }
        ], "batch-start");
        TaskStackReducer.Apply(state, decision);
        TaskStackReducer.DrainNextIntent(state);
        Assert.Equal("weather", TaskStackReducer.GetCurrentTask(state)?.TaskType);
        Assert.Equal("msg-1", TaskStackReducer.GetCurrentTask(state)?.SourceUserMessageId);

        var current = TaskStackReducer.GetCurrentTask(state)!;
        current.Status = SupervisorTaskStatus.Completed;
        TaskStackReducer.ContinueCurrent(state);
        TaskStackReducer.DrainNextIntent(state);
        Assert.Equal("onboarding", TaskStackReducer.GetCurrentTask(state)?.TaskType);
        Assert.Equal("msg-1", TaskStackReducer.GetCurrentTask(state)?.SourceUserMessageId);
    }

    [Fact]
    public void TaskStackReducer_FailCurrentAndClearQueue_CancelsPendingTasks()
    {
        var state = new TestSupervisorState();
        var decision = SupervisorDecision.Batch(
        [
            new SupervisorIntentItem { IntentType = SupervisorIntentType.StartNew, TaskType = "weather" },
            new SupervisorIntentItem { IntentType = SupervisorIntentType.StartNew, TaskType = "onboarding" }
        ], "batch-start");
        TaskStackReducer.Apply(state, decision);
        TaskStackReducer.DrainNextIntent(state);
        TaskStackReducer.DrainNextIntent(state);

        TaskStackReducer.FailCurrentAndClearQueue(state, "task-error:test");

        Assert.Empty(state.TaskQueue);
        Assert.All(state.TaskStack.Where(x => x.TaskType is "weather" or "onboarding"),
            x => Assert.Equal(SupervisorTaskStatus.Cancelled, x.Status));
        Assert.Contains(state.History, x => x.Contains("queue-fail-fast", StringComparison.Ordinal));
    }

    [Fact]
    public void TaskStackReducer_SuspendInterruptedAndTryMoveNext_ReordersQueue()
    {
        var state = new TestSupervisorState();
        TaskStackReducer.StartNew(state, "task-a");
        TaskStackReducer.StartNew(state, "task-b");

        Assert.Equal("task-b", TaskStackReducer.GetCurrentTask(state)?.TaskType);
        Assert.Equal(["task-b", "task-a"], state.TaskQueue.Select(x => x.TaskType).ToArray());

        var continued = TaskStackReducer.SuspendInterruptedAndTryMoveNext(state, "menu");

        Assert.True(continued);
        Assert.Equal("task-a", TaskStackReducer.GetCurrentTask(state)?.TaskType);
        Assert.Equal(["task-a", "task-b"], state.TaskQueue.Select(x => x.TaskType).ToArray());
        Assert.Equal(SupervisorTaskStatus.Suspended, state.TaskStack.Last(x => x.TaskType == "task-b").Status);
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
        Assert.Contains(":menu", resumed.Flow);
        Assert.DoesNotContain(":task-done", resumed.Flow);
        Assert.Equal(SupervisorTaskStatus.Active, resumed.TaskStack.Last(x => x.TaskType == "worker").Status);
    }

    [Fact]
    public async Task SupervisorGraph_BatchIntents_ExecutesQueueSequentially_WithSameSourceMessage()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();
        var weatherGraph = BuildEchoMappedTaskGraph(checkpointer);
        var onboardingGraph = BuildEchoMappedTaskGraph(checkpointer);
        var supervisor = new SupervisorGraph<TestSupervisorState>()
            .SetMenuNode("menu", WithContextNode.Wrap<TestSupervisorState>("menu", (state, _, _, _) =>
            {
                var lastHuman = state.Messages.LastOrDefault() as HumanMessage;
                if (lastHuman == null
                    || string.IsNullOrWhiteSpace(lastHuman.Content)
                    || string.Equals(lastHuman.Content, state.LastMenuHandledHumanMessage, StringComparison.Ordinal))
                {
                    state.Messages.Add(new AIMessage { Content = "Menu: waiting input." });
                    state.InterruptCaller = SupervisorNodeNames.Menu;
                    return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(gotoNode: WorkflowEdges.AskHuman, update: state));
                }

                state.LastMenuHandledHumanMessage = lastHuman.Content;
                var decision = SupervisorDecision.Batch(
                [
                    new SupervisorIntentItem
                    {
                        IntentType = SupervisorIntentType.StartNew,
                        TaskType = "weather",
                        SourceUserMessageId = lastHuman.Id,
                        Reason = "batch-weather"
                    },
                    new SupervisorIntentItem
                    {
                        IntentType = SupervisorIntentType.StartNew,
                        TaskType = "onboarding",
                        SourceUserMessageId = lastHuman.Id,
                        Reason = "batch-onboarding"
                    }
                ], "batch-from-user");
                state.MenuDecision = decision;
                return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(gotoNode: SupervisorNodeNames.Intent, update: state));
            }))
            .SetIntentResolver((state, _, _) =>
            {
                var decision = state.MenuDecision ?? SupervisorDecision.Continue("default");
                state.MenuDecision = null;
                return Task.FromResult(decision);
            })
            .RegisterTask(
                "weather",
                weatherGraph,
                initialStateMapping: (parent, _) => new EchoTaskState
                {
                    Messages = parent.Messages.Count > 0 ? [parent.Messages.Last()] : [],
                },
                completeStateMapping: (parent, sub, _) =>
                {
                    parent.Flow += $":weather-task:{sub.SeenHuman}";
                    return parent;
                })
            .RegisterTask(
                "onboarding",
                onboardingGraph,
                initialStateMapping: (parent, _) => new EchoTaskState
                {
                    Messages = parent.Messages.Count > 0 ? [parent.Messages.Last()] : [],
                },
                completeStateMapping: (parent, sub, _) =>
                {
                    parent.Flow += $":onboarding-task:{sub.SeenHuman}";
                    return parent;
                })
            .Compile(checkpointer);

        var run = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(update: new TestSupervisorState(), resume: new HumanMessage { Id = "msg-batch", Content = "weather and onboarding please" }),
            BaseConfig("supervisor-start-many-sequential"));

        Assert.Equal(WorkflowInterruptReason.AskHuman, run.InterruptReason);
        Assert.Contains(":weather-task:weather and onboarding please", run.Flow);
        Assert.Contains(":onboarding-task:weather and onboarding please", run.Flow);
    }

    [Fact]
    public async Task SupervisorGraph_TaskInterrupt_WithPendingQueue_ContinuesToNextTaskInSameRun()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();
        var interruptingTask = BuildInterruptingTaskGraph(checkpointer);
        var simpleTask = BuildSimpleTaskGraph(checkpointer);
        var supervisor = new SupervisorGraph<TestSupervisorState>()
            .SetIntentResolver((state, _, _) =>
            {
                var hasRunnableTasks = state.TaskStack.Any(x => x.TaskType is "task-a" or "task-b");
                if (!hasRunnableTasks)
                {
                    return Task.FromResult(SupervisorDecision.Batch(
                    [
                        new SupervisorIntentItem { IntentType = SupervisorIntentType.StartNew, TaskType = "task-b", Reason = "seed-b" },
                        new SupervisorIntentItem { IntentType = SupervisorIntentType.StartNew, TaskType = "task-a", Reason = "seed-a" },
                    ], "seed-queue"));
                }

                return Task.FromResult(SupervisorDecision.Continue("drain"));
            })
            .RegisterTask("task-a", interruptingTask)
            .RegisterTask("task-b", simpleTask)
            .Compile(checkpointer);

        var run = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(update: new TestSupervisorState()),
            BaseConfig("supervisor-interrupt-continue-queue"));

        Assert.Equal(WorkflowInterruptReason.AskHuman, run.InterruptReason);
        Assert.Contains(":task", run.Flow); // task-b from BuildSimpleTaskGraph executed in same run
    }

    [Fact]
    public async Task SupervisorGraph_OnboardingInterrupt_ThenWeatherMessage_SwitchesToWeatherThroughMenu()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();
        var onboardingGraph = BuildOnboardingTaskGraph(checkpointer);
        var weatherGraph = BuildWeatherTaskGraph(checkpointer);

        var supervisor = new SupervisorGraph<TestSupervisorState>()
            .SetMenuNode("menu", WithContextNode.Wrap<TestSupervisorState>("menu", (state, _, _, config) =>
            {
                if (config.Configurable.TryGetValue(WorkflowConfigKeys.WorkflowCommandKey, out var commandObj)
                    && commandObj is WorkflowCommand<TestSupervisorState> command
                    && command.IsResume
                    && command.Resume is HumanMessage resumeHuman)
                {
                    state.Messages.Add(resumeHuman);
                    command.Resume = null;
                }

                var lastHuman = state.Messages.LastOrDefault() as HumanMessage;
                if (lastHuman == null
                    || string.IsNullOrWhiteSpace(lastHuman.Content)
                    || string.Equals(lastHuman.Content, state.LastMenuHandledHumanMessage, StringComparison.Ordinal))
                {
                    state.Messages.Add(new AIMessage { Content = "Menu: choose onboarding or weather." });
                    state.InterruptCaller = SupervisorNodeNames.Menu;
                    return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(
                        gotoNode: WorkflowEdges.AskHuman,
                        update: state));
                }

                state.LastMenuHandledHumanMessage = lastHuman.Content;
                state.MenuDecision = lastHuman.Content.Contains("weather", StringComparison.OrdinalIgnoreCase)
                    ? SupervisorDecision.SwitchTo("weather", "user-asked-weather")
                    : SupervisorDecision.StartNew("onboarding", "start-onboarding");
                return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(
                    gotoNode: SupervisorNodeNames.Intent,
                    update: state));
            }))
            .SetIntentResolver((state, _, _) =>
            {
                var decision = state.MenuDecision ?? SupervisorDecision.Continue("default");
                state.MenuDecision = null;
                return Task.FromResult(decision);
            })
            .RegisterTask("onboarding", onboardingGraph)
            .RegisterTask("weather", weatherGraph)
            .Compile(checkpointer);

        var firstRun = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(update: new TestSupervisorState()),
            BaseConfig("supervisor-onboarding-weather-switch"));

        Assert.False(firstRun.WorkflowCompleted);
        Assert.Equal(WorkflowInterruptReason.AskHuman, firstRun.InterruptReason);
        Assert.Equal("menu", firstRun.TaskStack.Last(x => x.Status == SupervisorTaskStatus.Active).TaskType);
        Assert.Contains(firstRun.Messages, m => m is AIMessage ai
            && !string.IsNullOrWhiteSpace(ai.Content)
            && ai.Content.Contains("choose onboarding or weather", StringComparison.OrdinalIgnoreCase));

        var secondRunConfig = BaseConfig("supervisor-onboarding-weather-switch", firstRun.LastCheckpointId);
        var secondRun = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(resume: new HumanMessage { Content = "start onboarding" }),
            secondRunConfig);

        Assert.False(secondRun.WorkflowCompleted);
        Assert.Equal(WorkflowInterruptReason.AskHuman, secondRun.InterruptReason);
        Assert.Equal("onboarding", secondRun.TaskStack.Last(x => x.Status == SupervisorTaskStatus.Active).TaskType);
        Assert.Contains(secondRun.Messages, m => m is AIMessage ai && ai.Content == "Onboarding: what is your role?");

        var thirdRunConfig = BaseConfig("supervisor-onboarding-weather-switch", secondRun.LastCheckpointId);
        var thirdRun = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(resume: new HumanMessage { Content = "what weather in new york" }),
            thirdRunConfig);

        Assert.True(thirdRun.WorkflowCompleted);
        Assert.Contains(":weather-done", thirdRun.Flow);
        Assert.DoesNotContain(":onboarding-done", thirdRun.Flow);
        Assert.Contains(thirdRun.TaskStack, x => x.TaskType == "weather" && x.Status == SupervisorTaskStatus.Completed);
    }

    [Fact]
    public async Task SupervisorGraph_SuspendedTask_CanBeResumedAndContinued_FromCheckpoint()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();
        var onboardingGraph = BuildOnboardingTaskGraph(checkpointer);
        var weatherGraph = BuildWeatherNeedsCityTaskGraph(checkpointer);

        var supervisor = new SupervisorGraph<TestSupervisorState>()
            .SetMenuNode("menu", BuildIntentDrivenMenuNode())
            .SetIntentResolver((state, _, _) =>
            {
                var decision = state.MenuDecision ?? SupervisorDecision.Continue("default");
                state.MenuDecision = null;
                return Task.FromResult(decision);
            })
            .RegisterTask("onboarding", onboardingGraph)
            .RegisterTask("weather", weatherGraph)
            .Compile(checkpointer);

        var firstRun = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(update: new TestSupervisorState()),
            BaseConfig("supervisor-resume-suspended"));
        Assert.Equal(WorkflowInterruptReason.AskHuman, firstRun.InterruptReason);

        var secondRun = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(resume: new HumanMessage { Content = "start onboarding" }),
            BaseConfig("supervisor-resume-suspended", firstRun.LastCheckpointId));
        Assert.Equal(WorkflowInterruptReason.AskHuman, secondRun.InterruptReason);
        var onboardingTaskAfterInterrupt = secondRun.TaskStack.Last(x => x.TaskType == "onboarding");
        var onboardingTaskId = onboardingTaskAfterInterrupt.TaskId;
        Assert.NotNull(onboardingTaskAfterInterrupt.CheckpointId);

        var thirdRun = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(resume: new HumanMessage { Content = "switch to weather" }),
            BaseConfig("supervisor-resume-suspended", secondRun.LastCheckpointId));
        Assert.Equal(WorkflowInterruptReason.AskHuman, thirdRun.InterruptReason);
        Assert.Equal("weather", thirdRun.TaskStack.Last(x => x.Status == SupervisorTaskStatus.Active).TaskType);
        Assert.Contains(thirdRun.TaskStack, x => x.TaskType == "onboarding" && x.Status == SupervisorTaskStatus.Suspended);
        var thirdRunLastMessage = Assert.IsType<AIMessage>(thirdRun.Messages.Last());
        Assert.Equal("Weather: which city?", thirdRunLastMessage.Content);

        var fourthRun = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(resume: new HumanMessage { Content = "continue onboarding" }),
            BaseConfig("supervisor-resume-suspended", thirdRun.LastCheckpointId));
        Assert.Equal(WorkflowInterruptReason.AskHuman, fourthRun.InterruptReason);
        var resumedOnboarding = fourthRun.TaskStack.Last(x => x.Status == SupervisorTaskStatus.Active);
        Assert.Equal("onboarding", resumedOnboarding.TaskType);
        Assert.Equal(onboardingTaskId, resumedOnboarding.TaskId);
        Assert.Equal(1, fourthRun.TaskStack.Count(x => x.TaskType == "onboarding"));
        var lastMessage = Assert.IsType<AIMessage>(fourthRun.Messages.Last());
        Assert.Equal("Onboarding: what is your role?", lastMessage.Content);
    }

    [Fact]
    public async Task SupervisorGraph_SwitchBackToSuspendedTask_ReusesSameTaskInstance()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();
        var onboardingGraph = BuildOnboardingTaskGraph(checkpointer);
        var weatherGraph = BuildWeatherNeedsCityTaskGraph(checkpointer);

        var supervisor = new SupervisorGraph<TestSupervisorState>()
            .SetMenuNode("menu", BuildIntentDrivenMenuNode())
            .SetIntentResolver((state, _, _) =>
            {
                var decision = state.MenuDecision ?? SupervisorDecision.Continue("default");
                state.MenuDecision = null;
                return Task.FromResult(decision);
            })
            .RegisterTask("onboarding", onboardingGraph)
            .RegisterTask("weather", weatherGraph)
            .Compile(checkpointer);

        var run1 = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(update: new TestSupervisorState()),
            BaseConfig("supervisor-switch-reuse"));
        var run2 = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(resume: new HumanMessage { Content = "start onboarding" }),
            BaseConfig("supervisor-switch-reuse", run1.LastCheckpointId));
        var onboardingTaskId = run2.TaskStack.Last(x => x.TaskType == "onboarding").TaskId;

        var run3 = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(resume: new HumanMessage { Content = "switch to weather" }),
            BaseConfig("supervisor-switch-reuse", run2.LastCheckpointId));
        Assert.Equal("weather", run3.TaskStack.Last(x => x.Status == SupervisorTaskStatus.Active).TaskType);

        var run4 = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(resume: new HumanMessage { Content = "switch onboarding" }),
            BaseConfig("supervisor-switch-reuse", run3.LastCheckpointId));
        Assert.Equal(WorkflowInterruptReason.AskHuman, run4.InterruptReason);
        var activeOnboarding = run4.TaskStack.Last(x => x.Status == SupervisorTaskStatus.Active);
        Assert.Equal("onboarding", activeOnboarding.TaskType);
        Assert.Equal(onboardingTaskId, activeOnboarding.TaskId);
        Assert.Equal(1, run4.TaskStack.Count(x => x.TaskType == "onboarding"));
    }

    [Fact]
    public async Task SupervisorGraph_SkipSupervisorInternalCheckpoints_Flag_DoesNotBreakResumeFlow()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();
        var onboardingGraph = BuildOnboardingTaskGraph(checkpointer);
        var weatherGraph = BuildWeatherTaskGraph(checkpointer);

        var supervisor = new SupervisorGraph<TestSupervisorState>()
            .SetMenuNode("menu", BuildIntentDrivenMenuNode())
            .SetIntentResolver((state, _, _) =>
            {
                var decision = state.MenuDecision ?? SupervisorDecision.Continue("default");
                state.MenuDecision = null;
                return Task.FromResult(decision);
            })
            .RegisterTask("onboarding", onboardingGraph)
            .RegisterTask("weather", weatherGraph)
            .Compile(checkpointer);

        var run1 = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(update: new TestSupervisorState()),
            BaseConfig("supervisor-skip-internal-checkpoints", skipSupervisorInternalCheckpoints: true));
        Assert.Equal(WorkflowInterruptReason.AskHuman, run1.InterruptReason);
        Assert.NotNull(run1.LastCheckpointId);

        var run2 = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(resume: new HumanMessage { Content = "start onboarding" }),
            BaseConfig("supervisor-skip-internal-checkpoints", run1.LastCheckpointId, skipSupervisorInternalCheckpoints: true));
        Assert.Equal(WorkflowInterruptReason.AskHuman, run2.InterruptReason);
        Assert.Equal("onboarding", run2.TaskStack.Last(x => x.Status == SupervisorTaskStatus.Active).TaskType);

        var run3 = await supervisor.InvokeAsync(
            WorkflowCommand<TestSupervisorState>.Create(resume: new HumanMessage { Content = "what weather in new york" }),
            BaseConfig("supervisor-skip-internal-checkpoints", run2.LastCheckpointId, skipSupervisorInternalCheckpoints: true));
        Assert.True(run3.WorkflowCompleted);
        Assert.Contains(":weather-done", run3.Flow);
    }

    private static WorkflowNode<TestSupervisorState> BuildIntentDrivenMenuNode()
    {
        return WithContextNode.Wrap<TestSupervisorState>("menu", (state, _, _, config) =>
        {
            if (config.Configurable.TryGetValue(WorkflowConfigKeys.WorkflowCommandKey, out var commandObj)
                && commandObj is WorkflowCommand<TestSupervisorState> command
                && command.IsResume
                && command.Resume is HumanMessage resumeHuman)
            {
                state.Messages.Add(resumeHuman);
            }

            var lastHuman = state.Messages.LastOrDefault() as HumanMessage;
            if (lastHuman == null
                || string.IsNullOrWhiteSpace(lastHuman.Content)
                || string.Equals(lastHuman.Content, state.LastMenuHandledHumanMessage, StringComparison.Ordinal))
            {
                state.Messages.Add(new AIMessage { Content = "Menu: choose onboarding or weather." });
                state.InterruptCaller = SupervisorNodeNames.Menu;
                return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state));
            }

            state.LastMenuHandledHumanMessage = lastHuman.Content;
            var text = lastHuman.Content;
            var activeTask = TaskStackReducer.GetCurrentTask(state);
            if (text.Contains("continue onboarding", StringComparison.OrdinalIgnoreCase))
            {
                var onboarding = state.TaskStack.LastOrDefault(x =>
                    x.TaskType == "onboarding" && x.Status == SupervisorTaskStatus.Suspended);
                state.MenuDecision = onboarding != null
                    ? SupervisorDecision.ResumeTask(onboarding.TaskId, "user-resume-onboarding")
                    : SupervisorDecision.StartNew("onboarding", "resume-not-found");
            }
            else if (text.Contains("switch onboarding", StringComparison.OrdinalIgnoreCase))
            {
                state.MenuDecision = SupervisorDecision.SwitchTo("onboarding", "user-switch-onboarding");
            }
            else if (text.Contains("weather", StringComparison.OrdinalIgnoreCase))
            {
                state.MenuDecision = SupervisorDecision.SwitchTo("weather", "user-asked-weather");
            }
            else if (activeTask?.TaskType == "onboarding")
            {
                state.MenuDecision = SupervisorDecision.Continue("continue-active-onboarding");
            }
            else
            {
                state.MenuDecision = SupervisorDecision.StartNew("onboarding", "start-onboarding");
            }

            return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(
                gotoNode: SupervisorNodeNames.Intent,
                update: state));
        });
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

    private static CompiledWorkflowGraph<TestSupervisorState> BuildOnboardingTaskGraph(ICheckpointSaverFactory checkpointer)
    {
        var graph = new WorkflowGraph<TestSupervisorState>()
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<TestSupervisorState>())
            .AddNode("onboarding", (state, _, _, _) =>
            {
                var lastHuman = state.Messages.OfType<HumanMessage>().LastOrDefault();
                var hasRoleAnswer = lastHuman?.Content?.Contains("engineer", StringComparison.OrdinalIgnoreCase) == true;
                if (!hasRoleAnswer)
                {
                    state.Messages.Add(new AIMessage { Content = "Onboarding: what is your role?" });
                    state.InterruptCaller = "onboarding";
                    return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(
                        gotoNode: WorkflowEdges.AskHuman,
                        update: state));
                }

                state.Flow += ":onboarding-done";
                return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "onboarding")
            .AddEdge("onboarding", WorkflowEdges.AskHuman)
            .AddEdge("onboarding", WorkflowEdges.End)
            .AddEdge(WorkflowEdges.AskHuman, "onboarding");

        return graph.Compile(checkpointer);
    }

    private static CompiledWorkflowGraph<EchoTaskState> BuildEchoMappedTaskGraph(ICheckpointSaverFactory checkpointer)
    {
        var graph = new WorkflowGraph<EchoTaskState>()
            .AddNode("echo", (state, _, _, _) =>
            {
                var lastHuman = state.Messages.OfType<HumanMessage>().LastOrDefault();
                state.SeenHuman = lastHuman?.Content;
                return Task.FromResult(WorkflowCommand<EchoTaskState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "echo")
            .AddEdge("echo", WorkflowEdges.End);

        return graph.Compile(checkpointer);
    }

    private static CompiledWorkflowGraph<TestSupervisorState> BuildWeatherTaskGraph(ICheckpointSaverFactory checkpointer)
    {
        var graph = new WorkflowGraph<TestSupervisorState>()
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<TestSupervisorState>())
            .AddNode("weather", (state, _, _, _) =>
            {
                var lastHuman = state.Messages.OfType<HumanMessage>().LastOrDefault();
                var hasCity = !string.IsNullOrWhiteSpace(lastHuman?.Content)
                              && lastHuman.Content.Contains("weather", StringComparison.OrdinalIgnoreCase);
                if (!hasCity)
                {
                    state.Messages.Add(new AIMessage { Content = "Weather: which city?" });
                    state.InterruptCaller = "weather";
                    return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(
                        gotoNode: WorkflowEdges.AskHuman,
                        update: state));
                }

                state.Flow += ":weather-done";
                return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "weather")
            .AddEdge("weather", WorkflowEdges.AskHuman)
            .AddEdge("weather", WorkflowEdges.End)
            .AddEdge(WorkflowEdges.AskHuman, "weather");

        return graph.Compile(checkpointer);
    }

    private static CompiledWorkflowGraph<TestSupervisorState> BuildWeatherNeedsCityTaskGraph(ICheckpointSaverFactory checkpointer)
    {
        var graph = new WorkflowGraph<TestSupervisorState>()
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<TestSupervisorState>())
            .AddNode("weather", (state, _, _, _) =>
            {
                var lastHuman = state.Messages.OfType<HumanMessage>().LastOrDefault();
                var hasCity = !string.IsNullOrWhiteSpace(lastHuman?.Content)
                              && lastHuman.Content.Contains(" in ", StringComparison.OrdinalIgnoreCase);
                if (!hasCity)
                {
                    state.Messages.Add(new AIMessage { Content = "Weather: which city?" });
                    state.InterruptCaller = "weather";
                    return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(
                        gotoNode: WorkflowEdges.AskHuman,
                        update: state));
                }

                state.Flow += ":weather-done";
                return Task.FromResult(WorkflowCommand<TestSupervisorState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "weather")
            .AddEdge("weather", WorkflowEdges.AskHuman)
            .AddEdge("weather", WorkflowEdges.End)
            .AddEdge(WorkflowEdges.AskHuman, "weather");

        return graph.Compile(checkpointer);
    }
}
