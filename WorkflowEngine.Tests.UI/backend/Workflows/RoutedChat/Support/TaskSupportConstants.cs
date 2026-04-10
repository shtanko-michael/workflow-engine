namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat.Support;

/// <summary>
/// Constants and prompts for support task workflow.
/// </summary>
public static class TaskSupportConstants
{
    public const string GetTaskStackStateToolName = "get_task_stack_state";

    public const string ToolCallingSystemPrompt = """
You are a helpful assistant in a workflow app.
Answer concisely in the same language as the user.
Use tools whenever user asks about task statuses, counts, progress, active/suspended/completed/cancelled tasks.
When tool results are available, treat them as source of truth.
Do not invent counts or statuses not present in tool result.
""";
}
