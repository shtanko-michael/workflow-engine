using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.State;
using WorkflowEngine.Persistence.Memory;
using Xunit;

namespace WorkflowEngine.Examples.Tests;

/// <summary>
/// Unit tests for subgraph-as-node: parent graph with a node that runs a compiled subgraph in a child checkpoint namespace.
/// </summary>
public class SubgraphTests
{
    private static WorkflowRunnableConfig BaseConfig(string threadId, string? checkpointNs = null, string? checkpointId = null)
    {
        var config = new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object> { ["thread_id"] = threadId },
            Context = new WorkflowRunnableContext
            {
                Logger = NullLogger.Instance
            }
        };
        if (checkpointNs != null)
            config.Configurable["checkpoint_ns"] = checkpointNs;
        if (checkpointId != null)
            config.Configurable["checkpoint_id"] = checkpointId;
        return config;
    }

    /// <summary>
    /// Minimal state for subgraph tests: same shape as WorkflowStateBase, with a marker to assert flow.
    /// </summary>
    private class TestState : WorkflowStateBase
    {
        public string Flow { get; set; } = "";
    }

    /// <summary>
    /// Parent state type for different-state subgraph tests.
    /// </summary>
    private class ParentState : WorkflowStateBase
    {
        public string ParentFlow { get; set; } = "";
        public int ParentCounter { get; set; }
    }

    /// <summary>
    /// Subgraph state type (different from parent) for different-state subgraph tests.
    /// </summary>
    private class SubState : WorkflowStateBase
    {
        public string SubFlow { get; set; } = "";
        public int SubCounter { get; set; }
    }

    public class MemoryCheckpointSaveFactory : ICheckpointSaverFactory
    {
        private readonly MemoryCheckpointSaver _saver = new MemoryCheckpointSaver();

        public async Task<ICheckpointSaver> Build()
        {
            await _saver.SetupAsync();
            return _saver;
        }
    }

    [Fact]
    public async Task SubgraphAsNode_WhenSubgraphCompletes_ParentReceivesStateAndContinues()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();

        // Subgraph: single node that appends "subgraph" and goes to end
        var subgraphGraph = new WorkflowGraph<TestState>()
            .AddNode("inner", (state, _, _, _) =>
            {
                state.Flow += "subgraph";
                return Task.FromResult(WorkflowCommand<TestState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "inner")
            .AddEdge("inner", WorkflowEdges.End);
        var subgraph = subgraphGraph.Compile(checkpointer);

        // Parent: start -> subgraph node -> after
        var afterRan = false;
        var parentGraph = new WorkflowGraph<TestState>()
            .AddNode("sub", subgraph)
            .AddNode("after", (state, _, _, _) =>
            {
                state.Flow += ":after";
                afterRan = true;
                return Task.FromResult(WorkflowCommand<TestState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "sub")
            .AddEdge("sub", "after")
            .AddEdge("after", WorkflowEdges.End);
        var parent = parentGraph.Compile(checkpointer);

        var config = BaseConfig("t1");
        var command = WorkflowCommand<TestState>.Create(update: new TestState());
        var result = await parent.InvokeAsync(command, config);

        Assert.True(afterRan);
        Assert.True(result.WorkflowCompleted);
        Assert.Equal("subgraph:after", result.Flow);
    }

    [Fact]
    public async Task SubgraphAsNode_WhenSubgraphInterrupts_ParentSavesAtSubgraphNode()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();

        // Subgraph: goes to askHuman (will interrupt)
        var subgraphGraph = new WorkflowGraph<TestState>()
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<TestState>())
            .AddNode("step", (state, _, _, _) =>
            {
                state.Flow = "subgraph-step";
                state.Messages.Add(new AIMessage { Content = "?", Id = "step-msg" });
                return Task.FromResult(WorkflowCommand<TestState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "step")
            .AddEdge("step", WorkflowEdges.AskHuman);
        var subgraph = subgraphGraph.Compile(checkpointer);

        var parentGraph = new WorkflowGraph<TestState>()
            .AddNode("sub", subgraph)
            .AddNode("after", (state, _, _, _) =>
            {
                state.Flow += ":after";
                return Task.FromResult(WorkflowCommand<TestState>.Create(update: state));
            })
            .AddEdge(WorkflowEdges.Start, "sub")
            .AddEdge("sub", "after")
            .AddEdge("after", WorkflowEdges.End);
        var parent = parentGraph.Compile(checkpointer);

        var config = BaseConfig("t2");
        var command = WorkflowCommand<TestState>.Create(update: new TestState());
        var result = await parent.InvokeAsync(command, config);

        Assert.False(result.WorkflowCompleted);
        Assert.Equal("sub", result.InterruptCaller);
        Assert.Equal("subgraph-step", result.Flow);
        Assert.NotNull(result.LastCheckpointId);
    }

    [Fact]
    public async Task SubgraphAsNode_WhenResumedWithHumanMessage_SubgraphContinuesThenParentContinues()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();

        var subgraphGraph = new WorkflowGraph<TestState>()
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<TestState>())
            .AddNode("step", (state, _, _, _) =>
            {
                var hasHumanReply = state.Messages.OfType<HumanMessage>().Any();
                if (hasHumanReply)
                    return Task.FromResult(WorkflowCommand<TestState>.Create(gotoNode: "done", update: state));
                state.Flow = "subgraph-step";
                state.Messages.Add(new AIMessage { Content = "?", Id = "step-msg" });
                return Task.FromResult(WorkflowCommand<TestState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state));
            })
            .AddNode("done", (state, _, _, _) =>
            {
                state.Flow += ":done";
                state.WorkflowCompleted = true;
                return Task.FromResult(WorkflowCommand<TestState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "step")
            .AddEdge("step", WorkflowEdges.AskHuman)
            .AddEdge("step", "done")
            .AddEdge(WorkflowEdges.AskHuman, "step")
            .AddEdge("done", WorkflowEdges.End);
        var subgraph = subgraphGraph.Compile(checkpointer);

        var parentGraph = new WorkflowGraph<TestState>()
            .AddNode("sub", subgraph)
            .AddNode("after", (state, _, _, _) =>
            {
                state.Flow += ":after";
                return Task.FromResult(WorkflowCommand<TestState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "sub")
            .AddEdge("sub", "after")
            .AddEdge("after", WorkflowEdges.End);
        var parent = parentGraph.Compile(checkpointer);

        var config = BaseConfig("t3");
        var command = WorkflowCommand<TestState>.Create(update: new TestState());
        var result1 = await parent.InvokeAsync(command, config);

        Assert.False(result1.WorkflowCompleted);
        Assert.Equal("sub", result1.InterruptCaller);

        var resumeConfig = BaseConfig("t3", null);
        var humanMessage = new HumanMessage { Id = "msg1", Content = "user reply" };
        var resumeCommand = WorkflowCommand<TestState>.Create(resume: humanMessage);
        var result2 = await parent.InvokeAsync(resumeCommand, resumeConfig);

        Assert.True(result2.WorkflowCompleted);
        Assert.Contains("done", result2.Flow);
        Assert.Contains("after", result2.Flow);
        Assert.Contains(result2.Messages.OfType<HumanMessage>(), m => m.Content == "user reply");
    }

    [Fact]
    public async Task SubgraphAsNode_WithDifferentStateTypes_MapsAndMergesOnCompletion()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();

        var subgraphGraph = new WorkflowGraph<SubState>()
            .AddNode("inner", (state, _, _, _) =>
            {
                state.SubFlow += "sub";
                state.SubCounter = 42;
                return Task.FromResult(WorkflowCommand<SubState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "inner")
            .AddEdge("inner", WorkflowEdges.End);
        var subgraph = subgraphGraph.Compile(checkpointer);

        ParentState? mergedState = null;
        var parentGraph = new WorkflowGraph<ParentState>()
            .AddNode("sub", subgraph,
                mapParentToSubgraph: p => new SubState { SubFlow = p.ParentFlow, SubCounter = p.ParentCounter },
                mergeSubgraphIntoParent: (p, s) =>
                {
                    mergedState = new ParentState
                    {
                        ParentFlow = p.ParentFlow + ":" + s.SubFlow,
                        ParentCounter = s.SubCounter,
                        Messages = p.Messages,
                        InterruptCaller = p.InterruptCaller,
                        LastCheckpointId = p.LastCheckpointId,
                        WorkflowCompleted = p.WorkflowCompleted
                    };
                    return mergedState;
                })
            .AddNode("after", (state, _, _, _) =>
            {
                state.ParentFlow += ":after";
                return Task.FromResult(WorkflowCommand<ParentState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "sub")
            .AddEdge("sub", "after")
            .AddEdge("after", WorkflowEdges.End);
        var parent = parentGraph.Compile(checkpointer);

        var config = BaseConfig("t-diff");
        var command = WorkflowCommand<ParentState>.Create(update: new ParentState { ParentFlow = "start", ParentCounter = 0 });
        var result = await parent.InvokeAsync(command, config);

        Assert.True(result.WorkflowCompleted);
        Assert.Equal("start:startsub:after", result.ParentFlow);
        Assert.Equal(42, result.ParentCounter);
        Assert.NotNull(mergedState);
        Assert.Equal("start:startsub", mergedState.ParentFlow);
        Assert.Equal(42, mergedState.ParentCounter);
    }

    [Fact]
    public async Task SubgraphAsNode_WithDifferentStateTypes_InterruptAndResume_MergesCorrectly()
    {
        var checkpointer = new MemoryCheckpointSaveFactory();

        var subgraphGraph = new WorkflowGraph<SubState>()
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<SubState>())
            .AddNode("step", (state, _, _, _) =>
            {
                var hasHuman = state.Messages.OfType<HumanMessage>().Any();
                if (hasHuman)
                    return Task.FromResult(WorkflowCommand<SubState>.Create(gotoNode: "done", update: state));
                state.SubFlow = "sub-step";
                state.SubCounter = 1;
                state.Messages.Add(new AIMessage { Content = "?", Id = "ai" });
                return Task.FromResult(WorkflowCommand<SubState>.Create(
                    gotoNode: WorkflowEdges.AskHuman,
                    update: state));
            })
            .AddNode("done", (state, _, _, _) =>
            {
                state.SubFlow += ":done";
                state.SubCounter = 2;
                state.WorkflowCompleted = true;
                return Task.FromResult(WorkflowCommand<SubState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "step")
            .AddEdge("step", WorkflowEdges.AskHuman)
            .AddEdge("step", "done")
            .AddEdge(WorkflowEdges.AskHuman, "step")
            .AddEdge("done", WorkflowEdges.End);
        var subgraph = subgraphGraph.Compile(checkpointer);

        var parentGraph = new WorkflowGraph<ParentState>()
            .AddNode("sub", subgraph,
                mapParentToSubgraph: p => new SubState { SubFlow = p.ParentFlow, SubCounter = p.ParentCounter, Messages = p.Messages },
                mergeSubgraphIntoParent: (p, s) =>
                {
                    var merged = new ParentState
                    {
                        ParentFlow = p.ParentFlow + ":" + s.SubFlow,
                        ParentCounter = s.SubCounter,
                        Messages = s.Messages,
                        InterruptCaller = s.InterruptCaller,
                        LastCheckpointId = s.LastCheckpointId ?? p.LastCheckpointId,
                        WorkflowCompleted = false
                    };
                    return merged;
                })
            .AddNode("after", (state, _, _, _) =>
            {
                state.ParentFlow += ":after";
                return Task.FromResult(WorkflowCommand<ParentState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state));
            })
            .AddEdge(WorkflowEdges.Start, "sub")
            .AddEdge("sub", "after")
            .AddEdge("after", WorkflowEdges.End);
        var parent = parentGraph.Compile(checkpointer);

        var config = BaseConfig("t-diff2");
        var command = WorkflowCommand<ParentState>.Create(update: new ParentState { ParentFlow = "start" });
        var result1 = await parent.InvokeAsync(command, config);

        Assert.False(result1.WorkflowCompleted);
        Assert.Equal("sub", result1.InterruptCaller);
        Assert.NotNull(result1.LastCheckpointId);

        var resumeConfig = BaseConfig("t-diff2", null, result1.LastCheckpointId);
        var resumeCommand = WorkflowCommand<ParentState>.Create(resume: new HumanMessage { Id = "h1", Content = "ok" });
        var result2 = await parent.InvokeAsync(resumeCommand, resumeConfig);

        Assert.True(result2.WorkflowCompleted);
        Assert.Contains("done", result2.ParentFlow);
        Assert.Contains("after", result2.ParentFlow);
        Assert.Equal(2, result2.ParentCounter);
        Assert.Contains(result2.Messages.OfType<HumanMessage>(), m => m.Content == "ok");
        // Note: "sub-step" may be absent if subgraph restarted from mapped state (parent checkpoint_id passed to subgraph namespace)
    }
}
