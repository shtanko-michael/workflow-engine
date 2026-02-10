#pragma warning disable OPENAI001 // Response APIs are evaluation-only
using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Responses;

namespace WorkflowEngine.Tests.UI.Backend.LLM;

/// <summary>
/// OpenAI .NET implementation of LLM provider (Execute + ExecuteWithStructuredOutput).
/// </summary>
public sealed class OpenAILLMProvider : ILLMProviderClient
{
    private readonly string _apiKey;
    private readonly string? _baseUrl;
    private readonly string _defaultModel;
    private readonly ILogger<OpenAILLMProvider>? _logger;

    public OpenAILLMProvider(
        IOptions<OpenAIOptions> options,
        ILogger<OpenAILLMProvider>? logger = null)
    {
        var opts = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _apiKey = opts.ApiKey ?? "";
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("LLM:OpenAI:ApiKey is required in appsettings for AI chat.");
        _baseUrl = opts.BaseUrl;
        _defaultModel = opts.DefaultModel ?? "gpt-4o-mini";
        _logger = logger;
    }

    /// <summary>
    /// Creates OpenAIResponseClient for the given model (by analogy with OpenAIProviderClient).
    /// </summary>
    private OpenAIResponseClient CreateResponseClient(string model)
    {
        var credential = new ApiKeyCredential(_apiKey);
        OpenAIClientOptions? clientOptions = string.IsNullOrEmpty(_baseUrl) || !Uri.TryCreate(_baseUrl, UriKind.Absolute, out var baseUri)
            ? null
            : new OpenAIClientOptions { Endpoint = baseUri };
        return new OpenAIResponseClient(model, credential, clientOptions);
    }

    public async Task<LLMResponse> ExecuteAsync(
        LLMRequest request,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        var m = model ?? _defaultModel;
        var responseClient = CreateResponseClient(m);

        var options = new ResponseCreationOptions
        {
            Instructions = BuildInstructions(request),
        };

        var items = BuildResponseItems(request);

        var response = await responseClient.CreateResponseAsync(items, options, cancellationToken).ConfigureAwait(false);
        var value = response.Value;
        var content = value.GetOutputText();

        return new LLMResponse { Content = content, Model = m };
    }

    public async Task<LLMResponse> ExecuteStreamAsync(
        LLMRequest request,
        Func<string, Task>? onChunk,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        var m = model ?? _defaultModel;
        var responseClient = CreateResponseClient(m);

        var options = new ResponseCreationOptions
        {
            Instructions = BuildInstructions(request),
        };

        var items = BuildResponseItems(request);

        var fullContent = new System.Text.StringBuilder();
        await foreach (StreamingResponseUpdate update in responseClient.CreateResponseStreamingAsync(items, options, cancellationToken).ConfigureAwait(false))
        {
            if (update is StreamingResponseOutputTextDeltaUpdate textDelta && !string.IsNullOrEmpty(textDelta.Delta))
            {
                fullContent.Append(textDelta.Delta);
                if (onChunk != null)
                    await onChunk(textDelta.Delta).ConfigureAwait(false);
            }
        }

        return new LLMResponse { Content = fullContent.ToString(), Model = m };
    }

