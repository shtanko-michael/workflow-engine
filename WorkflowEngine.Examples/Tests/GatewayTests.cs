using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Core.State;
using WorkflowEngine.Examples.Contracts;
using WorkflowEngine.Persistence.Memory;
using Xunit;

namespace WorkflowEngine.Examples.Tests;

/// <summary>
/// Unit tests for IWorkflowMessageService: fake implementation and graph nodes resolving it from context.Container.
/// </summary>
public class GatewayTests
{
	/// <summary>
	/// Fake message service that records all calls and returns deterministic message Ids for assertions.
	/// </summary>
	private sealed class FakeMessageService : IWorkflowMessageService
	{
		private int _messageCounter;

		public List<string> CreateParentIds { get; } = new();
		public List<string> CreatedMessageIds { get; } = new();
		public List<(string MessageId, string Chunk)> StreamChunks { get; } = new();
		public List<(string MessageId, string? FullContent)> NotifyStreamEndCalls { get; } = new();

		public Task<AIMessage> CreateAssistantMessageAsync(WorkflowRunnableConfig config, string content = "", CancellationToken cancellationToken = default)
		{
			var parentId = config.Configurable.TryGetValue("parent_message_id", out var pid) ? pid?.ToString() ?? "<null>" : "<null>";
			CreateParentIds.Add(parentId);
			var id = $"msg-{++_messageCounter}";
			CreatedMessageIds.Add(id);
			return Task.FromResult(new AIMessage { Id = id, Content = content });
		}

		public Task StreamChunkAsync(WorkflowRunnableConfig config, string messageId, string chunk, CancellationToken cancellationToken = default)
		{
			StreamChunks.Add((messageId, chunk));
			return Task.CompletedTask;
		}

		public Task NotifyStreamEndAsync(WorkflowRunnableConfig config, string messageId, string? fullContent = null, string[]? options = null, CancellationToken cancellationToken = default)
		{
			NotifyStreamEndCalls.Add((messageId, fullContent));
			return Task.CompletedTask;
		}

		public Task<AIMessage> CreateErrorMessageAsync(WorkflowRunnableConfig config, string errorType, string? errorDetails, CancellationToken cancellationToken = default)
		{
			var id = $"err-{++_messageCounter}";
			CreatedMessageIds.Add(id);
			return Task.FromResult(new AIMessage { Id = id, Content = "" });
		}
	}

