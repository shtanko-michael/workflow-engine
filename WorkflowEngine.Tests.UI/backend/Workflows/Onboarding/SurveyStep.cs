using WorkflowEngine.Core.Commands;
using WorkflowEngine.Core.Execution;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.LLM;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;

/// <summary>
/// Step 2: Run survey until all data (job, sphere, employees) is collected. Uses structured output; LLM composes questions.
/// </summary>
public static class SurveyStep
{
    public static async Task<WorkflowCommand<OnboardingState>> Execute(
        OnboardingState state,
        WorkflowRunnableContext context,
        Func<Exception, WorkflowCommand<OnboardingState>> errorHandler,
        WorkflowRunnableConfig config,
        IServiceScopeFactory scopeFactory)
    {
        var request = new LLMRequest
        {
            Messages = new List<LLMMessage>
            {
                new() { Role = "system", Content = OnboardingConstants.SurveySystemPrompt }
            }
        };

        foreach (var m in state.Messages)
        {
            switch (m)
            {
                case HumanMessage h:
                    request.Messages.Add(new LLMMessage { Role = "user", Content = h.Content ?? "" });
                    break;
                case AIMessage a:
                    request.Messages.Add(new LLMMessage { Role = "assistant", Content = a.Content ?? "" });
                    break;
            }
        }

        var message = await context.Gateway.CreateAssistantMessageAsync(config, "", CancellationToken.None);
        state.Messages.Add(message);

        var lastStreamedQuestionLength = new[] { 0 };
        Func<string, Task> onAccumulatedRaw = async (accumulated) =>
        {
            var question = PartialJsonHelper.ExtractStringValue(accumulated, "questionToUser");
            if (question.Length <= lastStreamedQuestionLength[0]) return;
            var delta = question.Substring(lastStreamedQuestionLength[0]);
            lastStreamedQuestionLength[0] = question.Length;
            await context.Gateway.StreamChunkAsync(config, message.Id, delta).ConfigureAwait(false);
        };

        using var scope = scopeFactory.CreateScope();
        var llm = scope.ServiceProvider.GetRequiredService<ILLMProviderClient>();
        var response = await llm.ExecuteStreamWithStructuredOutputAsync<OnboardingStructuredOutput>(
            request, onTextChunk: null, onPartialOutput: null, onAccumulatedRaw, model: null, CancellationToken.None).ConfigureAwait(false);

        var output = response.Output;

        if (output == null)
            throw new InvalidOperationException("No ai data");

        var questionToUser = output.QuestionToUser?.Trim() ?? "";
        message.Content = questionToUser;
        message.Options = output.OptionsToUser?.Length > 0 ? output.OptionsToUser : null;
        await context.Gateway.NotifyStreamEndAsync(config, message.Id, message.Content, message.Options);

        if (output.Completed && !string.IsNullOrWhiteSpace(output.Job) && !string.IsNullOrWhiteSpace(output.Sphere) && output.Employees.HasValue)
        {
            state.OnboardingJob = output.Job;
            state.OnboardingSphere = output.Sphere;
            state.OnboardingEmployees = output.Employees.Value;
            return WorkflowCommand<OnboardingState>.Create(
                gotoNode: "thankYou",
                update: state);
        }

        return WorkflowCommand<OnboardingState>.Create(
            gotoNode: WorkflowEdges.AskHuman,
            update: state);
    }
}

/// <summary>
/// Extracts a string value for a given key from possibly incomplete JSON (e.g. while streaming).
/// Used to stream questionToUser to the frontend as the LLM generates the JSON.
/// </summary>
internal static class PartialJsonHelper
{
    /// <summary>
    /// Finds the key "key" in the JSON and returns the current value of that string field,
    /// even if the JSON is incomplete (e.g. the closing quote is not yet received).
    /// Handles escaped quotes and common escape sequences inside the string value.
    /// </summary>
    public static string ExtractStringValue(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return "";

        var keyPattern = "\"" + key + "\"";
        var keyIndex = json.IndexOf(keyPattern, StringComparison.Ordinal);
        if (keyIndex < 0) return "";

        var afterKey = keyIndex + keyPattern.Length;
        while (afterKey < json.Length && char.IsWhiteSpace(json[afterKey])) afterKey++;
        if (afterKey >= json.Length || json[afterKey] != ':') return "";

        afterKey++;
        while (afterKey < json.Length && char.IsWhiteSpace(json[afterKey])) afterKey++;
        if (afterKey >= json.Length || json[afterKey] != '"') return "";

        var valueStart = afterKey + 1;
        var sb = new System.Text.StringBuilder();
        var i = valueStart;
        while (i < json.Length)
        {
            var c = json[i];
            if (c == '"')
                return sb.ToString();
            if (c == '\\')
            {
                i++;
                if (i >= json.Length) return sb.ToString();
                var next = json[i];
                if (next == 'u')
                {
                    sb.Append(ParseUtf16Escape(json, i, out var nextI));
                    i = nextI;
                }
                else
                {
                    sb.Append(next switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        '/' => '/',
                        'b' => '\b',
                        'f' => '\f',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => next
                    });
                    i++;
                }
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static char ParseUtf16Escape(string json, int at, out int newIndex)
    {
        newIndex = at;
        if (at + 5 > json.Length) return '\uFFFD';
        if (json[at] != 'u') return '\uFFFD';
        var hex = json.Substring(at + 1, 4);
        newIndex = at + 5;
        if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
            return (char)code;
        return '\uFFFD';
    }
}
