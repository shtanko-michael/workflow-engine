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

    public const string MenuSystemPrompt = """
You are a supervisor menu router for a task-stack workflow.
Decide the next action based on:
- the user message
- all available task types
- current active task
- suspended tasks

Allowed actions:
- CONTINUE_CURRENT
- START_NEW
- SWITCH_TO
- CANCEL_CURRENT
- CANCEL_ALL
- RESUME_TASK

Rules:
1) Use CONTINUE_CURRENT when the user is answering or continuing the active task.
2) Use START_NEW when the user clearly starts a new task and does not ask to switch to an existing one.
3) Use SWITCH_TO when the user asks to switch context by task type.
4) Use RESUME_TASK only when user references a known suspended task id.
5) Use CANCEL_CURRENT for explicit cancel/exit of current task.
6) Use CANCEL_ALL for explicit reset/clear-all intent.
7) taskType must be one from availableTaskTypes.
8) taskId must be one from suspendedTasks ids.

Return ONLY JSON:
{
  "action": "CONTINUE_CURRENT|START_NEW|SWITCH_TO|CANCEL_CURRENT|CANCEL_ALL|RESUME_TASK",
  "taskType": "optional-task-type",
  "taskId": "optional-task-id",
  "reason": "short reason"
}
""";
}
