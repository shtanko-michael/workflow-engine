using Microsoft.Extensions.Logging;
using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Exceptions;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Nodes;

/// <summary>
/// Helper for creating workflow nodes with context and error handling
/// </summary>
public static class WithContextNode
{
    /// <summary>
    /// Wraps a node function with context and error handling
    /// </summary>
    public static WorkflowNode<TState> Wrap<TState>(
        string name,
        Func<TState, WorkflowRunnableContext, Func<Exception, WorkflowCommand<TState>>, WorkflowRunnableConfig, Task<WorkflowCommand<TState>>> nodeFunc)
        where TState : WorkflowStateBase
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Node name cannot be null or empty", nameof(name));
		ArgumentNullException.ThrowIfNull(nodeFunc);

		return async (state, context, errorHandler, config) =>
        {
            try
            {
                // Enhance context with node name (preserve Interceptor so host can be notified)
                var enhancedContext = new WorkflowRunnableContext
                {
                    Controller = context.Controller,
                    Container = context.Container,
                    Interceptor = context.Interceptor,
                    Tracking = new ClientTrackingContext
                    {
                        Comment = context.Tracking.Comment,
                        UserId = context.Tracking.UserId,
                        AccountId = context.Tracking.AccountId,
                        ThreadId = context.Tracking.ThreadId,
                        UserNumber = context.Tracking.UserNumber,
                        WorkspaceId = context.Tracking.WorkspaceId,
                        WorkspaceItemId = context.Tracking.WorkspaceItemId,
                        ChatMessageId = context.Tracking.ChatMessageId,
                        ArtifactId = context.Tracking.ArtifactId,
                        WorkflowType = context.Tracking.WorkflowType,
                        NodeName = name,
                        IpAddress = context.Tracking.IpAddress,
                        Tool = context.Tracking.Tool
                    },
                    Logger = context.Logger
                };
                
                return await nodeFunc(state, enhancedContext, errorHandler, config);
            }
			catch (SubgraphWorkflowInterruptException)
			{
				// Re-throw interrupt exceptions
				throw;
			}
			catch (WorkflowInterruptException)
            {
                // Re-throw interrupt exceptions
                throw;
            }
            catch (Exception ex) {
                context.Logger.LogError(ex, "Error in node {NodeName}", name);
                return errorHandler(ex);
            }
        };
    }
}