    public async Task<LLMResponse<TOutput>> ExecuteWithStructuredOutputAsync<TOutput>(
        LLMRequest request,
        string? model = null,
        CancellationToken cancellationToken = default)
        where TOutput : class
    {
        var m = model ?? _defaultModel;
        var responseClient = CreateResponseClient(m);

        var schema = JsonSchemaHelper.GetJsonSchemaForType<TOutput>();
        var schemaJson = System.Text.Encoding.UTF8.GetString(schema);
        var baseInstructions = BuildInstructions(request);
        var structuredInstructions = string.IsNullOrWhiteSpace(baseInstructions)
            ? $"Return the response in the following JSON schema: {schemaJson}"
            : $"{baseInstructions}\n\nReturn the response in the following JSON schema: {schemaJson}";

        var options = new ResponseCreationOptions
        {
            Instructions = structuredInstructions,
        };

        var items = BuildResponseItems(request);

        var response = await responseClient.CreateResponseAsync(items, options, cancellationToken).ConfigureAwait(false);
        var content = response.Value.GetOutputText();

        TOutput? output = null;
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                output = JsonSerializer.Deserialize<TOutput>(content);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to deserialize structured output to {Type}", typeof(TOutput).Name);
            }
        }

        return new LLMResponse<TOutput>
        {
            Content = content,
            Model = m,
            Output = output
        };
    }

    /// <summary>
    /// Streams response while requesting JSON matching TOutput. Accumulates chunks, tries partial deserialization
    /// on each chunk, and invokes onTextChunk (raw delta), onAccumulatedRaw (full accumulated JSON), and onPartialOutput when deserialization succeeds.
    /// </summary>
    public async Task<LLMResponse<TOutput>> ExecuteStreamWithStructuredOutputAsync<TOutput>(
        LLMRequest request,
        Func<string, Task>? onTextChunk,
        Func<TOutput?, Task>? onPartialOutput,
        Func<string, Task>? onAccumulatedRaw = null,
        string? model = null,
        CancellationToken cancellationToken = default)
        where TOutput : class
    {
        var m = model ?? _defaultModel;
        var responseClient = CreateResponseClient(m);

        var schema = JsonSchemaHelper.GetJsonSchemaForType<TOutput>();
        var schemaJson = System.Text.Encoding.UTF8.GetString(schema);
        var baseInstructions = BuildInstructions(request);
        var structuredInstructions = string.IsNullOrWhiteSpace(baseInstructions)
            ? $"Return the response in the following JSON schema: {schemaJson}"
            : $"{baseInstructions}\n\nReturn the response in the following JSON schema: {schemaJson}";

        var options = new ResponseCreationOptions
        {
            Instructions = structuredInstructions,
        };

        var items = BuildResponseItems(request);
        var fullContent = new System.Text.StringBuilder();
        TOutput? lastOutput = null;

        await foreach (StreamingResponseUpdate update in responseClient.CreateResponseStreamingAsync(items, options, cancellationToken).ConfigureAwait(false))
        {
            if (update is StreamingResponseOutputTextDeltaUpdate textDelta && !string.IsNullOrEmpty(textDelta.Delta))
            {
                fullContent.Append(textDelta.Delta);
                if (onTextChunk != null)
                    await onTextChunk(textDelta.Delta).ConfigureAwait(false);

                var accumulated = fullContent.ToString();
                if (!string.IsNullOrWhiteSpace(accumulated))
                {
                    if (onAccumulatedRaw != null)
                        await onAccumulatedRaw(accumulated).ConfigureAwait(false);

                    try
                    {
                        var parsed = JsonSerializer.Deserialize<TOutput>(accumulated);
                        if (parsed != null)
                        {
                            lastOutput = parsed;
                            if (onPartialOutput != null)
                                await onPartialOutput(parsed).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        // Incomplete JSON, ignore until we have more data
                    }
                }
            }
        }

        var content = fullContent.ToString();
        if (lastOutput == null && !string.IsNullOrWhiteSpace(content))
        {
            try
            {
                lastOutput = JsonSerializer.Deserialize<TOutput>(content);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to deserialize structured stream to {Type}", typeof(TOutput).Name);
            }
        }

        return new LLMResponse<TOutput>
        {
            Content = content,
            Model = m,
            Output = lastOutput
        };
    }

    /// <summary>
    /// Builds instructions for Response API from system messages (by analogy with BuildInstructions(LLMRequest) in OpenAIProviderClient).
    /// </summary>
    private static string BuildInstructions(LLMRequest request)
    {
        var parts = new List<string>();
        if (request?.Messages == null)
            return string.Join("\n", parts);
        foreach (var message in request.Messages)
        {
            if (message == null) continue;
            if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(message.Content))
                parts.Add(message.Content);
        }
        return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>
    /// Converts LLMRequest messages to ResponseItem list for Response API.
    /// By analogy with BuildResponseItemsAsync in OpenAIProviderClient: each message with content becomes a user message item.
    /// </summary>
    private static List<ResponseItem> BuildResponseItems(LLMRequest request)
    {
        var items = new List<ResponseItem>();
        if (request?.Messages != null)
        {
            foreach (var message in request.Messages)
            {
                if (message == null) continue;
                var parts = new List<ResponseContentPart>();
                if (!string.IsNullOrWhiteSpace(message.Content))
                    parts.Add(ResponseContentPart.CreateInputTextPart(message.Content));
                if (parts.Count > 0)
                    items.Add(ResponseItem.CreateUserMessageItem(parts));
            }
        }
        if (items.Count == 0)
            items.Add(ResponseItem.CreateUserMessageItem([ResponseContentPart.CreateInputTextPart(string.Empty)]));
        return items;
    }
}

/// <summary>
/// Options for OpenAI provider (from appsettings LLM:OpenAI).
/// </summary>
public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public string? DefaultModel { get; set; }
}

/// <summary>
/// Builds JSON schema for a type (for structured output with OpenAI).
/// </summary>
internal static class JsonSchemaHelper
{
    public static byte[] GetJsonSchemaForType<T>() where T : class
    {
        var type = typeof(T);
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var prop in type.GetProperties())
        {
            // Get JsonPropertyName attribute if present, otherwise use property name
            var jsonNameAttr = prop.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false)
                .OfType<JsonPropertyNameAttribute>()
                .FirstOrDefault();
            var jsonPropertyName = jsonNameAttr?.Name ?? prop.Name;

            var propSchema = GetPropertySchema(prop.PropertyType);
            properties[jsonPropertyName] = propSchema;

            // Add to required if property type is not nullable
            var underlyingType = Nullable.GetUnderlyingType(prop.PropertyType);
            if (underlyingType == null && prop.PropertyType.IsValueType)
            {
                required.Add(jsonPropertyName);
            }
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required.ToArray(),
            ["additionalProperties"] = false
        };

        return JsonSerializer.SerializeToUtf8Bytes(schema);
    }

    private static Dictionary<string, object> GetPropertySchema(Type propertyType)
    {
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (underlyingType == typeof(string))
            return new Dictionary<string, object> { ["type"] = "string" };
        
        if (underlyingType == typeof(int) || underlyingType == typeof(long))
            return new Dictionary<string, object> { ["type"] = "integer" };
        
        if (underlyingType == typeof(double) || underlyingType == typeof(float) || underlyingType == typeof(decimal))
            return new Dictionary<string, object> { ["type"] = "number" };
        
        if (underlyingType == typeof(bool))
            return new Dictionary<string, object> { ["type"] = "boolean" };
        
        if (underlyingType.IsArray || (underlyingType.IsGenericType && underlyingType.GetGenericTypeDefinition() == typeof(List<>)))
        {
            var elementType = underlyingType.IsArray ? underlyingType.GetElementType()! : underlyingType.GetGenericArguments()[0];
            return new Dictionary<string, object>
            {
                ["type"] = "array",
                ["items"] = GetPropertySchema(elementType)
            };
        }

        // Default to string for unknown types
        return new Dictionary<string, object> { ["type"] = "string" };
    }
}
