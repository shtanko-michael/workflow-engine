using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WorkflowEngine.Core.AI;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Persistence.Memory;

namespace WorkflowEngine.Core.Extensions;

/// <summary>
/// Extension methods for service collection
/// </summary>
public static class ServiceCollectionExtensions
{
    
    /// <summary>
    /// Adds memory checkpointer
    /// </summary>
    public static IServiceCollection AddMemoryCheckpointer(this IServiceCollection services)
    {
        services.AddSingleton<ICheckpointSaver>(new MemoryCheckpointSaver());
        return services;
    }
}
