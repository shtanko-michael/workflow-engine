# Workflow Engine for C#

A workflow engine for C# (.NET 9) inspired by the [LangGraph](https://github.com/langchain-ai/langgraph) approach. Define graphs of nodes and edges, persist checkpoints, and resume execution with human-in-the-loop support.

## Features

- **Graph-based workflows** — Nodes, edges, and conditional routing
- **Checkpoint persistence** — In-memory (dev) or PostgreSQL
- **Human-in-the-loop** — Interrupt at any node and resume with user input
- **State serialization** — State is checkpointed and restored across runs
- **DI-friendly** — Register workflows and checkpointer via `ServiceCollection`
- **AI-agnostic core** — Engine does not depend on an AI client; plug in your own when needed

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

For PostgreSQL persistence:

- PostgreSQL 12+ (or use in-memory checkpointer for development)

## Solution structure

| Project | Description |
|--------|-------------|
| **WorkflowEngine.Core** | Core engine: graph, state, checkpoints, execution |
| **WorkflowEngine.Persistence.Memory** | In-memory checkpoint store |
| **WorkflowEngine.Persistence.Postgres** | PostgreSQL checkpoint store |
| **WorkflowEngine.Bundle** | Single project referencing all libraries (convenience/NuGet) |
| **WorkflowEngine.Examples** | Example workflows (e.g. Onboarding) |
| **WorkflowEngine.Tests** | Core tests |
| **WorkflowEngine.Persistence.Postgres.Tests** | Postgres checkpointer tests |
| **WorkflowEngine.Tests.UI** | Sample chat UI (backend + frontend) |

## Installation

### Reference from source

Clone the repo and add a project reference:

```xml
<ItemGroup>
  <ProjectReference Include="path\to\WorkflowEngine.Bundle\WorkflowEngine.Bundle.csproj" />
</ItemGroup>
```

Or reference individual projects (e.g. Core + Persistence.Memory).

### NuGet (when published)

```bash
dotnet add package WorkflowEngine.Bundle
```

## Quick start

### 1. Register services

```csharp
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddWorkflowEngine();
services.AddMemoryCheckpointer(); // or AddPostgresCheckpointer(connectionString)

var serviceProvider = services.BuildServiceProvider();
```

### 2. Register a workflow

```csharp
var registry = serviceProvider.GetRequiredService<WorkflowRegistry>();
var onboardingWorkflow = OnboardingWorkflow.Create();
registry.Register(onboardingWorkflow);
```

### 3. Run the workflow

```csharp
var controller = serviceProvider.GetRequiredService<WorkflowController>();

var config = new WorkflowControllerExecuteConfig
{
    WorkflowType = "onboarding",
    ThreadId = "test-thread-1",
    UserId = "test-user-1",
    WorkspaceId = "test-workspace-1",
    CheckpointerConfig = new CheckpointerConfig { Mode = CheckpointerMode.Memory },
    InitialState = new OnboardingState { ProgressPercent = 0 }
};

var result = await controller.ExecuteAsync<OnboardingState>(config);
```

## PostgreSQL persistence

Use the Postgres checkpointer for production or shared state:

```csharp
services.AddPostgresCheckpointer(
    "Host=localhost;Database=workflow;Username=user;Password=pass");
```

Tables (`checkpoints`, `checkpoint_blobs`, `checkpoint_migrations`) are created automatically on first use via `SetupAsync()`.

## Defining a custom workflow

**1. Define state (inherits `WorkflowStateBase`):**

```csharp
public class MyState : WorkflowStateBase
{
    public string CustomField { get; set; }
}
```

**2. Create a node (e.g. with `WithContextNode` for logging/context):**

```csharp
return WithContextNode.Wrap<MyState>("myNode", (state, ctx, errorHandler, config) =>
{
    state.CustomField = "updated";
    return Task.FromResult(WorkflowCommand<MyState>.Create(
        gotoNode: "nextNode",
        update: state
    ));
});
```

**3. Build the graph:**

```csharp
var graph = new WorkflowGraph<MyState>()
    .AddNode("myNode", MyNode.Create())
    .AddNode("askHuman", AskHumanNode.Create<MyState>())
    .AddEdge(WorkflowEdges.Start, "myNode");
```

**4. Compile and register:**

```csharp
var declaration = new WorkflowDeclaration<MyState>
{
    Meta = new WorkflowMeta { Id = "my_workflow", Name = "My Workflow", ... },
    Workflow = graph
};
registry.Register(declaration);
```

## Human-in-the-loop

Use `AskHumanNode` to interrupt and wait for user input. The workflow throws `WorkflowInterruptException`; capture `InterruptRequestId` and the checkpoint, then resume later with a `HumanMessage` and the same checkpoint id.

## Tests

```bash
dotnet test WorkflowEngine.sln
```

Postgres tests require a running PostgreSQL instance (see connection string in `PostgresCheckpointSaverTests.cs`).

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
