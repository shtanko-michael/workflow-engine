using System.Text.Json.Serialization;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// Structured output for rendering supervisor menu text and quick options.
/// </summary>
public sealed class SupervisorMenuPresentationStructuredOutput
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("options")]
    public string[]? Options { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
