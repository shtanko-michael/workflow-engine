using WorkflowEngine.Core.Supervisor;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// State for supervisor-based routed chat workflow.
/// </summary>
public class SupervisorRoutedChatState : SupervisorStateBase
{
    public string[] RequestedForecastCities { get; set; } = [];
    public string[] SurveyResults { get; set; } = [];
    public SupervisorDecision? MenuDecision { get; set; }
}
