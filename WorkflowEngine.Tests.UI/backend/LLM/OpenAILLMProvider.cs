using System.ClientModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace WorkflowEngine.Tests.UI.Backend.LLM;

/// <summary>
/// OpenAI .NET implementation of LLM provider (Execute + ExecuteWithStructuredOutput).
/// </summary>
public sealed class OpenAILLMProvider : ILLMProviderClient
{
    private readonly OpenAIOptions _opts;
    private readonly ChatClient _client;
    private readonly string _defaultModel;
    private readonly ILogger<OpenAILLMProvider>? _logger;

    public OpenAILLMProvider(
        IOptions<OpenAIOptions> options,
        ILogger<OpenAILLMProvider>? logger = null)
    {
        _opts = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _defaultModel = _opts.DefaultModel ?? "gpt-4o-mini";
        _logger = logger;
        _client = CreateClient(_defaultModel);
    }

    private ChatClient CreateClient(string model)
    {
        var apiKey = _opts.ApiKey ?? "";
        if (!string.IsNullOrWhiteSpace(_opts.BaseUrl) && Uri.TryCreate(_opts.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            var credential = new ApiKeyCredential(apiKey);
            var options = new OpenAIClientOptions { Endpoint = baseUri };
            return new ChatClient(model, credential, options);
        }
        return new ChatClient(model, apiKey);
    }

    private ChatClient GetClient(string model)
    {
        var apiKey = _opts.ApiKey ?? "";
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("LLM:OpenAI:ApiKey is required in appsettings for AI chat.");
        return model == _defaultModel ? _client : CreateClient(model);
    }

    public async Task<LLMResponse> ExecuteAsync(
        LLMRequest request,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        var messages = ToChatMessages(request.Messages);
        var m = model ?? _defaultModel;
        var client = GetClient(m);
        return await CompleteAsync(client, messages, m, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LLMResponse<TOutput>> ExecuteWithStructuredOutputAsync<TOutput>(
        LLMRequest request,
        string? model = null,
        CancellationToken cancellationToken = default)
        where TOutput : class
    {
        var messages = ToChatMessages(request.Messages);
        var m = model ?? _defaultModel;
        var client = GetClient(m);

        var schema = JsonSchemaHelper.GetJsonSchemaForType<TOutput>();
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "response",
                jsonSchema: BinaryData.FromBytes(schema),
                jsonSchemaIsStrict: true)
        };

        var completion = await client.CompleteChatAsync(messages, options, cancellationToken).ConfigureAwait(false);
        var content = completion.Value.Content.Count > 0 ? completion.Value.Content[0].Text : "";
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

    private static async Task<LLMResponse> CompleteAsync(
        ChatClient client,
        List<ChatMessage> messages,
        string model,
        CancellationToken cancellationToken)
    {
        var completion = await client.CompleteChatAsync(messages, cancellationToken: cancellationToken).ConfigureAwait(false);
        var content = completion.Value.Content.Count > 0 ? completion.Value.Content[0].Text : "";
        return new LLMResponse { Content = content, Model = model };
    }

    private static List<ChatMessage> ToChatMessages(List<LLMMessage> messages)
    {
        var list = new List<ChatMessage>();
        foreach (var m in messages)
        {
            var role = m.Role?.ToLowerInvariant() ?? "user";
            list.Add(role switch
            {
                "system" => new SystemChatMessage(m.Content ?? ""),
                "assistant" => new AssistantChatMessage(m.Content ?? ""),
                _ => new UserChatMessage(m.Content ?? "")
            });
        }
        return list;
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
/// Builds minimal JSON schema for a type (for structured output).
/// </summary>
internal static class JsonSchemaHelper
{
    public static byte[] GetJsonSchemaForType<T>() where T : class
    {
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
            ["required"] = Array.Empty<string>(),
            ["additionalProperties"] = false
        };
        return JsonSerializer.SerializeToUtf8Bytes(schema);
    }
}
