using Microsoft.Extensions.DependencyInjection;
using WorkflowEngine.Core.Persistence;
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
