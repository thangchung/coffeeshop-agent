using System.IdentityModel.Tokens.Jwt;
using A2A;
using A2A.AspNetCore;
using CounterService.Agents;
using ServiceDefaults.Extensions;
using Microsoft.SemanticKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;

var builder = WebApplication.CreateBuilder(args);

// Configure Azure AD authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(options =>
            {
                builder.Configuration.Bind("AzureAd", options);
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateLifetime = true;
                options.TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(5);
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "role";
            }, options =>
            {
                builder.Configuration.Bind("AzureAd", options);
            });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("http://schemas.microsoft.com/identity/claims/scope", "admin");
    });
});

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
    var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
    
    var agent = new CounterAgent(
        configService,
        clientManager,
        validationService,
        orderParsingService,
        messageService,
        httpContextAccessor,
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

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
if (app.Environment.IsDevelopment())
{
    IdentityModelEventSource.ShowPII = true;
}

app.UseAuthentication();
app.UseAuthorization();

// Get the configured TaskManager for A2A endpoints
var taskManager = app.Services.GetRequiredService<ITaskManager>();

// Map A2A endpoints with authentication requirement
app.MapA2A(taskManager, "/submit-order").RequireAuthorization("AdminOnly");
app.MapHttpA2A(taskManager, "/submit-order").RequireAuthorization("AdminOnly");

app.MapDefaultEndpoints();

app.Run();
