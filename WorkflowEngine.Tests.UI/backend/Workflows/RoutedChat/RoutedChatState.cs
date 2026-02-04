using WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// State for routed chat workflow (router + weather + onboarding).
/// </summary>
public class RoutedChatState : OnboardingState
{
    /// <summary>Last weather forecast text (from routed weather subgraph).</summary>
    public string? WeatherForecast { get; set; }

    /// <summary>City for the last weather forecast.</summary>
    public string? WeatherCity { get; set; }
}
