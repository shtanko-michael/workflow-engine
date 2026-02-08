using Microsoft.Extensions.DependencyInjection;

namespace WorkflowEngine.Core.Persistence;

public interface ICheckpointSaverFactory
{
    Task<ICheckpointSaver> Build();
}

public class CheckpointSaverFactory : ICheckpointSaverFactory
{
    private readonly IServiceProvider _serviceProvider;
    public CheckpointSaverFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }
    public Task<ICheckpointSaver> Build()
    {
        // Resolve from the current scope to avoid disposing DbContext prematurely.
        var checkpointer = _serviceProvider.GetRequiredService<ICheckpointSaver>();
        return Task.FromResult(checkpointer);
    }
}