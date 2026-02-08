using WorkflowEngine.Core.State;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat.Weather;

/// <summary>
/// State for the weather subgraph (city and forecast result).
/// </summary>
public class WeatherSubState : WorkflowStateBase
{
    public string? City { get; set; }
    public string? Forecast { get; set; }
}
