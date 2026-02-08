namespace WorkflowEngine.Core.Graph;

/// <summary>
/// Edge between workflow nodes
/// </summary>
public class WorkflowEdge
{
    public string From { get; set; } = string.Empty;
    public string? To { get; set; } = string.Empty;
    public Func<object, string>? Condition { get; set; }
}

/// <summary>
/// Constants for workflow edges
/// </summary>
public static class WorkflowEdges
{
    public const string Start = "__start__";
    public const string End = "__end__";
    public const string AskHuman = "__ask_human__";
    public const string ErrorHandler = "__error_handler__";
}
