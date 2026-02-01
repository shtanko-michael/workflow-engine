using Microsoft.Extensions.Logging;
using WorkflowEngine.Core.Graph;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.State;

namespace WorkflowEngine.Core.Registry;

/// <summary>
/// Registry for workflow declarations
/// </summary>
public class WorkflowRegistry
{
    private readonly Dictionary<string, WorkflowRegistryItem> _workflows = new();
    private readonly ILogger<WorkflowRegistry>? _logger;
    
    public WorkflowRegistry(ILogger<WorkflowRegistry>? logger = null)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Registers a workflow declaration
    /// </summary>
    public void Register<TState>(WorkflowDeclaration<TState> declaration) where TState : WorkflowStateBase
    {
        if (declaration == null)
            throw new ArgumentNullException(nameof(declaration));
        if (string.IsNullOrWhiteSpace(declaration.Meta?.Id))
            throw new ArgumentException("Workflow meta ID is required", nameof(declaration));
            
        _workflows[declaration.Meta.Id] = new WorkflowRegistryItem(declaration);
        _logger?.LogInformation("Registered workflow: {WorkflowId}", declaration.Meta.Id);
    }
    
    /// <summary>
    /// Gets a workflow by ID
    /// </summary>
    public WorkflowRegistryItem? Get(string workflowId)
    {
        return _workflows.TryGetValue(workflowId, out var item) ? item : null;
    }
    
    /// <summary>
    /// Gets all registered workflow IDs
    /// </summary>
    public IEnumerable<string> GetAllIds()
    {
        return _workflows.Keys;
    }
}

/// <summary>
/// Registry item for a workflow
/// </summary>
public class WorkflowRegistryItem
{
    private readonly object _declaration;
    private readonly Type _stateType;
    
    internal WorkflowRegistryItem(object declaration)
    {
        _declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));
        
        // Extract state type from declaration
        var declarationType = declaration.GetType();
        if (declarationType.IsGenericType && declarationType.GetGenericTypeDefinition() == typeof(WorkflowDeclaration<>))
        {
            _stateType = declarationType.GetGenericArguments()[0];
        }
        else
        {
            throw new ArgumentException("Invalid workflow declaration type", nameof(declaration));
        }
    }
    
    /// <summary>
    /// Compiles the workflow graph with a checkpointer
    /// </summary>
    public CompiledWorkflowGraph<TState> Compile<TState>(ICheckpointSaver checkpointer, ILogger? logger = null) where TState : WorkflowStateBase
    {
        if (checkpointer == null)
            throw new ArgumentNullException(nameof(checkpointer));
            
        if (typeof(TState) != _stateType)
        {
            throw new ArgumentException($"State type {typeof(TState)} does not match workflow state type {_stateType}", nameof(TState));
        }
        
        // Get workflow graph from declaration
        var workflowProperty = _declaration.GetType().GetProperty("Workflow");
        if (workflowProperty == null)
            throw new InvalidOperationException("Workflow property not found in declaration");
            
        var workflow = workflowProperty.GetValue(_declaration);
        if (workflow == null)
            throw new InvalidOperationException("Workflow is null");
        
        // Compile using reflection
        var compileMethod = workflow.GetType().GetMethod("Compile");
        if (compileMethod == null)
            throw new InvalidOperationException("Compile method not found");
            
        var compiledGraph = compileMethod.Invoke(workflow, new object[] { checkpointer, logger });
        if (compiledGraph == null)
            throw new InvalidOperationException("Failed to compile workflow graph");
            
        return (CompiledWorkflowGraph<TState>)compiledGraph;
    }
}
