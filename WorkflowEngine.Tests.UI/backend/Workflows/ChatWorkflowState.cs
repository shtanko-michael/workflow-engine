using WorkflowEngine.Core.State;

namespace WorkflowEngine.Tests.UI.Backend.Workflows;

/// <summary>
/// Shared state for chat workflows (Demo, AI, Onboarding).
/// </summary>
public class ChatWorkflowState : WorkflowStateBase
{
    public string? LastUserMessage { get; set; }

    /// <summary>Collected during onboarding survey (job title/role).</summary>
    public string? OnboardingJob { get; set; }

    /// <summary>Collected during onboarding survey (industry/domain).</summary>
    public string? OnboardingSphere { get; set; }

    /// <summary>Collected during onboarding survey (company/team size).</summary>
    public int? OnboardingEmployees { get; set; }
}
