using System.IdentityModel.Tokens.Jwt;
using System.Security.Authentication;
using System.Security.Claims;
using A2A;
using A2A.AspNetCore;
using CounterService.Agents;
using CounterService.AuthZ;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;
using Microsoft.SemanticKernel;
using Scalar.AspNetCore;

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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(options =>
    {
        builder.Configuration.Bind("AzureAd", options);

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = CustomTokenValidated,
            OnAuthenticationFailed = CustomAuthenticationFailed
        };
    }, options => builder.Configuration.Bind("AzureAd", options))
    .EnableTokenAcquisitionToCallDownstreamApi(options => { })
    .AddInMemoryTokenCaches();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CounterOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(ClaimConstants.Scope, "CoffeeShop.Counter.ReadWrite");
    });
});

builder.Services.AddScoped<ITaskManager>(provider =>
{
    var taskManager = new TaskManager();
    var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
    var logger = provider.GetRequiredService<ILogger<CounterAgent>>();
    var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
    var tokenAcquisition = provider.GetRequiredService<ITokenAcquisition>();
    var kernel = provider.GetRequiredService<Kernel>();
    var agent = new CounterAgent(kernel, builder.Configuration, clientFactory, httpContextAccessor, tokenAcquisition, logger);
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
    // .AddOllamaChatCompletion("gpt-oss:20b", httpClient);
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

builder.Services.AddOpenApi();

builder.Services.AddScoped<IAuthZService, StuffAuthZService>();

builder.AddServiceDefaults();

var app = builder.Build();

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
if (app.Environment.IsDevelopment())
{
    IdentityModelEventSource.ShowPII = true;

    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();

// Get the configured TaskManager for A2A endpoints
using var scope = app.Services.CreateAsyncScope();
var taskManager = scope.ServiceProvider.GetRequiredService<ITaskManager>();

// Map A2A endpoints
app.MapA2A(taskManager, "/").RequireAuthorization("CounterOnly");
app.MapHttpA2A(taskManager, "/").RequireAuthorization("CounterOnly");
app.MapWellKnownAgentCard(taskManager, "/").AllowAnonymous();

app.MapDefaultEndpoints();

app.Run();

async Task CustomTokenValidated(TokenValidatedContext context)
{
    var authZ = context.HttpContext.RequestServices.GetRequiredService<IAuthZService>();

    // enrich and claim transformation for user roles
    var userEmail = context.Principal?.FindFirst(ClaimTypes.Upn)?.Value;
    var roleIdentity = new ClaimsIdentity("RoleClaims");
    roleIdentity.AddClaim(new Claim(ClaimTypes.Role, authZ.MapUserToRole(userEmail ?? throw new AuthenticationException())));
    context.Principal?.AddIdentity(roleIdentity);

    await Task.CompletedTask;
}

async Task CustomAuthenticationFailed(AuthenticationFailedContext context)
{
    // Custom logic upon authentication failure
    await Task.CompletedTask;
}
