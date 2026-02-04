using WorkflowEngine.Core.State;

namespace WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;

/// <summary>
/// State for onboarding workflow.
/// </summary>
public class OnboardingState : WorkflowStateBase
{
    /// <summary>Collected during onboarding survey (job title/role).</summary>
    public string? OnboardingJob { get; set; }

    /// <summary>Collected during onboarding survey (industry/domain).</summary>
    public string? OnboardingSphere { get; set; }

    /// <summary>Collected during onboarding survey (company/team size).</summary>
    public int? OnboardingEmployees { get; set; }
}
