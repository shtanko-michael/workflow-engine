using System.Collections.Concurrent;
using System.Text.Json;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Persistence;

namespace WorkflowEngine.Persistence.Memory;

/// <summary>
/// In-memory implementation of checkpoint saver (for development)
/// </summary>
public class MemoryCheckpointSaver : ICheckpointSaver
{
    private readonly ConcurrentDictionary<string, CheckpointTuple> _checkpoints = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, object>> _blobs = new();
    private readonly JsonSerializerOptions _jsonOptions;
    
    public MemoryCheckpointSaver()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };
    }
    
    public Task<CheckpointTuple?> GetAsync(WorkflowRunnableConfig config)
    {
        var key = GetCheckpointKey(config);
        
        if (_checkpoints.TryGetValue(key, out var checkpoint))
        {
            return Task.FromResult<CheckpointTuple?>(checkpoint);
        }
        
        return Task.FromResult<CheckpointTuple?>(null);
    }
    
    public Task<WorkflowRunnableConfig> PutAsync(
        WorkflowRunnableConfig config,
        Checkpoint checkpoint,
        object metadata,
        Dictionary<string, string> newVersions)
    {
        var key = GetCheckpointKey(config);
        var checkpointId = checkpoint.Id;
        
        // Store checkpoint; ParentConfig points to parent (use ParentCheckpointId, not current checkpoint_id)
        var tuple = new CheckpointTuple
        {
            Config = new WorkflowRunnableConfig
            {
                Configurable = new Dictionary<string, object>(config.Configurable)
                {
                    ["checkpoint_id"] = checkpointId
                },
                Context = config.Context
            },
            Checkpoint = checkpoint,
            Metadata = metadata,
            ParentConfig = !string.IsNullOrEmpty(config.ParentCheckpointId)
                ? new WorkflowRunnableConfig
                {
                    Configurable = new Dictionary<string, object>(config.Configurable)
                    {
                        ["checkpoint_id"] = config.ParentCheckpointId
                    },
                    Context = config.Context
                }
                : null
        };
        
        _checkpoints[key] = tuple;
        
        // Store blobs
        foreach (var (channel, version) in newVersions)
        {
            if (checkpoint.ChannelValues.TryGetValue(channel, out var value))
            {
                var blobKey = GetBlobKey(config, channel, version);
                if (!_blobs.ContainsKey(blobKey))
                {
                    _blobs[blobKey] = new Dictionary<string, object> { [channel] = value };
                }
            }
        }
        
        return Task.FromResult(tuple.Config);
    }
    
    public async IAsyncEnumerable<CheckpointTuple> ListAsync(
        WorkflowRunnableConfig config,
        CheckpointListOptions? options = null)
    {
        var threadId = config.Configurable.TryGetValue("thread_id", out var tid) ? tid?.ToString() : null;
        if (string.IsNullOrEmpty(threadId))
            yield break;
            
        var matchingCheckpoints = _checkpoints.Values
            .Where(c => c.Config.Configurable.TryGetValue("thread_id", out var ctid) && ctid?.ToString() == threadId)
            .OrderByDescending(c => c.Config.Configurable.TryGetValue("checkpoint_id", out var cid) ? cid?.ToString() : "")
            .ToList();
        
        if (options?.Limit.HasValue == true)
        {
            matchingCheckpoints = matchingCheckpoints.Take(options.Limit.Value).ToList();
        }
        
        foreach (var checkpoint in matchingCheckpoints)
        {
            yield return checkpoint;
        }
    }
    
    public Task SetupAsync()
    {
        // No setup needed for in-memory storage
        return Task.CompletedTask;
    }
    
    private string GetCheckpointKey(WorkflowRunnableConfig config)
    {
        var threadId = config.Configurable.TryGetValue("thread_id", out var tid) ? tid?.ToString() : "";
        var checkpointNs = config.Configurable.TryGetValue("checkpoint_ns", out var ns) ? ns?.ToString() ?? "" : "";
        var checkpointId = config.Configurable.TryGetValue("checkpoint_id", out var cid) ? cid?.ToString() : "latest";
        return $"{threadId}:{checkpointNs}:{checkpointId}";
    }
    
    private string GetBlobKey(WorkflowRunnableConfig config, string channel, string version)
    {
        var threadId = config.Configurable.TryGetValue("thread_id", out var tid) ? tid?.ToString() : "";
        var checkpointNs = config.Configurable.TryGetValue("checkpoint_ns", out var ns) ? ns?.ToString() ?? "" : "";
        return $"{threadId}:{checkpointNs}:{channel}:{version}";
    }
    
}
