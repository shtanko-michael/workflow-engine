using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.State;
using WorkflowEngine.Persistence.Postgres;
using WorkflowEngine.Persistence.Postgres.Entities;
using Xunit;

namespace WorkflowEngine.Persistence.Postgres.Tests;

public class PostgresCheckpointSaverTests
{
    private const string ConnectionString =
        "Server=localhost;Port=5432;Database=workflow_engine_test;user id=saa;password=1235;";

    [Fact]
    public async Task PutAndGetCheckpoint_RoundTripsInlineAndBlobValues()
    {
        var threadId = $"test-thread-{Guid.NewGuid()}";
        var checkpointNs = string.Empty;
        var config = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>
            {
                ["thread_id"] = threadId,
                ["checkpoint_ns"] = checkpointNs
            }
        };

        await using var dbContext = CreateDbContext();
        var saver = new PostgresCheckpointSaver(dbContext, NullLogger<PostgresCheckpointSaver>.Instance);
        await CleanupAsync(dbContext);
        await saver.SetupAsync();

        var checkpoint = new Checkpoint
        {
            Id = Guid.NewGuid().ToString(),
            ChannelValues = new Dictionary<string, object>
            {
                ["flag"] = true,
                ["count"] = 3,
                ["state"] = new Dictionary<string, object> { ["stage"] = "survey" }
            },
            ChannelVersions = new Dictionary<string, string>
            {
                ["flag"] = CreateVersion(),
                ["count"] = CreateVersion(),
                ["state"] = CreateVersion()
            }
        };

        await saver.PutAsync(config, checkpoint, new { }, checkpoint.ChannelVersions);
        var restored = await saver.GetAsync(config);

        Assert.NotNull(restored);
        Assert.True(restored!.Checkpoint.ChannelValues.ContainsKey("flag"));
        Assert.True(restored.Checkpoint.ChannelValues.ContainsKey("count"));
        Assert.True(restored.Checkpoint.ChannelValues.ContainsKey("state"));

