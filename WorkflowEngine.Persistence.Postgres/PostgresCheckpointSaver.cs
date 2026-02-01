using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Persistence.Postgres.Entities;

namespace WorkflowEngine.Persistence.Postgres;

/// <summary>
/// PostgreSQL implementation of checkpoint saver
/// </summary>
public class PostgresCheckpointSaver : ICheckpointSaver
{
    private readonly CheckpointDbContext _dbContext;
    private readonly ILogger<PostgresCheckpointSaver> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _isSetup;
    
    public PostgresCheckpointSaver(
        CheckpointDbContext dbContext,
        ILogger<PostgresCheckpointSaver> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }
    
    public async Task SetupAsync()
    {
        if (_isSetup)
            return;
            
        try
        {
            // Check current migration version
            var currentVersion = -1;
            try
            {
                var latestMigration = await _dbContext.CheckpointMigrations
                    .OrderByDescending(m => m.Version)
                    .FirstOrDefaultAsync();
                    
                if (latestMigration != null)
                {
                    currentVersion = latestMigration.Version;
                }
            }
            catch (Exception ex) when (ex.Message.Contains("does not exist") || ex.Message.Contains("relation"))
            {
                // Table doesn't exist, start from version -1
                currentVersion = -1;
            }
            
            // Apply migrations
            var migrations = GetMigrations();
            for (int v = currentVersion + 1; v < migrations.Count; v++)
            {
                var migrationSql = migrations[v];
                await _dbContext.Database.ExecuteSqlRawAsync(migrationSql);
                _dbContext.CheckpointMigrations.Add(new CheckpointMigrationEntity { Version = v });
                await _dbContext.SaveChangesAsync();
            }
            
            _isSetup = true;
            _logger.LogInformation("PostgresCheckpointSaver setup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting up PostgresCheckpointSaver");
            throw;
        }
    }
    
    public async Task<CheckpointTuple?> GetAsync(WorkflowRunnableConfig config)
    {
        var threadId = config.Configurable.TryGetValue("thread_id", out var tid) ? tid?.ToString() : null;
        var checkpointNs = config.Configurable.TryGetValue("checkpoint_ns", out var ns) ? ns?.ToString() ?? "" : "";
        var checkpointId = config.Configurable.TryGetValue("checkpoint_id", out var cid) ? cid?.ToString() : null;
        
        if (string.IsNullOrEmpty(threadId))
            return null;
        
        CheckpointEntity? entity;
        
        if (!string.IsNullOrEmpty(checkpointId))
        {
            entity = await _dbContext.Checkpoints
                .FirstOrDefaultAsync(c => c.ThreadId == threadId && 
                                         c.CheckpointNs == checkpointNs && 
                                         c.CheckpointId == checkpointId);
        }
        else
        {
            entity = await _dbContext.Checkpoints
                .Where(c => c.ThreadId == threadId && c.CheckpointNs == checkpointNs)
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.CheckpointId)
                .FirstOrDefaultAsync();
        }
        
        if (entity == null)
            return null;
        
        // Load checkpoint
        var checkpoint = DeserializeCheckpoint(entity.CheckpointJson);
        
        // Load channel values (blobs)
        var channelValues = await LoadBlobsAsync(threadId, checkpointNs, checkpoint.ChannelVersions);
        checkpoint.ChannelValues ??= new Dictionary<string, object>();
        foreach (var kvp in channelValues)
        {
            checkpoint.ChannelValues[kvp.Key] = kvp.Value;
        }
        
        // Load metadata
        var metadata = JsonSerializer.Deserialize<object>(entity.MetadataJson, _jsonOptions) ?? new { };
        
