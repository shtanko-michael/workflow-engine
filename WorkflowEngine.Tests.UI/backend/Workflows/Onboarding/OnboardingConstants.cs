namespace WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;

/// <summary>
/// Constants for the onboarding workflow.
/// </summary>
public static class OnboardingConstants
{
    public const string WorkflowId = "onboarding";

    /// <summary>
    /// Prompt for the LLM to generate the welcome message (step 1).
    /// </summary>
    public const string WelcomePrompt = """
Write a short, friendly welcome message inviting the user to complete a quick onboarding so we can tailor the system to their needs. Do not ask any questions yet; just invite them. One or two sentences.
""";

    /// <summary>
    /// Description for the LLM of what to collect from the user (no fixed questions; LLM composes them).
    /// </summary>
    public const string SurveySystemPrompt = """
You are conducting a short onboarding survey. You must collect the following from the user (compose your own questions):

1. Job: the user's job title or main role (e.g. "Engineer", "Manager").
2. Sphere: the industry or domain they work in (e.g. "Fintech", "Healthcare").
3. Employees: how many people work in their company or team (a number).

Ask one question at a time in a friendly way. For each question, provide 3–5 short "optionsToUser" as suggested answers the user can click (e.g. job titles, industries, or size ranges). When you have gathered all three pieces of information, set "completed" to true and leave "questionToUser" empty or with a short confirmation; "optionsToUser" can be empty then.

You must respond ONLY with a JSON object in this exact format (no markdown, no extra text):
{"questionToUser": "<string>", "optionsToUser": ["<option1>", "<option2>", ...], "completed": <boolean>, "job": "<string or null>", "sphere": "<string or null>", "employees": <number or null>}
""";
}
