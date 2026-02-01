namespace WorkflowEngine.Core.State;

/// <summary>
/// Interface for defining state annotations in workflow engine
/// </summary>
public interface IStateAnnotation<TState> where TState : class
{
    /// <summary>
    /// Creates a default instance of the state
    /// </summary>
    TState CreateDefault();
    
    /// <summary>
    /// Validates the state instance
    /// </summary>
    void Validate(TState state);
}
