using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Core.State;
using WorkflowEngine.Persistence.Memory;
using Xunit;

namespace WorkflowEngine.Examples.Tests;

/// <summary>
/// Unit tests for IWorkflowRunGateway (bridge): default gateway, fake gateway, and graph nodes using context.Gateway.
/// </summary>
public class GatewayTests
{
    private static WorkflowRunnableConfig ConfigWithGateway(IWorkflowRunGateway gateway, string threadId = "t1")
    {
        return new WorkflowRunnableConfig
        {
            Configurable = new Dictionary<string, object> { ["thread_id"] = threadId },
            Context = new WorkflowRunnableContext
            {
                Gateway = gateway,
                Logger = NullLogger.Instance
            }
        };
    }

    /// <summary>
    /// Fake gateway that records all calls and returns deterministic message Ids for assertions.
    /// </summary>
    private sealed class FakeGateway : IWorkflowRunGateway
    {
        private int _messageCounter;

        public List<string> CreateParentIds { get; } = new();
        public List<string> CreatedMessageIds { get; } = new();
        public List<(string MessageId, string Chunk)> StreamChunks { get; } = new();
        public List<(string MessageId, string? FullContent)> NotifyStreamEndCalls { get; } = new();

        public Task<AIMessage> CreateAssistantMessageAsync(string? parentMessageId = null, CancellationToken cancellationToken = default)
        {
            CreateParentIds.Add(parentMessageId ?? "<null>");
            var id = $"msg-{++_messageCounter}";
            CreatedMessageIds.Add(id);
            return Task.FromResult(new AIMessage { Id = id, Content = string.Empty });
        }

        public Task StreamChunkAsync(string messageId, string chunk, CancellationToken cancellationToken = default)
        {
            StreamChunks.Add((messageId, chunk));
            return Task.CompletedTask;
        }

        public Task NotifyStreamEndAsync(string messageId, string? fullContent = null, CancellationToken cancellationToken = default)
        {
            NotifyStreamEndCalls.Add((messageId, fullContent));
            return Task.CompletedTask;
        }
    }

    private class TestState : WorkflowStateBase
    {
        public string Flow { get; set; } = "";
    }

    private static ICheckpointSaverFactory MemoryCheckpointer()
    {
        return new SubgraphTests.MemoryCheckpointSaveFactory();
    }

    [Fact]
    public async Task NodeUsingGateway_CreateMessage_StreamChunks_NotifyEnd_StateHasMessageWithGatewayId()
    {
        var gateway = new FakeGateway();
        var checkpointer = MemoryCheckpointer();

        var graph = new WorkflowGraph<TestState>()
            .AddNode("assistant", async (state, context, _, _) =>
            {
                var parentId = state.Messages.Count > 0 ? state.Messages[^1].Id : null;
                var message = await context.Gateway.CreateAssistantMessageAsync(parentId, CancellationToken.None);
                state.Messages.Add(message);

                await context.Gateway.StreamChunkAsync(message.Id, "Hello");
                await context.Gateway.StreamChunkAsync(message.Id, " ");
                await context.Gateway.StreamChunkAsync(message.Id, "world");

                message.Content = "Hello world";
                await context.Gateway.NotifyStreamEndAsync(message.Id, message.Content);

                state.Flow = "done";
                return WorkflowCommand<TestState>.Create(
                    gotoNode: WorkflowEdges.End,
                    update: state);
            })
            .AddEdge(WorkflowEdges.Start, "assistant")
            .AddEdge("assistant", WorkflowEdges.End);

        var compiled = graph.Compile(checkpointer);
        var config = ConfigWithGateway(gateway);
        var command = WorkflowCommand<TestState>.Create(update: new TestState());
        var result = await compiled.InvokeAsync(command, config);

        Assert.True(result.WorkflowCompleted);
        Assert.Equal("done", result.Flow);
        Assert.Single(result.Messages);
        var aiMsg = result.Messages[0] as AIMessage;
        Assert.NotNull(aiMsg);
        Assert.Equal("msg-1", aiMsg.Id);
        Assert.Equal("Hello world", aiMsg.Content);

        Assert.Single(gateway.CreateParentIds);
        Assert.Equal("<null>", gateway.CreateParentIds[0]);
        Assert.Equal(3, gateway.StreamChunks.Count);
        Assert.Equal("msg-1", gateway.StreamChunks[0].MessageId);
        Assert.Equal("Hello", gateway.StreamChunks[0].Chunk);
        Assert.Equal(" ", gateway.StreamChunks[1].Chunk);
        Assert.Equal("world", gateway.StreamChunks[2].Chunk);
        Assert.Single(gateway.NotifyStreamEndCalls);
        Assert.Equal("msg-1", gateway.NotifyStreamEndCalls[0].MessageId);
        Assert.Equal("Hello world", gateway.NotifyStreamEndCalls[0].FullContent);
    }

