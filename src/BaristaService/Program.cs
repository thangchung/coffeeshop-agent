using System.IdentityModel.Tokens.Jwt;
using A2A;
using A2A.AspNetCore;
using BaristaService.Agents;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(
                options => {
                    builder.Configuration.Bind("AzureAd", options);

                    options.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = CustomTokenValidated,
                        OnAuthenticationFailed = CustomAuthenticationFailed
                    };
                },
                options => builder.Configuration.Bind("AzureAd", options));

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("BaristaOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(ClaimConstants.Scope, "CoffeeShop.Barista.ReadWrite");
    });
});

builder.Services.AddHttpContextAccessor();

// Register TaskManager as singleton
builder.Services.AddScoped<ITaskManager>(provider =>
{
    var taskManager = new TaskManager();
    var logger = provider.GetRequiredService<ILogger<BaristaAgent>>();
    var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
    var agent = new BaristaAgent(httpContextAccessor, builder.Configuration, logger);
    agent.Attach(taskManager);
    return taskManager;
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

using var scope = app.Services.CreateAsyncScope();
var taskManager = scope.ServiceProvider.GetRequiredService<ITaskManager>();

// Map A2A endpoints
app.MapA2A(taskManager, "/").RequireAuthorization("BaristaOnly");
app.MapHttpA2A(taskManager, "/").RequireAuthorization("BaristaOnly");
app.MapWellKnownAgentCard(taskManager, "/").AllowAnonymous();

app.MapDefaultEndpoints();

app.Run();

async Task CustomTokenValidated(TokenValidatedContext context)
{
    // Custom logic upon successful token validation
    await Task.CompletedTask;
}

async Task CustomAuthenticationFailed(AuthenticationFailedContext context)
{
    // Custom logic upon authentication failure
    await Task.CompletedTask;
}
