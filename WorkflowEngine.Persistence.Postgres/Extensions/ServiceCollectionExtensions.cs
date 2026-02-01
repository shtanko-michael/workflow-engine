using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WorkflowEngine.Core.AI;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Persistence.Postgres;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Microsoft.EntityFrameworkCore;

namespace WorkflowEngine.Core.Extensions;

/// <summary>
/// Extension methods for service collection
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL checkpointer
    /// </summary>
    public static IServiceCollection AddPostgresCheckpointer(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));

        // Register DbContext using reflection - simplified approach
        // For MVP, we'll register it manually in the consuming application
        // This method just registers the saver factory

        services.AddDbContext<CheckpointDbContext>(
            options =>
            {
                options.UseNpgsql(connectionString,
                    x => {
                        x.CommandTimeout(30);
                    });
            });
        // Register PostgresCheckpointSaver factory
        // Note: DbContext must be registered separately using AddDbContext
        services.AddScoped<ICheckpointSaver>(sp =>
        {
            var dbContext = sp.GetService<CheckpointDbContext>();

            var loggerFactory = sp.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger<PostgresCheckpointSaver>();

            return new PostgresCheckpointSaver(dbContext, logger);
        });

        return services;
    }
}