        return new CheckpointTuple
        {
            Config = new WorkflowRunnableConfig
            {
                Configurable = new Dictionary<string, object>(config.Configurable)
                {
                    ["checkpoint_id"] = entity.CheckpointId
                },
                Context = config.Context
            },
            Checkpoint = checkpoint,
            Metadata = metadata,
            ParentConfig = !string.IsNullOrEmpty(entity.ParentCheckpointId)
                ? new WorkflowRunnableConfig
                {
                    Configurable = new Dictionary<string, object>(config.Configurable)
                    {
                        ["checkpoint_id"] = entity.ParentCheckpointId
                    },
                    Context = config.Context
                }
                : null,
        };
    }
    
    public async Task<WorkflowRunnableConfig> PutAsync(
        WorkflowRunnableConfig config,
        Checkpoint checkpoint,
        object metadata,
        Dictionary<string, string> newVersions)
    {
        var threadId = config.Configurable.TryGetValue("thread_id", out var tid) ? tid?.ToString() : null;
        var checkpointNs = config.Configurable.TryGetValue("checkpoint_ns", out var ns) ? ns?.ToString() ?? "" : "";
        var parentCheckpointId = config.Configurable.TryGetValue("checkpoint_id", out var cid) ? cid?.ToString() : null;
        
        if (string.IsNullOrEmpty(threadId))
            throw new ArgumentException("thread_id is required", nameof(config));
        
        var checkpointId = checkpoint.Id;
        var checkpointJson = SerializeCheckpoint(checkpoint, out var blobChannels);
        var metadataJson = JsonSerializer.Serialize(metadata, _jsonOptions);
        
        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            // Save blobs (only complex values)
            foreach (var (channel, version) in newVersions)
            {
                if (blobChannels.Contains(channel) &&
                    checkpoint.ChannelValues.TryGetValue(channel, out var value))
                {
                    var (type, blob) = SerializeValue(value);
                    await UpsertBlobAsync(threadId, checkpointNs, channel, version, type, blob);
                }
            }
            
            // Save checkpoint
            await UpsertCheckpointAsync(
                threadId,
                checkpointNs,
                checkpointId,
                parentCheckpointId,
                checkpointJson,
                metadataJson);
            await transaction.CommitAsync();
            
            return new WorkflowRunnableConfig
            {
                Configurable = new Dictionary<string, object>(config.Configurable)
                {
                    ["checkpoint_id"] = checkpointId
                },
                Context = config.Context
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    
    public async IAsyncEnumerable<CheckpointTuple> ListAsync(
        WorkflowRunnableConfig config,
        CheckpointListOptions? options = null)
    {
        var threadId = config.Configurable.TryGetValue("thread_id", out var tid) ? tid?.ToString() : null;
        if (string.IsNullOrEmpty(threadId))
            yield break;
        
        var checkpointNs = config.Configurable.TryGetValue("checkpoint_ns", out var ns) ? ns?.ToString() ?? "" : "";
        
        var query = _dbContext.Checkpoints
            .Where(c => c.ThreadId == threadId && c.CheckpointNs == checkpointNs);
        
        if (options?.Before != null)
        {
            var beforeCheckpointId = options.Before.Configurable.TryGetValue("checkpoint_id", out var bid) ? bid?.ToString() : null;
            if (!string.IsNullOrEmpty(beforeCheckpointId))
            {
                query = query.Where(c => string.Compare(c.CheckpointId, beforeCheckpointId) < 0);
            }
        }
        
        query = query.OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.CheckpointId);
        
        if (options?.Limit.HasValue == true)
        {
            query = query.Take(options.Limit.Value);
        }
        
        var entities = await query.ToListAsync();
        
        foreach (var entity in entities)
        {
            var checkpoint = DeserializeCheckpoint(entity.CheckpointJson);
            var channelValues = await LoadBlobsAsync(threadId, checkpointNs, checkpoint.ChannelVersions);
            checkpoint.ChannelValues ??= new Dictionary<string, object>();
            foreach (var kvp in channelValues)
            {
                checkpoint.ChannelValues[kvp.Key] = kvp.Value;
            }
            var metadata = JsonSerializer.Deserialize<object>(entity.MetadataJson, _jsonOptions) ?? new { };
            
            yield return new CheckpointTuple
            {
                Config = new WorkflowRunnableConfig
                {
                    Configurable = new Dictionary<string, object>(config.Configurable)
                    {
                        ["checkpoint_id"] = entity.CheckpointId
                    },
                    Context = config.Context
                },
                Checkpoint = checkpoint,
                Metadata = metadata,
                ParentConfig = !string.IsNullOrEmpty(entity.ParentCheckpointId)
                    ? new WorkflowRunnableConfig
                    {
                        Configurable = new Dictionary<string, object>(config.Configurable)
                        {
                            ["checkpoint_id"] = entity.ParentCheckpointId
                        },
                        Context = config.Context
                    }
                    : null,
            };
        }
    }
    
    private Checkpoint DeserializeCheckpoint(string json)
    {
        return JsonSerializer.Deserialize<Checkpoint>(json, _jsonOptions) ?? new Checkpoint();
    }
    
    private string SerializeCheckpoint(
        Checkpoint checkpoint,
        out HashSet<string> blobChannels)
    {
        var inlineValues = new Dictionary<string, object>();
        blobChannels = new HashSet<string>();
        foreach (var kvp in checkpoint.ChannelValues ?? new Dictionary<string, object>())
        {
            if (IsInlineValue(kvp.Value))
            {
                inlineValues[kvp.Key] = kvp.Value;
            }
            else
            {
                blobChannels.Add(kvp.Key);
            }
        }
        
        var checkpointCopy = new Checkpoint
        {
            Id = checkpoint.Id,
            ChannelVersions = checkpoint.ChannelVersions,
            ChannelValues = inlineValues
        };
        return JsonSerializer.Serialize(checkpointCopy, _jsonOptions);
    }
    
    private async Task<Dictionary<string, object>> LoadBlobsAsync(
        string threadId,
        string checkpointNs,
        Dictionary<string, string> channelVersions)
    {
        var result = new Dictionary<string, object>();
        
        if (channelVersions.Count == 0)
            return result;
        
        var channels = channelVersions.Keys.ToList();
        var blobs = await _dbContext.CheckpointBlobs
            .Where(b => b.ThreadId == threadId &&
                       b.CheckpointNs == checkpointNs &&
                       channels.Contains(b.Channel))
            .ToListAsync();
        
        foreach (var blob in blobs)
        {
            if (blob.Blob != null && blob.Blob.Length > 0)
            {
                if (channelVersions.TryGetValue(blob.Channel, out var version) &&
                    version == blob.Version)
                {
                    var value = DeserializeValue(blob.Type, blob.Blob);
                    result[blob.Channel] = value;
                }
            }
        }
        
        return result;
    }
    
    private (string type, byte[] blob) SerializeValue(object value)
    {
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        return ("json", Encoding.UTF8.GetBytes(json));
    }
    
    private object DeserializeValue(string type, byte[] blob)
    {
        if (type == "json" || string.IsNullOrEmpty(type))
        {
            var json = Encoding.UTF8.GetString(blob);
            return JsonSerializer.Deserialize<object>(json, _jsonOptions) ?? new { };
        }
        
        throw new NotSupportedException($"Type '{type}' is not supported");
    }

    private static bool IsInlineValue(object? value)
    {
        if (value == null)
            return true;
        
        if (value is string || value is bool)
            return true;
        
        if (value is byte || value is sbyte || value is short || value is ushort ||
            value is int || value is uint || value is long || value is ulong ||
            value is float || value is double || value is decimal)
            return true;
        
        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind == JsonValueKind.Null ||
                   jsonElement.ValueKind == JsonValueKind.String ||
                   jsonElement.ValueKind == JsonValueKind.Number ||
                   jsonElement.ValueKind == JsonValueKind.True ||
                   jsonElement.ValueKind == JsonValueKind.False;
        }
        
        return false;
    }

    private Task UpsertCheckpointAsync(
        string threadId,
        string checkpointNs,
        string checkpointId,
        string? parentCheckpointId,
        string checkpointJson,
        string metadataJson)
    {
        return _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO checkpoints (thread_id, checkpoint_ns, checkpoint_id, parent_checkpoint_id, type, checkpoint, metadata)
VALUES ({threadId}, {checkpointNs}, {checkpointId}, {parentCheckpointId}, {null}, {checkpointJson}::jsonb, {metadataJson}::jsonb)
ON CONFLICT (thread_id, checkpoint_ns, checkpoint_id)
DO UPDATE SET
    parent_checkpoint_id = EXCLUDED.parent_checkpoint_id,
    type = EXCLUDED.type,
    checkpoint = EXCLUDED.checkpoint,
    metadata = EXCLUDED.metadata;");
    }

    private Task UpsertBlobAsync(
        string threadId,
        string checkpointNs,
        string channel,
        string version,
        string type,
        byte[] blob)
    {
        return _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO checkpoint_blobs (thread_id, checkpoint_ns, channel, version, type, blob)
VALUES ({threadId}, {checkpointNs}, {channel}, {version}, {type}, {blob})
ON CONFLICT (thread_id, checkpoint_ns, channel, version)
DO UPDATE SET
    type = EXCLUDED.type,
    blob = EXCLUDED.blob;");
    }


    private List<string> GetMigrations()
    {
        return new List<string>
        {
            @"CREATE TABLE IF NOT EXISTS checkpoint_migrations (
  v INTEGER PRIMARY KEY
);",
            @"CREATE TABLE IF NOT EXISTS checkpoints (
  thread_id TEXT NOT NULL,
  checkpoint_ns TEXT NOT NULL DEFAULT '',
  checkpoint_id TEXT NOT NULL,
  parent_checkpoint_id TEXT,
  type TEXT,
  checkpoint JSONB NOT NULL,
  metadata JSONB NOT NULL DEFAULT '{{}}',
  PRIMARY KEY (thread_id, checkpoint_ns, checkpoint_id)
);",
            @"CREATE TABLE IF NOT EXISTS checkpoint_blobs (
  thread_id TEXT NOT NULL,
  checkpoint_ns TEXT NOT NULL DEFAULT '',
  channel TEXT NOT NULL,
  version TEXT NOT NULL,
  type TEXT NOT NULL,
  blob BYTEA,
  PRIMARY KEY (thread_id, checkpoint_ns, channel, version)
);",
            @"ALTER TABLE checkpoint_blobs ALTER COLUMN blob DROP NOT NULL;",
            @"CREATE INDEX IF NOT EXISTS checkpoints_thread_id_idx ON checkpoints (thread_id);",
            @"ALTER TABLE checkpoints ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();",
            @"CREATE INDEX IF NOT EXISTS checkpoints_thread_ns_created_at_idx ON checkpoints (thread_id, checkpoint_ns, created_at DESC);",
            @"CREATE INDEX IF NOT EXISTS checkpoint_blobs_thread_id_idx ON checkpoint_blobs (thread_id);",
        };
    }
}
