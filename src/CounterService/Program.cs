using System.ClientModel;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Authentication;
using System.Security.Claims;
using Azure.AI.OpenAI;
using CounterService.Agents;
using CounterService.AuthZ;
using CounterService.Workflows;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;
using ModelContextProtocol.Client;
using OpenAI.Chat;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddHttpContextAccessor();

builder.Services.AddOpenApi();

builder.Services.AddAGUI();

builder.Services.AddScoped<IAuthZService, StuffAuthZService>();

//builder.AddWorkflow("order-workflow", async (sp, key) =>
//{
//    var provider = sp.GetRequiredService<IServiceProvider>();
//    var agent = provider.GetService<CounterChatAgent>();
//    var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
//    var logger = provider.GetRequiredService<ILogger<CounterChatAgent>>();
//    var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
//    var tokenAcquisition = provider.GetRequiredService<ITokenAcquisition>();

//    var chatClient = new AzureOpenAIClient(
//          new Uri(endpoint),
//          new ApiKeyCredential(apiKey))
//            .GetChatClient(chatModelId);

//    var validator = new ValidatorExecutor(chatClient, null);
//    var start = new SplitExecutor(agent!);
//    var baristaExecutor = new BaristaExecuter(null);
//    var kitchenExecutor = new KitchenExecuter(null);
//    var aggregation = new AggregationExecutor();
//    var uncertainHandler = new HandleUncertainExecutor(null!);

//    var workflow = new WorkflowBuilder(validator)
//        .AddSwitch(validator, switchBuilder => switchBuilder
//            .AddCase(GetValidCondition(true), start)
//            .AddCase(GetValidCondition(false), uncertainHandler)
//        )
//        .AddFanOutEdge(start, [baristaExecutor, kitchenExecutor])
//        .AddFanInEdge([baristaExecutor, kitchenExecutor], aggregation)
//        .WithOutputFrom(aggregation, uncertainHandler)
//        .Build();

//    Func<object?, bool> GetValidCondition(bool valid) => detectionResult => detectionResult is ValidResponse res && res.Valid == valid;

//    return workflow;
//}).AddAsAIAgent();

// devui
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

builder.AddServiceDefaults();

var app = builder.Build();

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

if (app.Environment.IsDevelopment())
{
    IdentityModelEventSource.ShowPII = true;

    app.MapOpenApi();
    app.MapScalarApiReference();
    app.MapDevUI();
}

app.UseAuthentication();
app.UseAuthorization();

using var scope = app.Services.CreateAsyncScope();
var provider = scope.ServiceProvider;
var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
var logger = provider.GetRequiredService<ILogger<CounterChatAgent>>();
var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
var tokenAcquisition = provider.GetRequiredService<ITokenAcquisition>();
var chatClient = new AzureOpenAIClient(
          new Uri(endpoint),
          new ApiKeyCredential(apiKey))
            .GetChatClient(chatModelId);

var agent = new CounterChatAgent(chatClient, builder.Configuration, clientFactory, httpContextAccessor, tokenAcquisition, logger);

app.MapAGUI("/", agent);

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

