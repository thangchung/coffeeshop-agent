using A2A;
using A2A.AspNetCore;
using KitchenService.Agents;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();

// Register TaskManager as singleton
builder.Services.AddSingleton<ITaskManager>(provider =>
{
    var taskManager = new TaskManager();
    var logger = provider.GetRequiredService<ILogger<KitchenAgent>>();
    var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
    var agent = new KitchenAgent(httpContextAccessor, logger);
    agent.Attach(taskManager);
    return taskManager;
});

builder.AddServiceDefaults();

var app = builder.Build();

var taskManager = app.Services.GetRequiredService<ITaskManager>();

// Map A2A endpoints
app.MapA2A(taskManager, "/");
app.MapHttpA2A(taskManager, "/");

app.MapDefaultEndpoints();

app.Run();
