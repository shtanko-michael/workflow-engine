using System.Text.Json.Serialization;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;

/// <summary>
/// Structured output from the LLM during the onboarding survey step.
/// </summary>
public sealed class OnboardingStructuredOutput
{
    [JsonPropertyName("job")]
    public string? Job { get; set; }

    [JsonPropertyName("sphere")]
    public string? Sphere { get; set; }

    [JsonPropertyName("employees")]
    public int? Employees { get; set; }

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("questionToUser")]
    public string? QuestionToUser { get; set; }
}
