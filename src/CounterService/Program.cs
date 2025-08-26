using A2A;
using A2A.AspNetCore;
using CounterService.Agents;
using Microsoft.SemanticKernel;

var builder = WebApplication.CreateBuilder(args);

AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);

var chatModelId = builder.Configuration.GetConnectionString("chatModelId");
if (string.IsNullOrEmpty(chatModelId))
{
    throw new ArgumentNullException(nameof(chatModelId), "The chatModelId connection string cannot be null or empty.");
}

var endpoint = builder.Configuration.GetConnectionString("endpoint");
if (string.IsNullOrEmpty(endpoint))
{
    throw new ArgumentNullException(nameof(endpoint), "The endpoint connection string cannot be null or empty.");
}

var apiKey = builder.Configuration.GetConnectionString("apiKey");
if (string.IsNullOrEmpty(apiKey))
{
    throw new ArgumentNullException(nameof(apiKey), "The apiKey connection string cannot be null or empty.");
}

// Register TaskManager as singleton
builder.Services.AddSingleton<ITaskManager>(provider =>
{
    var taskManager = new TaskManager();
    var config = provider.GetRequiredService<IConfiguration>();
    var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
    var logger = provider.GetRequiredService<ILogger<CounterAgent>>();
    var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
    var kernel = provider.GetRequiredService<Kernel>();
    var agent = new CounterAgent(kernel, config, clientFactory, httpContextAccessor, logger);
    agent.Attach(taskManager);
    return taskManager;
});

builder.Services.AddHttpContextAccessor();

var uri = new Uri("http://localhost:11434");
var httpClient = new HttpClient
{
    BaseAddress = uri
};
var kernelBuilder = builder.Services.AddKernel()
    // .AddOllamaChatCompletion("llama3.2", httpClient);
    //.AddOpenAIChatCompletion(
    //    modelId: "openai/gpt-5-nano",
    //    apiKey: apiKey,
    //    endpoint: new Uri("https://models.github.ai/inference"));
    .AddAzureOpenAIChatCompletion(chatModelId, endpoint, apiKey);

kernelBuilder.Services.ConfigureHttpClientDefaults(c =>
{
    c.AddStandardResilienceHandler();
    c.ConfigureHttpClient((sp, httpClient) =>
    {
        httpClient.Timeout = TimeSpan.FromMinutes(5); // Set timeout to 5 minutes
    });
});

builder.AddServiceDefaults();

var app = builder.Build();

// Get the configured TaskManager for A2A endpoints
var taskManager = app.Services.GetRequiredService<ITaskManager>();

// Map A2A endpoints
app.MapA2A(taskManager, "/submit-order");
app.MapHttpA2A(taskManager, "/submit-order");

app.MapDefaultEndpoints();

app.Run();
