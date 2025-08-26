using A2A;
using A2A.AspNetCore;
using CounterService.Agents;
using ServiceDefaults.Extensions;
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

// Add agent services following SOLID principles
builder.Services.AddCounterAgentServices();

// Register TaskManager as singleton with dependency injection
builder.Services.AddSingleton<ITaskManager>(provider =>
{
    var taskManager = new TaskManager();
    var logger = provider.GetRequiredService<ILogger<CounterAgent>>();
    
    // Use dependency injection to create the CounterAgent with all required services
    var configService = provider.GetRequiredService<ServiceDefaults.Configuration.IAgentConfigurationService>();
    var clientManager = provider.GetRequiredService<ServiceDefaults.Services.IA2AClientManager>();
    var validationService = provider.GetRequiredService<ServiceDefaults.Services.IInputValidationService>();
    var orderParsingService = provider.GetRequiredService<ServiceDefaults.Services.IOrderParsingService>();
    var messageService = provider.GetRequiredService<ServiceDefaults.Services.IA2AMessageService>();
    
    var agent = new CounterAgent(
        configService,
        clientManager,
        validationService,
        orderParsingService,
        messageService,
        logger);
        
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
