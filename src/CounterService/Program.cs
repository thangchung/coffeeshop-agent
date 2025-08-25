using A2A;
using A2A.AspNetCore;
using CounterService.Agents;

var builder = WebApplication.CreateBuilder(args);

// Register TaskManager as singleton
builder.Services.AddSingleton<ITaskManager>(provider =>
{
    var taskManager = new TaskManager();
    var config = provider.GetRequiredService<IConfiguration>();
    var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
    var logger = provider.GetRequiredService<ILogger<CounterAgent>>();
    var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
    var agent = new CounterAgent(config, clientFactory, httpContextAccessor, logger);
    agent.Attach(taskManager);
    return taskManager;
});

builder.Services.AddHttpContextAccessor();

builder.AddServiceDefaults();

var app = builder.Build();

// Get the configured TaskManager for A2A endpoints
var taskManager = app.Services.GetRequiredService<ITaskManager>();

// Map A2A endpoints
app.MapA2A(taskManager, "/submit-order");
app.MapHttpA2A(taskManager, "/submit-order");

app.MapDefaultEndpoints();

app.Run();
