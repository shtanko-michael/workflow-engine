using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Nodes;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Examples.Onboarding.Nodes;

namespace WorkflowEngine.Examples.Onboarding;

/// <summary>
/// Onboarding workflow definition
/// </summary>
public static class OnboardingWorkflow
{
    public static WorkflowDeclaration<OnboardingState> Create()
    {
        var graph = new WorkflowGraph<OnboardingState>()
            .AddNode(WorkflowEdges.AskHuman, AskHumanNode.Create<OnboardingState>())
            .AddNode(WorkflowEdges.ErrorHandler, ErrorHandlerNode.Create<OnboardingState>())
            .AddNode("welcome", WelcomeNode.Create(), ends: new List<string> { WorkflowEdges.ErrorHandler, "survey" })
            .AddNode("survey", SurveyNode.Create(), ends: new List<string> { WorkflowEdges.AskHuman, WorkflowEdges.ErrorHandler, "complete" })
            .AddNode("complete", CompleteNode.Create(), ends: new List<string> { WorkflowEdges.ErrorHandler, "summary" })
            .AddNode("summary", SummaryNode.Create(), ends: new List<string> { WorkflowEdges.ErrorHandler })
            .AddEdge(WorkflowEdges.Start, "welcome");
        
        return new WorkflowDeclaration<OnboardingState>
        {
            Meta = new WorkflowMeta
            {
                Id = "onboarding",
                Name = "Onboarding",
                Description = "Onboarding workflow",
                Version = "1.0.0"
            },
            Workflow = graph
        };
    }
}
