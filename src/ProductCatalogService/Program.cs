using Microsoft.Identity.Web;
using ProductCatalogService.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("http://schemas.microsoft.com/identity/claims/scope", "admin");
    });
});

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<McpTools>();

builder.AddServiceDefaults();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapMcp("/mcp").RequireAuthorization("AdminOnly");

app.MapDefaultEndpoints();

app.Run();