    [Fact]
    public async Task NodeUsingGateway_WithParentMessage_PassesParentIdToGateway()
    {
        var gateway = new FakeGateway();
        var checkpointer = MemoryCheckpointer();

        var graph = new WorkflowGraph<TestState>()
            .AddNode("assistant", async (state, context, _, _) =>
            {
                var parentId = state.Messages.Count > 0 ? state.Messages[^1].Id : null;
                var message = await context.Gateway.CreateAssistantMessageAsync(parentId, CancellationToken.None);
                state.Messages.Add(message);
                message.Content = "Reply";
                await context.Gateway.NotifyStreamEndAsync(message.Id, message.Content);
                return WorkflowCommand<TestState>.Create(gotoNode: WorkflowEdges.End, update: state);
            })
            .AddEdge(WorkflowEdges.Start, "assistant")
            .AddEdge("assistant", WorkflowEdges.End);

        var compiled = graph.Compile(checkpointer);
        var config = ConfigWithGateway(gateway);
        var initialState = new TestState();
        initialState.Messages.Add(new HumanMessage { Id = "user-1", Content = "Hi" });
        var command = WorkflowCommand<TestState>.Create(update: initialState);
        var result = await compiled.InvokeAsync(command, config);

        Assert.Single(gateway.CreateParentIds);
        Assert.Equal("user-1", gateway.CreateParentIds[0]);
        Assert.Equal("msg-1", result.Messages[^1].Id);
    }

    [Fact]
    public async Task DefaultWorkflowRunGateway_WithoutCallback_CreatesNewGuid_StreamNoOp_NotifyNoOp()
    {
        var gateway = new DefaultWorkflowRunGateway(legacyChunkCallback: null);
        var msg1 = await gateway.CreateAssistantMessageAsync(null);
        var msg2 = await gateway.CreateAssistantMessageAsync("parent-1");

        Assert.NotNull(msg1.Id);
        Assert.NotEqual(Guid.Empty, Guid.Parse(msg1.Id));
        Assert.NotNull(msg2.Id);
        Assert.NotEqual(msg1.Id, msg2.Id);
        Assert.True(string.IsNullOrEmpty(msg1.Content));
        Assert.True(string.IsNullOrEmpty(msg2.Content));

        await gateway.StreamChunkAsync("any-id", "chunk");
        await gateway.NotifyStreamEndAsync("any-id", "full");
        // No exception, no-op
    }

    [Fact]
    public async Task DefaultWorkflowRunGateway_WithLegacyCallback_StreamChunkInvokesCallbackWithChunkOnly()
    {
        var chunks = new List<string>();
        Func<string, Task> legacy = (chunk) => { chunks.Add(chunk); return Task.CompletedTask; };
        var gateway = new DefaultWorkflowRunGateway(legacy);

        await gateway.StreamChunkAsync("message-99", "a");
        await gateway.StreamChunkAsync("message-99", "b");

        Assert.Equal(2, chunks.Count);
        Assert.Equal("a", chunks[0]);
        Assert.Equal("b", chunks[1]);
    }

