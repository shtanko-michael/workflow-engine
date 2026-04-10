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
    /// Execute completion with optional tool-calling. Provider decides when to call tools and returns final answer.
    /// </summary>
    Task<LLMResponse> ExecuteWithToolsAsync(
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

    /// <summary>
    /// Execute chat completion with streaming; invokes onChunk for each text delta and returns full response at the end.
    /// </summary>
    Task<LLMResponse> ExecuteStreamAsync(
        LLMRequest request,
        Func<string, Task>? onChunk,
        string? model = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute chat completion with streaming and structured JSON output. Streams text deltas via onTextChunk,
    /// invokes onAccumulatedRaw with the full accumulated JSON on each chunk (for partial field extraction),
    /// and invokes onPartialOutput whenever the accumulated JSON can be deserialized to TOutput (partial or final).
    /// </summary>
    Task<LLMResponse<TOutput>> ExecuteStreamWithStructuredOutputAsync<TOutput>(
        LLMRequest request,
        Func<string, Task>? onTextChunk,
        Func<TOutput?, Task>? onPartialOutput,
        Func<string, Task>? onAccumulatedRaw = null,
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