        Assert.True(IsJsonTrue(restored.Checkpoint.ChannelValues["flag"]));
        Assert.Equal(3, GetJsonNumber(restored.Checkpoint.ChannelValues["count"]));
        Assert.Equal("survey", GetJsonString(restored.Checkpoint.ChannelValues["state"], "stage"));
    }

    [Fact]
    public async Task Branching_CanRestorePreviousBranchCheckpoint()
    {
        var threadId = $"test-thread-{Guid.NewGuid()}";
        var checkpointNs = string.Empty;
        var baseConfig = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>
            {
                ["thread_id"] = threadId,
                ["checkpoint_ns"] = checkpointNs
            },
            Context = new WorkflowRunnableContext
            {
                Logger = NullLoggerFactory.Instance.CreateLogger("test")
            }
        };

        await using var dbContext = CreateDbContext();
        var saver = new PostgresCheckpointSaver(dbContext, NullLogger<PostgresCheckpointSaver>.Instance);
        await CleanupAsync(dbContext);
        await saver.SetupAsync();

        var graph = new WorkflowGraph<BranchState>()
            .AddNode("start", (state, ctx, errorHandler, cfg) =>
                Task.FromResult(WorkflowCommand<BranchState>.Create(gotoNode: "decide", update: state)))
            .AddNode("decide", (state, ctx, errorHandler, cfg) =>
            {
                var branch = cfg.Configurable.TryGetValue("branch_choice", out var choice) && choice != null
                    ? choice.ToString()
                    : "A";
                state.BranchChoice = branch;
                return Task.FromResult(WorkflowCommand<BranchState>.Create(
                    gotoNode: branch == "A" ? "branchA" : "branchB",
                    update: state));
            })
            .AddNode("branchA", (state, ctx, errorHandler, cfg) =>
            {
                state.Path = "A";
                return Task.FromResult(WorkflowCommand<BranchState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddNode("branchB", (state, ctx, errorHandler, cfg) =>
            {
                state.Path = "B";
                return Task.FromResult(WorkflowCommand<BranchState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "start")
            .Compile(saver);

        var stateA = new BranchState { BranchChoice = "A" };

        var configAStart = CloneConfig(baseConfig);
        configAStart.Configurable["branch_choice"] = "A";
        await graph.InvokeAsync(WorkflowCommand<BranchState>.Create(update: stateA), configAStart);

        var checkpointsAfterA = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(baseConfig))
        {
            checkpointsAfterA.Add(item);
        }

        var baseCheckpoint = checkpointsAfterA.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("current_node", out var nodeValue) &&
            GetCurrentNode(nodeValue) == "decide");

        Assert.NotNull(baseCheckpoint);

        var configBStart = CloneConfig(baseConfig);
        configBStart.Configurable["checkpoint_id"] = baseCheckpoint!.Config.Configurable["checkpoint_id"];
        configBStart.Configurable["branch_choice"] = "B";
        await graph.InvokeAsync(WorkflowCommand<BranchState>.Create(), configBStart);

        var checkpoints = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(baseConfig))
        {
            checkpoints.Add(item);
        }

        var checkpointA = checkpoints.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("state", out var value) &&
            GetBranchKey(value) == "A");
        var checkpointB = checkpoints.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("state", out var value) &&
            GetBranchKey(value) == "B");

        Assert.NotNull(checkpointA);
        Assert.NotNull(checkpointB);

        var configA = CloneConfig(baseConfig);
        configA.Configurable["checkpoint_id"] = checkpointA!.Config.Configurable["checkpoint_id"];

        var restoredA = await graph.InvokeAsync(WorkflowCommand<BranchState>.Create(), configA);
        Assert.Equal("A", restoredA.Path ?? restoredA.BranchChoice);

        var configB = CloneConfig(baseConfig);
        configB.Configurable["checkpoint_id"] = checkpointB!.Config.Configurable["checkpoint_id"];

        var restoredB = await graph.InvokeAsync(WorkflowCommand<BranchState>.Create(), configB);
        Assert.Equal("B", restoredB.Path ?? restoredB.BranchChoice);
    }

    [Fact]
    public async Task Branching_StateDrivenChoice_RestoresBothBranches()
    {
        var threadId = $"test-thread-{Guid.NewGuid()}";
        var checkpointNs = string.Empty;
        var baseConfig = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>
            {
                ["thread_id"] = threadId,
                ["checkpoint_ns"] = checkpointNs
            },
            Context = new WorkflowRunnableContext
            {
                Logger = NullLoggerFactory.Instance.CreateLogger("test")
            }
        };

        await using var dbContext = CreateDbContext();
        var saver = new PostgresCheckpointSaver(dbContext, NullLogger<PostgresCheckpointSaver>.Instance);
        await CleanupAsync(dbContext);
        await saver.SetupAsync();

        var graph = new WorkflowGraph<BranchState>()
            .AddNode("start", (state, ctx, errorHandler, cfg) =>
                Task.FromResult(WorkflowCommand<BranchState>.Create(gotoNode: "setChoice", update: state)))
            .AddNode("setChoice", (state, ctx, errorHandler, cfg) =>
            {
                var branch = cfg.Configurable.TryGetValue("branch_choice", out var choice) && choice != null
                    ? choice.ToString()
                    : "A";
                state.BranchChoice = branch;
                return Task.FromResult(WorkflowCommand<BranchState>.Create(
                    gotoNode: "decide",
                    update: state));
            })
            .AddNode("decide", (state, ctx, errorHandler, cfg) =>
            {
                var branch = string.IsNullOrWhiteSpace(state.BranchChoice) ? "A" : state.BranchChoice;
                return Task.FromResult(WorkflowCommand<BranchState>.Create(
                    gotoNode: branch == "A" ? "branchA" : "branchB",
                    update: state));
            })
            .AddNode("branchA", (state, ctx, errorHandler, cfg) =>
            {
                state.Path = "A";
                return Task.FromResult(WorkflowCommand<BranchState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddNode("branchB", (state, ctx, errorHandler, cfg) =>
            {
                state.Path = "B";
                return Task.FromResult(WorkflowCommand<BranchState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "start")
            .Compile(saver);

        var stateA = new BranchState { BranchChoice = "A" };

        await graph.InvokeAsync(WorkflowCommand<BranchState>.Create(update: stateA), baseConfig);

        var checkpointsAfterA = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(baseConfig))
        {
            checkpointsAfterA.Add(item);
        }

        var baseCheckpoint = checkpointsAfterA.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("current_node", out var nodeValue) &&
            GetCurrentNode(nodeValue) == "setChoice");

        Assert.NotNull(baseCheckpoint);

        var configBStart = CloneConfig(baseConfig);
        configBStart.Configurable["checkpoint_id"] = baseCheckpoint!.Config.Configurable["checkpoint_id"];
        configBStart.Configurable["branch_choice"] = "B";
        var resultB = await graph.InvokeAsync(
            WorkflowCommand<BranchState>.Create(update: new BranchState { BranchChoice = "B" }),
            configBStart);
        Assert.Equal("B", resultB.Path ?? resultB.BranchChoice);

        var checkpoints = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(baseConfig))
        {
            checkpoints.Add(item);
        }

        var checkpointA = checkpoints.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("state", out var value) &&
            GetBranchKey(value) == "A");
        var checkpointB = checkpoints.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("state", out var value) &&
            GetBranchKey(value) == "B");

        Assert.NotNull(checkpointA);
        Assert.NotNull(checkpointB);

        var configA = CloneConfig(baseConfig);
        configA.Configurable["checkpoint_id"] = checkpointA!.Config.Configurable["checkpoint_id"];

        var restoredA = await graph.InvokeAsync(WorkflowCommand<BranchState>.Create(), configA);
        Assert.Equal("A", restoredA.Path ?? restoredA.BranchChoice);

        var configB = CloneConfig(baseConfig);
        configB.Configurable["checkpoint_id"] = checkpointB!.Config.Configurable["checkpoint_id"];

        var restoredB = await graph.InvokeAsync(WorkflowCommand<BranchState>.Create(), configB);
        Assert.Equal("B", restoredB.Path ?? restoredB.BranchChoice);
    }

    [Fact]
    public async Task Branching_ChatEdit_ForksFromSameCheckpoint()
    {
        var threadId = $"test-thread-{Guid.NewGuid()}";
        var checkpointNs = string.Empty;
        var baseConfig = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>
            {
                ["thread_id"] = threadId,
                ["checkpoint_ns"] = checkpointNs
            },
            Context = new WorkflowRunnableContext
            {
                Logger = NullLoggerFactory.Instance.CreateLogger("test")
            }
        };

        await using var dbContext = CreateDbContext();
        var saver = new PostgresCheckpointSaver(dbContext, NullLogger<PostgresCheckpointSaver>.Instance);
        await CleanupAsync(dbContext);
        await saver.SetupAsync();

        var graph = new WorkflowGraph<BranchState>()
            .AddNode("prompt", (state, ctx, errorHandler, cfg) =>
            {
                state.Messages.Add(new AIMessage { Content = "Pick A or B" });
                return Task.FromResult(WorkflowCommand<BranchState>.Create(
                    gotoNode: "route",
                    update: state));
            })
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<BranchState>())
            .AddNode("route", (state, ctx, errorHandler, cfg) =>
            {
                var lastMessage = state.Messages.LastOrDefault();
                if (lastMessage is HumanMessage humanMessage && !string.IsNullOrWhiteSpace(humanMessage.Content))
                {
                    state.Path = humanMessage.Content;
                    return Task.FromResult(WorkflowCommand<BranchState>.Create(
                        gotoNode: WorkflowEdges.End,
                        update: state));
                }

                return Task.FromResult(WorkflowCommand<BranchState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "prompt")
            .Compile(saver);

        var initialState = new BranchState();
        var firstRun = await graph.InvokeAsync(
            WorkflowCommand<BranchState>.Create(update: initialState),
            baseConfig);

        Assert.False(string.IsNullOrWhiteSpace(firstRun.InterruptRequestId));

        var checkpointsAfterPrompt = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(baseConfig))
        {
            checkpointsAfterPrompt.Add(item);
        }

        var baseCheckpoint = checkpointsAfterPrompt.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("current_node", out var nodeValue) &&
            GetCurrentNode(nodeValue) == WorkflowEdges.AskHuman &&
            c.Checkpoint.ChannelValues.TryGetValue("state", out var stateValue) &&
            GetInterruptRequestId(stateValue) == firstRun.InterruptRequestId);

        Assert.NotNull(baseCheckpoint);

        var resumeA = new HumanMessage
        {
            Content = "A",
            RequestId = firstRun.InterruptRequestId
        };
        var configA = CloneConfig(baseConfig);
        configA.Configurable["checkpoint_id"] = baseCheckpoint!.Config.Configurable["checkpoint_id"];
        var resultA = await graph.InvokeAsync(
            WorkflowCommand<BranchState>.Create(resume: resumeA),
            configA);
        Assert.Equal("A", resultA.Path);

        var resumeB = new HumanMessage
        {
            Content = "B",
            RequestId = firstRun.InterruptRequestId
        };
        var configB = CloneConfig(baseConfig);
        configB.Configurable["checkpoint_id"] = baseCheckpoint!.Config.Configurable["checkpoint_id"];
        var resultB = await graph.InvokeAsync(
            WorkflowCommand<BranchState>.Create(resume: resumeB),
            configB);
        Assert.Equal("B", resultB.Path);

        var checkpoints = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(baseConfig))
        {
            checkpoints.Add(item);
        }

        var checkpointA = checkpoints.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("state", out var value) &&
            GetBranchKey(value) == "A");
        var checkpointB = checkpoints.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("state", out var value) &&
            GetBranchKey(value) == "B");

        Assert.NotNull(checkpointA);
        Assert.NotNull(checkpointB);

        var restoreConfigA = CloneConfig(baseConfig);
        restoreConfigA.Configurable["checkpoint_id"] = checkpointA!.Config.Configurable["checkpoint_id"];
        var restoredA = await graph.InvokeAsync(WorkflowCommand<BranchState>.Create(), restoreConfigA);
        Assert.Equal("A", restoredA.Path);

        var restoreConfigB = CloneConfig(baseConfig);
        restoreConfigB.Configurable["checkpoint_id"] = checkpointB!.Config.Configurable["checkpoint_id"];
        var restoredB = await graph.InvokeAsync(WorkflowCommand<BranchState>.Create(), restoreConfigB);
        Assert.Equal("B", restoredB.Path);
    }

    [Fact]
    public async Task Branching_EditOlderMessage_ForksByRequestId()
    {
        var threadId = $"test-thread-{Guid.NewGuid()}";
        var checkpointNs = string.Empty;
        var baseConfig = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>
            {
                ["thread_id"] = threadId,
                ["checkpoint_ns"] = checkpointNs
            },
            Context = new WorkflowRunnableContext
            {
                Logger = NullLoggerFactory.Instance.CreateLogger("test")
            }
        };

        await using var dbContext = CreateDbContext();
        var saver = new PostgresCheckpointSaver(dbContext, NullLogger<PostgresCheckpointSaver>.Instance);
        await CleanupAsync(dbContext);
        await saver.SetupAsync();

        var graph = new WorkflowGraph<BranchState>()
            .AddNode("prompt", (state, ctx, errorHandler, cfg) =>
            {
                var prompt = new AIMessage { Content = "Pick A or B" };
                state.Messages.Add(prompt);
                return Task.FromResult(WorkflowCommand<BranchState>.Create(
                    gotoNode: "route",
                    update: state));
            })
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<BranchState>())
            .AddNode("route", (state, ctx, errorHandler, cfg) =>
            {
                var lastMessage = state.Messages.LastOrDefault();
                if (lastMessage is HumanMessage humanMessage && !string.IsNullOrWhiteSpace(humanMessage.Content))
                {
                    state.Path = humanMessage.Content;
                    return Task.FromResult(WorkflowCommand<BranchState>.Create(
                        gotoNode: WorkflowEdges.End,
                        update: state));
                }

                return Task.FromResult(WorkflowCommand<BranchState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "prompt")
            .Compile(saver);

        var initialState = new BranchState();
        var firstRun = await graph.InvokeAsync(
            WorkflowCommand<BranchState>.Create(update: initialState),
            baseConfig);

        Assert.False(string.IsNullOrWhiteSpace(firstRun.InterruptRequestId));

        var checkpointsAfterPrompt = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(baseConfig))
        {
            checkpointsAfterPrompt.Add(item);
        }

        var promptId = firstRun.InterruptRequestId!;

        var baseCheckpoint = checkpointsAfterPrompt.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("current_node", out var nodeValue) &&
            GetCurrentNode(nodeValue) == WorkflowEdges.AskHuman &&
            c.Checkpoint.ChannelValues.TryGetValue("state", out var stateValue) &&
            GetInterruptRequestId(stateValue) == promptId);

        Assert.NotNull(baseCheckpoint);

        var resumeOld = new HumanMessage
        {
            Content = "A",
            RequestId = promptId
        };
        var configA = CloneConfig(baseConfig);
        configA.Configurable["checkpoint_id"] = baseCheckpoint!.Config.Configurable["checkpoint_id"];
        var resultA = await graph.InvokeAsync(
            WorkflowCommand<BranchState>.Create(resume: resumeOld),
            configA);
        Assert.Equal("A", resultA.Path);

        var resumeEdited = new HumanMessage
        {
            Content = "B",
            RequestId = promptId
        };
        var configB = CloneConfig(baseConfig);
        configB.Configurable["checkpoint_id"] = baseCheckpoint!.Config.Configurable["checkpoint_id"];
        var resultB = await graph.InvokeAsync(
            WorkflowCommand<BranchState>.Create(resume: resumeEdited),
            configB);
        Assert.Equal("B", resultB.Path);

        var checkpoints = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(baseConfig))
        {
            checkpoints.Add(item);
        }

        var checkpointA = checkpoints.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("state", out var value) &&
            GetBranchKey(value) == "A");
        var checkpointB = checkpoints.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("state", out var value) &&
            GetBranchKey(value) == "B");

        Assert.NotNull(checkpointA);
        Assert.NotNull(checkpointB);
    }

    [Fact]
    public async Task Branching_EditFirstMessage_AfterSecondPrompt()
    {
        var threadId = $"test-thread-{Guid.NewGuid()}";
        var checkpointNs = string.Empty;
        var baseConfig = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>
            {
                ["thread_id"] = threadId,
                ["checkpoint_ns"] = checkpointNs
            },
            Context = new WorkflowRunnableContext
            {
                Logger = NullLoggerFactory.Instance.CreateLogger("test")
            }
        };

        await using var dbContext = CreateDbContext();
        var saver = new PostgresCheckpointSaver(dbContext, NullLogger<PostgresCheckpointSaver>.Instance);
        await CleanupAsync(dbContext);
        await saver.SetupAsync();

        var graph = new WorkflowGraph<ChatBranchState>()
            .AddNode("prompt1", (state, ctx, errorHandler, cfg) =>
            {
                state.Messages.Add(new AIMessage { Content = "First answer?" });
                return Task.FromResult(WorkflowCommand<ChatBranchState>.Create(
                    gotoNode: "route1",
                    update: state));
            })
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<ChatBranchState>())
            .AddNode("route1", (state, ctx, errorHandler, cfg) =>
            {
                var lastMessage = state.Messages.LastOrDefault();
                if (lastMessage is HumanMessage humanMessage && !string.IsNullOrWhiteSpace(humanMessage.Content))
                {
                    state.Path1 = humanMessage.Content;
                    state.Path2 = null;
                    return Task.FromResult(WorkflowCommand<ChatBranchState>.Create(
                        gotoNode: "prompt2",
                        update: state));
                }

                return Task.FromResult(WorkflowCommand<ChatBranchState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state));
            })
            .AddNode("prompt2", (state, ctx, errorHandler, cfg) =>
            {
                state.Messages.Add(new AIMessage { Content = "Second answer?" });
                return Task.FromResult(WorkflowCommand<ChatBranchState>.Create(
                    gotoNode: "route2",
                    update: state));
            })
            .AddNode("route2", (state, ctx, errorHandler, cfg) =>
            {
                var lastMessage = state.Messages.LastOrDefault();
                if (lastMessage is HumanMessage humanMessage && !string.IsNullOrWhiteSpace(humanMessage.Content))
                {
                    state.Path2 = humanMessage.Content;
                    return Task.FromResult(WorkflowCommand<ChatBranchState>.Create(
                        gotoNode: WorkflowEdges.End,
                        update: state));
                }

                return Task.FromResult(WorkflowCommand<ChatBranchState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "prompt1")
            .Compile(saver);

        var firstInterrupt = await graph.InvokeAsync(
            WorkflowCommand<ChatBranchState>.Create(update: new ChatBranchState()),
            baseConfig);
        Assert.False(string.IsNullOrWhiteSpace(firstInterrupt.InterruptRequestId));
        var firstRequestId = firstInterrupt.InterruptRequestId!;

        var checkpointsAfterPrompt = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(baseConfig))
        {
            checkpointsAfterPrompt.Add(item);
        }

        var baseCheckpoint = checkpointsAfterPrompt.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("current_node", out var nodeValue) &&
            GetCurrentNode(nodeValue) == WorkflowEdges.AskHuman &&
            c.Checkpoint.ChannelValues.TryGetValue("state", out var stateValue) &&
            GetInterruptRequestId(stateValue) == firstRequestId);

        Assert.NotNull(baseCheckpoint);

        var configAnswer1 = CloneConfig(baseConfig);
        configAnswer1.Configurable["checkpoint_id"] = baseCheckpoint!.Config.Configurable["checkpoint_id"];
        var answer1 = new HumanMessage { Content = "A", RequestId = firstRequestId };
        var finished = await graph.InvokeAsync(
            WorkflowCommand<ChatBranchState>.Create(resume: answer1),
            configAnswer1);
        Assert.Equal("A", finished.Path1);
        Assert.Equal("A", finished.Path2);

        var checkpoints = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(baseConfig))
        {
            checkpoints.Add(item);
        }

        var editCheckpoint = checkpoints.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("current_node", out var nodeValue) &&
            GetCurrentNode(nodeValue) == WorkflowEdges.AskHuman &&
            c.Checkpoint.ChannelValues.TryGetValue("state", out var stateValue) &&
            GetInterruptRequestId(stateValue) == firstRequestId);

        Assert.NotNull(editCheckpoint);

        var configEdit = CloneConfig(baseConfig);
        configEdit.Configurable["checkpoint_id"] = editCheckpoint!.Config.Configurable["checkpoint_id"];
        var editMessage = new HumanMessage { Content = "A2", RequestId = firstRequestId };
        var editedState = await graph.InvokeAsync(
            WorkflowCommand<ChatBranchState>.Create(resume: editMessage),
            configEdit);

        Assert.Equal("A2", editedState.Path1);

        var checkpointsAfterEdit = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(baseConfig))
        {
            checkpointsAfterEdit.Add(item);
        }

        var editedCheckpoint = checkpointsAfterEdit.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("state", out var value) &&
            GetChatPath1(value) == "A2");

        Assert.NotNull(editedCheckpoint);
    }

    [Fact]
    public async Task Subgraph_PersistsAndRestoresParentAndChildStates()
    {
        var threadId = $"test-thread-{Guid.NewGuid()}";
        var checkpointNs = string.Empty;
        var baseConfig = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>
            {
                ["thread_id"] = threadId,
                ["checkpoint_ns"] = checkpointNs
            },
            Context = new WorkflowRunnableContext
            {
                Logger = NullLoggerFactory.Instance.CreateLogger("test")
            }
        };

        await using var dbContext = CreateDbContext();
        var saver = new PostgresCheckpointSaver(dbContext, NullLogger<PostgresCheckpointSaver>.Instance);
        await CleanupAsync(dbContext);
        await saver.SetupAsync();

        var subgraphGraph = new WorkflowGraph<SubgraphState>()
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<SubgraphState>())
            .AddNode("step", (state, _, _, _) =>
            {
                var human = state.Messages.OfType<HumanMessage>().LastOrDefault();
                if (human != null && !string.IsNullOrWhiteSpace(human.Content))
                {
                    state.SubValue = human.Content;
                    return Task.FromResult(WorkflowCommand<SubgraphState>.Create(
                        gotoNode: WorkflowEdges.End,
                        update: state));
                }

                state.Messages.Add(new AIMessage { Content = "subgraph prompt" });
                return Task.FromResult(WorkflowCommand<SubgraphState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "step")
            .AddEdge("step", WorkflowEdges.AskHuman)
            .AddEdge("step", WorkflowEdges.End);
        var subgraph = subgraphGraph.Compile(saver);

        var parentGraph = new WorkflowGraph<SubgraphState>()
            .AddNode("sub", subgraph)
            .AddNode("after", (state, _, _, _) =>
            {
                state.ParentValue = "after";
                return Task.FromResult(WorkflowCommand<SubgraphState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "sub")
            .AddEdge("sub", "after")
            .AddEdge("after", WorkflowEdges.End);
        var parent = parentGraph.Compile(saver);

        var firstRun = await parent.InvokeAsync(
            WorkflowCommand<SubgraphState>.Create(update: new SubgraphState()),
            baseConfig);

        Assert.False(firstRun.WorkflowCompleted);
        Assert.Equal("sub", firstRun.InterruptCaller);
        Assert.NotNull(firstRun.LastCheckpointId);

        var parentCheckpoints = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(baseConfig))
        {
            parentCheckpoints.Add(item);
        }

        var parentInterrupt = parentCheckpoints.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("current_node", out var nodeValue) &&
            GetCurrentNode(nodeValue) == "sub");
        Assert.NotNull(parentInterrupt);

        var childConfig = CloneConfig(baseConfig);
        childConfig.Configurable["checkpoint_ns"] = "sub";
        var childCheckpoints = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(childConfig))
        {
            childCheckpoints.Add(item);
        }

        var childInterrupt = childCheckpoints.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("current_node", out var nodeValue) &&
            GetCurrentNode(nodeValue) == WorkflowEdges.AskHuman);
        Assert.NotNull(childInterrupt);

        var resumeConfig = CloneConfig(baseConfig);
        var resume = new HumanMessage { Content = "ok" };
        var finished = await parent.InvokeAsync(
            WorkflowCommand<SubgraphState>.Create(resume: resume),
            resumeConfig);

        Assert.True(finished.WorkflowCompleted);
        Assert.Equal("ok", finished.SubValue);
        Assert.Equal("after", finished.ParentValue);

        var childCheckpointsAfterResume = new List<CheckpointTuple>();
        await foreach (var item in saver.ListAsync(childConfig))
        {
            childCheckpointsAfterResume.Add(item);
        }

        var childCompleted = childCheckpointsAfterResume.FirstOrDefault(c =>
            c.Checkpoint.ChannelValues.TryGetValue("state", out var value) &&
            GetJsonString(value, "subValue") == "ok");
        Assert.NotNull(childCompleted);
    }

    private static CheckpointDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CheckpointDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new CheckpointDbContext(options);
    }

    private static WorkflowRunnableConfig CloneConfig(WorkflowRunnableConfig config)
    {
        return new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object>(config.Configurable),
            Context = config.Context
        };
    }

    private static async Task CleanupAsync(CheckpointDbContext dbContext)
    {
        // Drop all checkpoint tables to ensure a clean database per test.
        await dbContext.Database.ExecuteSqlRawAsync(@"
DROP TABLE IF EXISTS checkpoint_blobs;
DROP TABLE IF EXISTS checkpoints;
DROP TABLE IF EXISTS checkpoint_migrations;");
    }

    private static string CreateVersion()
    {
        var version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"{version:D32}.0000000000000000";
    }

    private static bool IsJsonTrue(object value)
    {
        if (value is bool boolValue)
            return boolValue;
        if (value is JsonElement element && element.ValueKind == JsonValueKind.True)
            return true;
        return false;
    }

    private static int GetJsonNumber(object value)
    {
        if (value is int intValue)
            return intValue;
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Number)
            return element.GetInt32();
        return 0;
    }

    private static string? GetJsonString(object value, string propertyName)
    {
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
            return element.GetProperty(propertyName).GetString();
        if (value is Dictionary<string, object> dict && dict.TryGetValue(propertyName, out var inner))
            return inner?.ToString();
        return null;
    }

    private static string? GetBranchKey(object value)
    {
        if (value is BranchState branchState)
            return branchState.Path ?? branchState.BranchChoice;
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            if (TryGetProperty(element, out var path, "path", "Path"))
                return path.GetString();
            if (TryGetProperty(element, out var branch, "branchChoice", "branch_choice", "BranchChoice"))
                return branch.GetString();
        }
        if (value is Dictionary<string, object> dict)
        {
            if (TryGetDictionaryValue(dict, out var pathValue, "path", "Path"))
                return pathValue?.ToString();
            if (TryGetDictionaryValue(dict, out var branchValue, "branchChoice", "branch_choice", "BranchChoice"))
                return branchValue?.ToString();
        }
        return null;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement property, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out property))
                return true;
        }
        property = default;
        return false;
    }

    private static bool TryGetDictionaryValue(
        Dictionary<string, object> dict,
        out object? value,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (dict.TryGetValue(name, out value))
                return true;
        }
        value = null;
        return false;
    }

    private static string? GetCurrentNode(object value)
    {
        if (value is string stringValue)
            return stringValue;
        if (value is JsonElement element)
            return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        return value.ToString();
    }

    private static string? GetChatPath1(object value)
    {
        if (value is ChatBranchState chatState)
            return chatState.Path1;
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("path1", out var path))
            return path.GetString();
        if (value is Dictionary<string, object> dict && dict.TryGetValue("path1", out var inner))
            return inner?.ToString();
        return null;
    }

    private static string? GetInterruptRequestId(object value)
    {
        if (value is WorkflowStateBase baseState)
            return baseState.InterruptRequestId;
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("interruptRequestId", out var id))
            return id.GetString();
        if (value is Dictionary<string, object> dict && dict.TryGetValue("interruptRequestId", out var inner))
            return inner?.ToString();
        return null;
    }

    private class BranchState : WorkflowStateBase
    {
        public string? BranchChoice { get; set; }
        public string? Path { get; set; }
    }

    private class ChatBranchState : WorkflowStateBase
    {
        public string? Path1 { get; set; }
        public string? Path2 { get; set; }
    }

    private class SubgraphState : WorkflowStateBase
    {
        public string? SubValue { get; set; }
        public string? ParentValue { get; set; }
    }
}
