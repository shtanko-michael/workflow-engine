namespace WorkflowEngine.Tests.UI.Backend.LLM;

/// <summary>
/// Generic LLM provider client: execute and execute with structured output.
/// </summary>
public interface ILLMProviderClient
{
    /// <summary>
    /// Execute chat completion and return plain text content.
    /// </summary>
    Task<LLMResponse> ExecuteAsync(
        LLMRequest request,
        string? model = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute chat completion and deserialize response as JSON into T.
    /// </summary>
    Task<LLMResponse<TOutput>> ExecuteWithStructuredOutputAsync<TOutput>(
        LLMRequest request,
        string? model = null,
        CancellationToken cancellationToken = default)
        where TOutput : class;
}

/// <summary>
/// LLM response with deserialized structured output.
/// </summary>
public sealed class LLMResponse<TOutput> where TOutput : class
{
    public string Content { get; set; } = "";
    public string Model { get; set; } = "";
    public TOutput? Output { get; set; }
}
