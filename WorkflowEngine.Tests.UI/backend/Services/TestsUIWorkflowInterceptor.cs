using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Contracts;

namespace WorkflowEngine.Tests.UI.Backend.Services;

/// <summary>
/// Interceptor that creates error messages when the workflow hits an error. Works with any state type.
/// </summary>
public sealed class TestsUIWorkflowInterceptor<TState> : IWorkflowRunInterceptor<TState> where TState : WorkflowStateBase
{
	private readonly IWorkflowMessageService _messageService;

	public TestsUIWorkflowInterceptor(IWorkflowMessageService messageService)
	{
		_messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
	}

	public Task OnGraphStartedAsync(WorkflowRunnableConfig config, TState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
	public Task OnGraphCompletedAsync(WorkflowRunnableConfig config, TState state, WorkflowGraphCompletionReason reason, CancellationToken cancellationToken = default)
	{
		if (reason == WorkflowGraphCompletionReason.Error && !string.IsNullOrEmpty(state.ErrorName))
			return _messageService.CreateErrorMessageAsync(config, state.ErrorName, state.ErrorMessage, cancellationToken);
		return Task.CompletedTask;
	}
	public Task OnSubgraphStartedAsync(string nodeName, WorkflowRunnableConfig config, TState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
	public Task OnSubgraphCompletedAsync(string nodeName, WorkflowRunnableConfig config, TState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
	public Task OnNodeStartedAsync(string nodeName, WorkflowRunnableConfig config, TState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
	public Task OnNodeCompletedAsync(string nodeName, WorkflowRunnableConfig config, TState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
	public Task OnErrorAsync(WorkflowRunnableConfig config, TState state, CancellationToken cancellationToken = default)
		=> _messageService.CreateErrorMessageAsync(config, state.ErrorName ?? "Error", state.ErrorMessage, cancellationToken);
	public Task OnInterruptAsync(WorkflowRunnableConfig config, TState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
