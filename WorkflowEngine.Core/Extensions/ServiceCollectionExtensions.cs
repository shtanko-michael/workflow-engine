using Microsoft.Extensions.DependencyInjection;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Registry;

namespace WorkflowEngine.Core.Extensions;

/// <summary>
/// Extension methods for service collection
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds workflow engine services
    /// </summary>
    public static IServiceCollection AddWorkflowEngine(this IServiceCollection services)
    {
        services.AddSingleton<WorkflowRegistry>();
        services.AddScoped<WorkflowController>();
        return services;
    }
}
