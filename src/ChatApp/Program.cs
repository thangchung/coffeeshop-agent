using System.Net.Http.Headers;
using ChatApp.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Primitives;
using Microsoft.Identity.Web;

// Ref: https://github.com/damienbod/BlazorServerOidc/tree/main/BlazorWebApp

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(options =>
    {
        builder.Configuration.Bind("AzureAd", options);

        options.SaveTokens = true;
        options.Scope.Add($"api://{builder.Configuration["AzureAd:ClientId"]}/CoffeeShop.Counter.ReadWrite");

        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = CustomTokenValidated,
            OnAuthenticationFailed = CustomAuthenticationFailed
        };

    }, options => builder.Configuration.Bind("AzureAd", options));

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<TokenHandler>();

builder.Services.AddHttpClient("CounterClient",
      client => client.BaseAddress = new Uri("https+http://counter" ??
          throw new Exception("Missing base address!")))
      .AddHttpMessageHandler<TokenHandler>();

builder.Services.AddAuthenticationCore();
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapLoginLogoutEndpoints();

app.Run();

async Task CustomTokenValidated(TokenValidatedContext context)
{
    await Task.CompletedTask;
}

async Task CustomAuthenticationFailed(AuthenticationFailedContext context)
{
    // Custom logic upon authentication failure
    await Task.CompletedTask;
}

public class TokenHandler(IHttpContextAccessor httpContextAccessor) :
    DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext is null)
        {
            throw new Exception("HttpContext not available");
        }

        var accessToken = await httpContextAccessor.HttpContext.GetTokenAsync("access_token");

        if (accessToken is null)
        {
            throw new Exception("No access token");
        }

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        return await base.SendAsync(request, cancellationToken);
    }
}

public static class LoginLogoutEndpoints
{
    public static WebApplication MapLoginLogoutEndpoints(this WebApplication app)
    {
        app.MapGet("/login", async context =>
        {
            var returnUrl = context.Request.Query["returnUrl"];

            await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
            {
                RedirectUri = returnUrl == StringValues.Empty ? "/" : returnUrl.ToString()
            });
        }).AllowAnonymous();

        app.MapPost("/logout", async context =>
        {
            if (context.User.Identity?.IsAuthenticated ?? false)
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
            }
            else
            {
                context.Response.Redirect("/");
            }
        });

        return app;
    }

}
