using System.Security.Authentication;
using System.Security.Claims;
using System.Text.Json;
using A2A;
using Microsoft.Agents.AI;
using Microsoft.Identity.Web;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace CounterService.Extentions;

public static class AgenticExtentions
{
    #region MCP Client Helpers

    /// <summary>
    /// Gets an MCP client with optional authentication.
    /// </summary>
    internal static async Task<McpClient?> GetMcpClientAsync(
        IHttpClientFactory httpClientFactory,
        ITokenAcquisition? tokenAcquisition,
        CancellationToken cancellationToken = default)
    {
        var httpClient = httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Clear();

        // Add authentication if provided
        if (tokenAcquisition != null)
        {
            var metadata = await DiscoverAuthServerMetadataAsync(httpClientFactory, new Uri("https+http://product/"));
            var scope = metadata.ScopesSupported.FirstOrDefault() ?? throw new AuthenticationException("Couldn't find scope for MCP server.");
            var accessToken = await tokenAcquisition.GetAccessTokenForUserAsync([scope]);
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        }

        httpClient.BaseAddress = new Uri("https+http://product/mcp");

        var transport = new HttpClientTransport(new()
        {
            Endpoint = new Uri("http://product/mcp"), // set anything with valid URI, because we override it with our own HttpClient
            Name = "product-catalog-service"
        }, httpClient, ownsHttpClient: true);

        // Create MCP client using the official factory
        var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        return mcpClient;
    }

    #endregion

    #region A2A Client Helpers

    /// <summary>
    /// Resolves A2A clients for barista and kitchen services with optional authentication.
    /// </summary>
    internal static async Task<(AIAgent, AIAgent)> ResolveA2AClientsAsync(
        IHttpClientFactory httpClientFactory,
        ITokenAcquisition? tokenAcquisition,
        CancellationToken cancellationToken = default)
    {
        var a2aClientUrls = new string[] { "https+http://barista", "https+http://kitchen" };
        List<AIAgent> a2aAgents = [];

        foreach (var url in a2aClientUrls)
        {
            var httpClient = httpClientFactory.CreateClient();
            var cardResolver = new A2ACardResolver(new Uri(url), httpClient: httpClient);
            var agentCard = await cardResolver.GetAgentCardAsync(cancellationToken);

            // Add authentication if provided
            if (tokenAcquisition != null)
            {
                // Extract scope from security scheme
                if (agentCard.SecuritySchemes == null || !agentCard.SecuritySchemes.TryGetValue("root", out var securityScheme) || securityScheme is not OAuth2SecurityScheme oauthScheme)
                {
                    throw new AuthenticationException($"Invalid or missing OAuth2 security scheme for A2A client at {url}.");
                }

                var scope = oauthScheme.Flows?.AuthorizationCode?.Scopes?.FirstOrDefault().Key;
                if (string.IsNullOrEmpty(scope))
                {
                    throw new AuthenticationException($"Couldn't find scope for A2A client at {url}.");
                }

                string accessToken = await tokenAcquisition.GetAccessTokenForUserAsync([scope]);

                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            }

            var agent = await cardResolver.GetAIAgentAsync(httpClient, cancellationToken: cancellationToken);
            a2aAgents.Add(agent);
        }

        return (a2aAgents[0], a2aAgents[1]);
    }

    #endregion

    #region Authentication Helpers

    /// <summary>
    /// Discovers OAuth protected resource metadata from the authentication server.
    /// </summary>
    internal static async Task<ProtectedResourceMetadata> DiscoverAuthServerMetadataAsync(
        IHttpClientFactory httpClientFactory,
        Uri authServerUri)
    {
        var metadataEndpoint = ".well-known/oauth-protected-resource";
        using var httpClient = httpClientFactory.CreateClient();
        httpClient.BaseAddress = authServerUri;

        var jsonResponse = await httpClient.GetStringAsync(metadataEndpoint);
        var metadata = JsonSerializer.Deserialize<ProtectedResourceMetadata>(jsonResponse);
        return metadata!;
    }

    /// <summary>
    /// Ensures the current HTTP context is properly authenticated with required roles.
    /// </summary>
    internal static void EnsureAuthentication(IHttpContextAccessor httpContextAccessor)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            throw new AuthenticationException("User is not authenticated.");
        }

        var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
        var jwtToken = string.Empty;
        if (authHeader != null && authHeader.StartsWith("Bearer "))
        {
            jwtToken = authHeader.Substring("Bearer ".Length).Trim();
        }

        var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(jwtToken) || string.IsNullOrEmpty(role) || role?.ToLowerInvariant() is not "admin")
        {
            throw new AuthenticationException("JWT token: missing or Role: admin is required.");
        }
    }

    #endregion
}
