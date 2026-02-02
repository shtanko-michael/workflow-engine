using Microsoft.EntityFrameworkCore;
using WorkflowEngine.Core.Extensions;
using WorkflowEngine.Core.Registry;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Persistence.Postgres;
using WorkflowEngine.Tests.UI.Backend.Data;
using WorkflowEngine.Tests.UI.Backend.Data.Repositories;
using WorkflowEngine.Tests.UI.Backend.Hubs;
using WorkflowEngine.Tests.UI.Backend.LLM;
using WorkflowEngine.Tests.UI.Backend.Services;
using WorkflowEngine.Tests.UI.Backend.Workflows;
using WorkflowEngine.Tests.UI.Backend.Workflows.Onboarding;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("ui", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Application database context (dialogs, messages)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Checkpointer"),
        x => x.CommandTimeout(30)));

// Checkpoint database context (workflow persistence)
builder.Services.AddWorkflowEngine();
builder.Services.AddPostgresCheckpointer(
    builder.Configuration.GetConnectionString("Checkpointer") ?? string.Empty);

builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection("LLM:OpenAI"));
builder.Services.AddSingleton<ILLMProviderClient, OpenAILLMProvider>();

builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();
// InMemoryChatStore kept for backward compatibility with old API (v1)
builder.Services.AddSingleton<InMemoryChatStore>();
builder.Services.AddScoped<ChatWorkflowService>();
builder.Services.AddScoped<ChatWorkflowServiceNew>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // Setup checkpoint persistence
    var checkpointer = scope.ServiceProvider.GetService<ICheckpointSaver>();
    if (checkpointer is PostgresCheckpointSaver postgresSaver)
    {
        await postgresSaver.SetupAsync();
    }

    // Apply EF Core migrations for ApplicationDbContext
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();

    var registry = scope.ServiceProvider.GetRequiredService<WorkflowRegistry>();
    registry.Register(DemoChatWorkflow.Build());
    var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
    registry.Register(AIChatWorkflow.Build(scopeFactory));
    registry.Register(OnboardingWorkflow.Build(scopeFactory));
}

app.UseCors("ui");
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapGet("/", () => "WorkflowEngine Tests UI backend");

app.Run();
