using WorkflowEngine.Core.State;

namespace WorkflowEngine.Examples.Onboarding;

/// <summary>
/// State for onboarding workflow
/// </summary>
public class OnboardingState : WorkflowStateBase
{
    public int ProgressPercent { get; set; }
}
