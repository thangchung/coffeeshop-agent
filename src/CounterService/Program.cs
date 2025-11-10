using System.ClientModel;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Authentication;
using System.Security.Claims;
using Azure.AI.OpenAI;
using CounterService.Agents;
using CounterService.AuthZ;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;
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

var ignoreAuth = builder.Configuration.GetValue("IgnoreAuth", false);
if (!ignoreAuth)
{
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
}

builder.Services.AddHttpContextAccessor();

builder.Services.AddOpenApi();

if (!ignoreAuth)
{
    builder.Services.AddAGUI();

    builder.Services.AddScoped<IAuthZService, StuffAuthZService>();
}

builder.AddWorkflow("order-workflow", (sp, key) =>
{
    using var scope = sp.CreateScope();
    return scope.ServiceProvider.BuildWorkflowForDevUI(key, CancellationToken.None).GetAwaiter().GetResult();
}).AddAsAIAgent();

// Register ChatClient for DevUI
builder.Services.AddSingleton<ChatClient>(sp =>
{
    return new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
        .GetChatClient(chatModelId);
});

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

    // Map OpenAI endpoints and DevUI (required for DevUI to work)
    //app.MapOpenAIResponses();
    //app.MapOpenAIConversations();

    app.MapDevUI();  // This should automatically map /v1/entities endpoint
}

if (!ignoreAuth)
{
    app.UseAuthentication();
    app.UseAuthorization();

    // Note: Don't use 'using' here - the agent needs to live for the application lifetime
    // The scope will be disposed when the application shuts down
    var agent = app.Services.BuildAIAgentForAGUI(endpoint, apiKey, chatModelId);

    app.MapAGUI("/", agent);
}

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

