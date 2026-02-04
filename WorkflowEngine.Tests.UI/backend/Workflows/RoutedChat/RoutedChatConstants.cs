namespace WorkflowEngine.Tests.UI.Backend.Workflows.RoutedChat;

/// <summary>
/// Constants for the routed chat workflow (router with weather and onboarding subgraphs).
/// </summary>
public static class RoutedChatConstants
{
    public const string WorkflowId = "routed_chat";

    /// <summary>
    /// Prompt for the LLM to generate the welcome message listing available functions.
    /// </summary>
    public const string WelcomePrompt = """
You are a friendly assistant. Write a short welcome message that lists your two main functions:
1. Weather forecast — the user can ask for weather in any city.
2. Onboarding survey — a short survey to tailor the system to the user.
""";

    /// <summary>
    /// Prompt when the router needs to ask for user input (no prior user message).
    /// </summary>
    public const string RouterPromptNoInput = """
Ask the user what they would like to do: get a weather forecast for a city, or complete the onboarding survey. One short sentence.
""";

    /// <summary>
    /// System prompt for the router LLM: classify user intent into weather, onboarding, or none.
    /// </summary>
    public const string RouterSystemPrompt = """
You are a router. Based on the user's last message, decide which route to take.

Routes:
- "weather": user wants a weather forecast (e.g. "weather in London", "what's the weather in Paris", "forecast Moscow").
- "onboarding": user wants to do the onboarding/survey (e.g. "onboarding", "survey", "sign me up", "I want to complete the survey").
- "none": the message does not clearly match weather or onboarding.

Respond ONLY with a JSON object: {"route": "weather" | "onboarding" | "none"}
""";
}
