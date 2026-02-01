namespace WorkflowEngine.Core.State;

/// <summary>
/// Base class for state annotations
/// </summary>
public abstract class StateAnnotation<TState> : IStateAnnotation<TState> where TState : class
{
    /// <summary>
    /// Creates a default instance of the state
    /// </summary>
    public abstract TState CreateDefault();
    
    /// <summary>
    /// Validates the state instance
    /// </summary>
    public virtual void Validate(TState state) { }
    
    /// <summary>
    /// Creates a root state annotation with a factory function
    /// </summary>
    public static StateAnnotation<TState> Root(Func<TState> factory)
    {
        return new RootStateAnnotation<TState>(factory);
    }
}

/// <summary>
/// Internal implementation of root state annotation
/// </summary>
internal class RootStateAnnotation<TState> : StateAnnotation<TState> where TState : class
{
    private readonly Func<TState> _factory;
    
    public RootStateAnnotation(Func<TState> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }
    
    public override TState CreateDefault()
    {
        return _factory();
    }
}
