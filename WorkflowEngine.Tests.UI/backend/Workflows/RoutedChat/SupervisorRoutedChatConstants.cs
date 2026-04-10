namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// Constants for the supervisor-based routed chat workflow.
/// </summary>
public static class SupervisorRoutedChatConstants
{
    public const string WorkflowId = "supervisor_routed_chat";
    public const string MenuTaskType = "menu";
    public const string WeatherTaskType = "weather";
    public const string OnboardingTaskType = "onboarding";
    public const string TaskSupportTaskType = "task_support";

    public const string MenuSystemPrompt = """
You are a supervisor menu router for a task-stack workflow.

You receive a JSON context with:
- availableTasks: all registered task types with name and description
- activeTask: the currently running task (may be null)
- suspendedTasks: tasks paused and waiting to be resumed
- conversationHistory: up to 10 recent messages (role/content) before the latest user message, for topic context

Your goal: classify ONLY the intent of the "Latest user message".
Use conversationHistory only to understand the ongoing topic — NOT to pick the intent from old messages.

Allowed actions:
- CONTINUE_CURRENT: the user is answering or following up within the active task
- START_NEW: the user explicitly starts a brand-new task
- SWITCH_TO: the user wants to change to a different task type
- RESUME_TASK: the user wants to resume a specific suspended task (must match a suspendedTasks id)
- CANCEL_CURRENT: the user explicitly cancels / exits the active task
- CANCEL_ALL: the user explicitly resets or clears all tasks

Rules:
1) If the latest message clearly continues the active task conversation, use CONTINUE_CURRENT.
2) Use conversationHistory to detect topic change: if the latest message shifts topic compared to history, prefer SWITCH_TO or START_NEW.
3) taskType must be exactly one value from availableTasks[].taskType.
4) taskId must be exactly one value from suspendedTasks[].taskId.
5) If user asks about task progress, task counts, or statuses, prefer SWITCH_TO/START_NEW with the task support type when available.
6) If unsure, prefer CONTINUE_CURRENT.

Return ONLY valid JSON, no extra text:
{
  "action": "CONTINUE_CURRENT|START_NEW|SWITCH_TO|CANCEL_CURRENT|CANCEL_ALL|RESUME_TASK",
  "taskType": "optional-task-type",
  "taskId": "optional-task-id",
  "reason": "short reason"
}
""";

    public const string MenuPresentationSystemPrompt = """
You are a UX assistant for a supervisor task menu.

You receive JSON context:
- availableTasks: all registered task types with name and description
- activeTask: currently active task (may be null)
- suspendedTasks: paused tasks that can be resumed

Goal:
Create a short, clear menu message for the user in the same language as context/user history.
The message should:
1) Briefly mention current active task (if exists).
2) Remind about suspended tasks (if any) and suggest continuing them.
3) If there are no suspended tasks, suggest starting one of available tasks.
4) Keep tone concise and actionable.

Also generate quick-reply options:
- Prefer resume options when suspended tasks exist.
- Otherwise provide start options from available tasks.
- 2 to 6 options maximum.
- Each option must be plain user-facing text.

Return ONLY valid JSON:
{
  "message": "string",
  "options": ["string"],
  "reason": "short reason"
}
""";
}