	private static WorkflowRunnableConfig ConfigWithMessageService(IWorkflowMessageService messageService, string threadId = "t1")
	{
		var services = new ServiceCollection()
			.AddSingleton(messageService)
			.BuildServiceProvider();
		return new WorkflowRunnableConfig
		{
			Configurable = new Dictionary<string, object> { ["thread_id"] = threadId },
			Context = new WorkflowRunnableContext
			{
				Container = services,
				Logger = NullLogger.Instance
			}
		};
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
	public async Task NodeUsingMessageService_CreateMessage_StreamChunks_NotifyEnd_StateHasMessageWithServiceId()
	{
		var messageService = new FakeMessageService();
		var checkpointer = MemoryCheckpointer();

		var graph = new WorkflowGraph<TestState>()
			.AddNode("assistant", async (state, context, _, config) =>
			{
				var ms = context.Container!.GetRequiredService<IWorkflowMessageService>();
				var parentId = state.Messages.Count > 0 ? state.Messages[^1].Id : null;
				if (parentId != null)
					config.Configurable["parent_message_id"] = parentId;
				var message = await ms.CreateAssistantMessageAsync(config, "", CancellationToken.None);
				state.Messages.Add(message);

				await ms.StreamChunkAsync(config, message.Id, "Hello");
				await ms.StreamChunkAsync(config, message.Id, " ");
				await ms.StreamChunkAsync(config, message.Id, "world");

				message.Content = "Hello world";
				await ms.NotifyStreamEndAsync(config, message.Id, message.Content);

				state.Flow = "done";
				return WorkflowCommand<TestState>.Create(
					gotoNode: WorkflowEdges.End,
					update: state);
			})
			.AddEdge(WorkflowEdges.Start, "assistant")
			.AddEdge("assistant", WorkflowEdges.End);

		var compiled = graph.Compile(checkpointer);
		var config = ConfigWithMessageService(messageService);
		var command = WorkflowCommand<TestState>.Create(update: new TestState());
		var result = await compiled.InvokeAsync(command, config);

		Assert.True(result.WorkflowCompleted);
		Assert.Equal("done", result.Flow);
		Assert.Single(result.Messages);
		var aiMsg = result.Messages[0] as AIMessage;
		Assert.NotNull(aiMsg);
		Assert.Equal("msg-1", aiMsg.Id);
		Assert.Equal("Hello world", aiMsg.Content);

		Assert.Single(messageService.CreateParentIds);
		Assert.Equal("<null>", messageService.CreateParentIds[0]);
		Assert.Equal(3, messageService.StreamChunks.Count);
		Assert.Equal("msg-1", messageService.StreamChunks[0].MessageId);
		Assert.Equal("Hello", messageService.StreamChunks[0].Chunk);
		Assert.Equal(" ", messageService.StreamChunks[1].Chunk);
		Assert.Equal("world", messageService.StreamChunks[2].Chunk);
		Assert.Single(messageService.NotifyStreamEndCalls);
		Assert.Equal("msg-1", messageService.NotifyStreamEndCalls[0].MessageId);
		Assert.Equal("Hello world", messageService.NotifyStreamEndCalls[0].FullContent);
	}

	[Fact]
	public async Task NodeUsingMessageService_WithParentMessage_PassesParentIdToService()
	{
		var messageService = new FakeMessageService();
		var checkpointer = MemoryCheckpointer();

		var graph = new WorkflowGraph<TestState>()
			.AddNode("assistant", async (state, context, _, config) =>
			{
				var ms = context.Container!.GetRequiredService<IWorkflowMessageService>();
				var parentId = state.Messages.Count > 0 ? state.Messages[^1].Id : null;
				if (parentId != null)
					config.Configurable["parent_message_id"] = parentId;
				var message = await ms.CreateAssistantMessageAsync(config, "", CancellationToken.None);
				state.Messages.Add(message);
				message.Content = "Reply";
				await ms.NotifyStreamEndAsync(config, message.Id, message.Content);
				return WorkflowCommand<TestState>.Create(gotoNode: WorkflowEdges.End, update: state);
			})
			.AddEdge(WorkflowEdges.Start, "assistant")
			.AddEdge("assistant", WorkflowEdges.End);

		var compiled = graph.Compile(checkpointer);
		var config = ConfigWithMessageService(messageService);
		var initialState = new TestState();
		initialState.Messages.Add(new HumanMessage { Id = "user-1", Content = "Hi" });
		var command = WorkflowCommand<TestState>.Create(update: initialState);
		var result = await compiled.InvokeAsync(command, config);

		Assert.Single(messageService.CreateParentIds);
		Assert.Equal("user-1", messageService.CreateParentIds[0]);
		Assert.Equal("msg-1", result.Messages[^1].Id);
	}

	[Fact]
	public async Task InMemoryMessageService_CreatesNewGuid_StreamNoOp_NotifyNoOp()
	{
		var messageService = new InMemoryWorkflowMessageService();
		var config = new WorkflowRunnableConfig { Configurable = new Dictionary<string, object>() };
		var msg1 = await messageService.CreateAssistantMessageAsync(config);
		var msg2 = await messageService.CreateAssistantMessageAsync(config, content: "x");

		Assert.NotNull(msg1.Id);
		Assert.NotEqual(Guid.Empty, Guid.Parse(msg1.Id));
		Assert.NotNull(msg2.Id);
		Assert.NotEqual(msg1.Id, msg2.Id);
		Assert.True(string.IsNullOrEmpty(msg1.Content));
		Assert.Equal("x", msg2.Content);

		await messageService.StreamChunkAsync(config, "any-id", "chunk");
		await messageService.NotifyStreamEndAsync(config, "any-id", "full");
	}

	[Fact]
	public async Task Controller_SetsInterceptorFromConfig_NodeGetsMessageServiceFromContainer()
	{
		var fakeMessageService = new FakeMessageService();
		var registry = new WorkflowRegistry();
		var checkpointer = MemoryCheckpointer();
		var logger = NullLogger<WorkflowController>.Instance;

		var graph = new WorkflowGraph<TestState>()
			.AddNode("n", async (state, context, _, config) =>
			{
				var ms = context.Container!.GetRequiredService<IWorkflowMessageService>();
				var msg = await ms.CreateAssistantMessageAsync(config, "");
				state.Messages.Add(msg);
				msg.Content = "ok";
				await ms.NotifyStreamEndAsync(config, msg.Id, msg.Content);
				return WorkflowCommand<TestState>.Create(gotoNode: WorkflowEdges.End, update: state);
			})
			.AddEdge(WorkflowEdges.Start, "n")
			.AddEdge("n", WorkflowEdges.End);

		var declaration = new WorkflowDeclaration<TestState>
		{
			Meta = new WorkflowMeta
			{
				Id = "msg-svc-test",
				Name = "Message Service Test",
				Description = "",
				Version = "1.0"
			},
			Workflow = graph
		};
		registry.Register(declaration);

		using var sp = new ServiceCollection()
			.AddSingleton<ICheckpointSaverFactory>(checkpointer)
			.AddSingleton(registry)
			.AddSingleton<IWorkflowMessageService>(fakeMessageService)
			.AddLogging()
			.BuildServiceProvider();

		var controller = new WorkflowController(registry, logger, sp);

		var result = await controller.ExecuteAsync<TestState>(new WorkflowControllerExecuteConfig
		{
			WorkflowType = "msg-svc-test",
			ThreadId = "ctrl-1"
		});

		Assert.True(result.WorkflowCompleted);
		Assert.Single(fakeMessageService.CreateParentIds);
		Assert.Single(fakeMessageService.NotifyStreamEndCalls);
		Assert.Single(result.Messages);
		Assert.Equal(fakeMessageService.CreatedMessageIds[0], result.Messages[0].Id);
	}

	[Fact]
	public async Task Controller_WhenMessageServiceRegistered_NodeResolvesFromContainer()
	{
		var messageService = new InMemoryWorkflowMessageService();
		var registry = new WorkflowRegistry();
		var checkpointer = MemoryCheckpointer();
		var logger = NullLogger<WorkflowController>.Instance;

		var graph = new WorkflowGraph<TestState>()
			.AddNode("n", async (state, context, _, config) =>
			{
				var ms = context.Container!.GetRequiredService<IWorkflowMessageService>();
				var msg = await ms.CreateAssistantMessageAsync(config, "");
				state.Messages.Add(msg);
				msg.Content = "from-container";
				await ms.NotifyStreamEndAsync(config, msg.Id, msg.Content);
				return WorkflowCommand<TestState>.Create(gotoNode: WorkflowEdges.End, update: state);
			})
			.AddEdge(WorkflowEdges.Start, "n")
			.AddEdge("n", WorkflowEdges.End);

		var declaration = new WorkflowDeclaration<TestState>
		{
			Meta = new WorkflowMeta
			{
				Id = "container-msg-test",
				Name = "Container Message Test",
				Description = "",
				Version = "1.0"
			},
			Workflow = graph
		};
		registry.Register(declaration);

		using var sp = new ServiceCollection()
			.AddSingleton<ICheckpointSaverFactory>(checkpointer)
			.AddSingleton(registry)
			.AddSingleton<IWorkflowMessageService>(messageService)
			.AddLogging()
			.BuildServiceProvider();

		var controller = new WorkflowController(registry, logger, sp);

		var result = await controller.ExecuteAsync<TestState>(new WorkflowControllerExecuteConfig
		{
			WorkflowType = "container-msg-test",
			ThreadId = "ctrl-2"
		});

		Assert.True(result.WorkflowCompleted);
		Assert.Single(result.Messages);
		Assert.NotNull(result.Messages[0].Id);
		Assert.Equal("from-container", (result.Messages[0] as AIMessage)?.Content);
	}

	/// <summary>
	/// Simple in-memory implementation for tests that do not need real persistence or streaming.
	/// </summary>
	private sealed class InMemoryWorkflowMessageService : IWorkflowMessageService
	{
		public Task<AIMessage> CreateAssistantMessageAsync(WorkflowRunnableConfig config, string content = "", CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new AIMessage { Id = Guid.NewGuid().ToString(), Content = content });
		}

		public Task StreamChunkAsync(WorkflowRunnableConfig config, string messageId, string chunk, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task NotifyStreamEndAsync(WorkflowRunnableConfig config, string messageId, string? fullContent = null, string[]? options = null, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task<AIMessage> CreateErrorMessageAsync(WorkflowRunnableConfig config, string errorType, string? errorDetails, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new AIMessage { Id = Guid.NewGuid().ToString(), Content = "" });
		}
	}
}
