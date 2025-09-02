using System.IdentityModel.Tokens.Jwt;
using A2A;
using A2A.AspNetCore;
using BaristaService.Agents;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddHttpContextAccessor();

// Register TaskManager as singleton
builder.Services.AddSingleton<ITaskManager>(provider =>
{
    var taskManager = new TaskManager();
    var logger = provider.GetRequiredService<ILogger<BaristaAgent>>();
    var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
    var agent = new BaristaAgent(httpContextAccessor, logger);
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

var taskManager = app.Services.GetRequiredService<ITaskManager>();

// Map A2A endpoints
app.MapA2A(taskManager, "/").RequireAuthorization("AdminOnly");
app.MapHttpA2A(taskManager, "/").RequireAuthorization("AdminOnly");

app.MapDefaultEndpoints();

app.Run();
