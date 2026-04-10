using System.Text.Json;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat.Support;

/// <summary>
/// Tool that returns current task stack snapshot with aggregate counters.
/// </summary>
public static class GetTaskStackStateTool
{
    public static string Execute(TaskSupportState state)
    {
        var tasks = state.TaskStackSnapshot
            .Select(x => new
            {
                x.TaskId,
                x.TaskType,
                x.Status,
                updatedAt = x.UpdatedAt
            })
            .ToArray();

        var byStatus = tasks
            .GroupBy(x => x.Status, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var payload = new
        {
            total = tasks.Length,
            completed = byStatus.TryGetValue("Completed", out var completed) ? completed : 0,
            suspended = byStatus.TryGetValue("Suspended", out var suspended) ? suspended : 0,
            active = byStatus.TryGetValue("Active", out var active) ? active : 0,
            cancelled = byStatus.TryGetValue("Cancelled", out var cancelled) ? cancelled : 0,
            tasks
        };

        return JsonSerializer.Serialize(payload);
    }
}
