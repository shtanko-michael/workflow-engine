namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat.Weather;

/// <summary>
/// Constants for the weather subgraph.
/// </summary>
public static class WeatherConstants
{
    /// <summary>
    /// Prompt when we need to ask the user for a city (no city in state yet).
    /// </summary>
    public const string AskCityPrompt = "Which city would you like a weather forecast for? Reply with the city name.";

    /// <summary>
    /// System prompt for the LLM to generate a mock weather forecast for a given city.
    /// </summary>
    public const string ForecastSystemPrompt = """
You are a weather assistant. Given a city name, generate a short, plausible mock weather forecast (temperature, conditions, maybe a tip). Keep it to 1-2 sentences. Do not use real APIs; invent a believable forecast.
""";
}
