namespace WorkflowEngine.Tests.UI.Backend.Models;

public record DialogDto(
    string Id,
    string Title,
    string ThreadId,
    string WorkflowType,
    string? LastCheckpointId,
    string? LastInterruptRequestId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record MessageDto(
    string Id,
    string DialogId,
    string Role,
    string Content,
    DateTimeOffset CreatedAt,
    string? RequestId);

public record MessageVersionDto(
    string Id,
    string MessageId,
    string Content,
    string CheckpointId,
    DateTimeOffset CreatedAt);

public record MessageWithVersionsDto(
    string MessageId, // Version ID (for backward compatibility with frontend)
    string Role,
    string ActiveVersionId,
    string Content,
    int CurrentVersionIndex,
    int TotalVersions,
    List<MessageVersionDto> Versions,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string>? Options = null);

public record CreateDialogRequest(string? Title, string? WorkflowId);

public record SendMessageRequest(string Content, string ThreadId, string CheckpointId, string? RequestId);

public record EditMessageRequest(string VersionId, string Content);

public record SwitchVersionRequest(string VersionId);
