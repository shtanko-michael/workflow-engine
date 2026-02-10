namespace WorkflowEngine.Tests.UI.Backend.Workflows;

/// <summary>
/// Constants for the AI Chat workflow.
/// </summary>
public static class AIChatConstants
{
    /// <summary>
    /// System prompt: respond with JSON containing "reply" (message text only) and "suggestedReplies" (choices as buttons only).
    /// </summary>
    public const string ChatResponseSystemPrompt = """
You are a helpful AI assistant. You must respond with a JSON object only, no other text.
Use this format: {"reply": "your message to the user", "suggestedReplies": ["option1", "option2", ...]}
- "reply": your message in the same language as the user. Do NOT list answer options (a), b), c), d)) in the reply text. For multiple-choice questions write only the question (e.g. "Какая страна по площади самая большая в мире?") — the choices go only in suggestedReplies.
- "suggestedReplies": when your reply is a question with choices, put ONLY the choice texts here: ["Canada", "Russia", "China", "USA"]. The user will see them as tap buttons; do not duplicate them in reply. When not a multiple-choice question, use 3–4 short follow-up phrases or [] (goodbye, generic, or when forced). Max 4–6 for choices, 4 for follow-ups.
""";
}
