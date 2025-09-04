using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ProductCatalogService.Resources;
using ProductCatalogService.Tools;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
    })
    .AddMcp(options =>
    {
        var identityOptions = builder
            .Configuration.GetSection("AzureAd")
            .Get<MicrosoftIdentityOptions>()!;

        options.ResourceMetadata = new ProtectedResourceMetadata
        {
            Resource = GetMcpServerUrl(),
            AuthorizationServers = [GetAuthorizationServerUrl(identityOptions)],
            ScopesSupported = [$"api://{identityOptions.ClientId}/CoffeeShop.Mcp.Product.ReadWrite"],
        };
    })
    .AddMicrosoftIdentityWebApi(options =>
    {
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
    options.AddPolicy("ProductCatalogOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(ClaimConstants.Scope, "CoffeeShop.Mcp.Product.ReadWrite");
    });
});

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<McpTools>()
    .WithResources<McpResources>();

builder.Services.AddOpenApi();

builder.AddServiceDefaults();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapMcp("/mcp").RequireAuthorization("ProductCatalogOnly");

app.MapDefaultEndpoints();

app.Run();

// Helper method to get authorization server URL
static Uri GetAuthorizationServerUrl(MicrosoftIdentityOptions identityOptions) =>
    new($"{identityOptions.Instance?.TrimEnd('/')}/{identityOptions.TenantId}/v2.0");

Uri GetMcpServerUrl() => builder.Configuration.GetValue<Uri>("McpServerUrl") ?? throw new InvalidOperationException("McpServerUrl is not configured.");

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
