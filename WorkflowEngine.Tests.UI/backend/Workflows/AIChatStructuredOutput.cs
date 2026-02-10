using System.Text.Json.Serialization;

namespace WorkflowEngine.Tests.UI.Backend.Workflows;

/// <summary>
/// Single structured response from the AI Chat LLM: main reply + optional suggested follow-ups (3-4 when they make sense).
/// </summary>
public sealed class AIChatStructuredOutput
{
    /// <summary>
    /// The assistant's main reply to the user.
    /// </summary>
    [JsonPropertyName("reply")]
    public string? Reply { get; set; }

    /// <summary>
    /// Optional 3-4 short follow-up options the user might say. Empty when suggestions do not add value.
    /// </summary>
    [JsonPropertyName("suggestedReplies")]
    public string[]? SuggestedReplies { get; set; }
}
