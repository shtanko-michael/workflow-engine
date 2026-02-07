using WorkflowEngine.Core.State;
using WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// State for routed chat workflow (router + weather + onboarding).
/// </summary>
public class RoutedChatState : WorkflowStateBase
{

    /// <summary>Cities for which a forecast was requested (across all weather subgraph runs).</summary>
    public string[] RequestedForecastCities { get; set; } = [];

    /// <summary>Survey results from completed onboarding runs (one string per run).</summary>
    public string[] SurveyResults { get; set; } = [];
}