    [Fact]
    public async Task Controller_SetsGatewayFromConfig_WhenProvided()
    {
        var fakeGateway = new FakeGateway();
        var registry = new WorkflowRegistry();
        var checkpointer = MemoryCheckpointer();
        var logger = NullLogger<WorkflowController>.Instance;

        var graph = new WorkflowGraph<TestState>()
            .AddNode("n", async (state, context, _, _) =>
            {
                var msg = await context.Gateway.CreateAssistantMessageAsync(null);
                state.Messages.Add(msg);
                msg.Content = "ok";
                await context.Gateway.NotifyStreamEndAsync(msg.Id, msg.Content);
                return WorkflowCommand<TestState>.Create(gotoNode: WorkflowEdges.End, update: state);
            })
            .AddEdge(WorkflowEdges.Start, "n")
            .AddEdge("n", WorkflowEdges.End);

        var declaration = new WorkflowDeclaration<TestState>
        {
            Meta = new WorkflowMeta
            {
                Id = "gateway-test",
                Name = "Gateway Test",
                Description = "",
                Version = "1.0"
            },
            Workflow = graph
        };
        registry.Register(declaration);

        using var sp = new ServiceCollection()
            .AddSingleton<ICheckpointSaverFactory>(checkpointer)
            .AddSingleton(registry)
            .AddLogging()
            .BuildServiceProvider();

        var controller = new WorkflowController(
            registry,
            logger,
            sp);

        var result = await controller.ExecuteAsync<TestState>(new WorkflowControllerExecuteConfig
        {
            WorkflowType = "gateway-test",
            ThreadId = "ctrl-1",
            Gateway = fakeGateway
        });

        Assert.True(result.WorkflowCompleted);
        Assert.Single(fakeGateway.CreateParentIds);
        Assert.Single(fakeGateway.NotifyStreamEndCalls);
        Assert.Single(result.Messages);
        Assert.Equal(fakeGateway.CreatedMessageIds[0], result.Messages[0].Id);
    }

    [Fact]
    public async Task Controller_WhenNoGateway_UsesDefaultGateway_NodeStillGetsValidGateway()
    {
        var registry = new WorkflowRegistry();
        var checkpointer = MemoryCheckpointer();
        var logger = NullLogger<WorkflowController>.Instance;

        var graph = new WorkflowGraph<TestState>()
            .AddNode("n", async (state, context, _, _) =>
            {
                var msg = await context.Gateway.CreateAssistantMessageAsync(null);
                state.Messages.Add(msg);
                msg.Content = "from-default";
                await context.Gateway.NotifyStreamEndAsync(msg.Id, msg.Content);
                return WorkflowCommand<TestState>.Create(gotoNode: WorkflowEdges.End, update: state);
            })
            .AddEdge(WorkflowEdges.Start, "n")
            .AddEdge("n", WorkflowEdges.End);

        var declaration = new WorkflowDeclaration<TestState>
        {
            Meta = new WorkflowMeta
            {
                Id = "default-gw-test",
                Name = "Default Gateway Test",
                Description = "",
                Version = "1.0"
            },
            Workflow = graph
        };
        registry.Register(declaration);

        using var sp = new ServiceCollection()
            .AddSingleton<ICheckpointSaverFactory>(checkpointer)
            .AddSingleton(registry)
            .AddLogging()
            .BuildServiceProvider();

        var controller = new WorkflowController(registry, logger, sp);

        var result = await controller.ExecuteAsync<TestState>(new WorkflowControllerExecuteConfig
        {
            WorkflowType = "default-gw-test",
            ThreadId = "ctrl-2"
        });

        Assert.True(result.WorkflowCompleted);
        Assert.Single(result.Messages);
        Assert.NotNull(result.Messages[0].Id);
        Assert.Equal("from-default", (result.Messages[0] as AIMessage)?.Content);
    }
}
