using System.Text.Json.Serialization;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// Structured output from the router LLM: one of weather, onboarding, or none.
/// </summary>
public sealed class RouterStructuredOutput
{
    [JsonPropertyName("route")]
    public string? Route { get; set; }
}
