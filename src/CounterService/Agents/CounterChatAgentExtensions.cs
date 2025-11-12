using System.ClientModel;
using System.Security.Authentication;
using System.Security.Claims;
using System.Text.Json;
using A2A;
using Azure.AI.OpenAI;
using CounterService.Workflows;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Web;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using OpenAI.Chat;

namespace CounterService.Agents;

/// <summary>
/// Extension methods for building and configuring CounterChatAgent instances.
/// </summary>
public static class CounterChatAgentExtensions
{
    #region Public Extension Methods

    /// <summary>
    /// Builds an AI agent configured for AGUI (Agent UI) from the service provider.
    /// </summary>
    public static AIAgent BuildAIAgentForAGUI(this IServiceProvider sp, string endpoint, string apiKey, string chatModelId)
    {
        var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var logger = sp.GetRequiredService<ILogger<CounterChatAgent>>();
        var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
        var configuration = sp.GetRequiredService<IConfiguration>();
        var chatClient = new AzureOpenAIClient(
                  new Uri(endpoint),
                  new ApiKeyCredential(apiKey))
                    .GetChatClient(chatModelId);

        var agent = new CounterChatAgent(chatClient, configuration, clientFactory, httpContextAccessor, logger);
        return agent;
    }

    /// <summary>
    /// Builds a workflow configured for DevUI from the service provider.
    /// </summary>
    public static async Task<Workflow> BuildWorkflowForDevUI(
        this IServiceProvider sp,
        string workflowName,
        CancellationToken cancellationToken = default)
    {
        var chatClient = sp.GetRequiredService<ChatClient>();
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

        // Initialize clients based on auth mode
        var (mcpClient, a2aBaristaAIAgent, a2aKitchenAIAgent) = await InitializeClientsWithoutAuthAsync(httpClientFactory, cancellationToken);

        if (mcpClient == null || a2aBaristaAIAgent == null || a2aKitchenAIAgent == null)
        {
            throw new InvalidOperationException("Failed to initialize required clients");
        }

        // Build workflow using the common core method
        return await BuildWorkflowCoreAsync(
            chatClient,
            mcpClient,
            a2aBaristaAIAgent,
            a2aKitchenAIAgent,
            workflowName,
            cancellationToken);
    }

    #endregion

    #region Workflow Building

    /// <summary>
    /// Builds a workflow using the provided clients and configuration.
    /// </summary>
    internal static async Task<Workflow> BuildWorkflowCoreAsync(
        ChatClient chatClient,
        McpClient mcpClient,
        AIAgent a2aBaristaAIAgent,
        AIAgent a2aKitchenAIAgent,
        string? workflowName = null,
        CancellationToken cancellationToken = default)
    {
        var mcpTools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);

        var schema = AIJsonUtilities.CreateJsonSchema(typeof(OrderDto));
        var chatOptions = new ChatOptions()
        {
            ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema(
            schema: schema,
            schemaName: "OrderDto",
            schemaDescription: "Information about an order including list of items (ItemTypeDto). Each item includes ItemType, Name, Quantity, Price.")
        };

        // Convert ChatClient to IChatClient for CreateAIAgent extension method
        var chatClientInterface = chatClient.AsIChatClient();
        
        var agent = chatClientInterface
            .CreateAIAgent(
                instructions: CounterChatAgent.SystemInstructionPrompt,
                tools: [.. mcpTools.Cast<AITool>()])
            .AsBuilder()
            .UseOpenTelemetry(sourceName: CounterChatAgent.AgentName, configure: (cfg) => cfg.EnableSensitiveData = true)
            .Build();

        var validator = new ValidatorExecutor(chatClient, mcpClient);
        var start = new SplitExecutor(agent!);
        var baristaExecutor = new BaristaExecuter(a2aBaristaAIAgent);
        var kitchenExecutor = new KitchenExecuter(a2aKitchenAIAgent);
        var aggregation = new AggregationExecutor();
        var uncertainHandler = new HandleUncertainExecutor(mcpClient);

        var getValid = (bool valid) => (Func<object?, bool>)(detectionResult => detectionResult is ValidResponse res && res.Valid == valid);

        var workflowBuilder = new WorkflowBuilder(validator);

        if (!string.IsNullOrEmpty(workflowName))
        {
            workflowBuilder = workflowBuilder.WithName(workflowName);
        }

        var workflow = workflowBuilder
            .AddSwitch(validator, switchBuilder => switchBuilder
                .AddCase(getValid(true), start)
                .AddCase(getValid(false), uncertainHandler)
            )
            .AddFanOutEdge(start, [baristaExecutor, kitchenExecutor])
            .AddFanInEdge([baristaExecutor, kitchenExecutor], aggregation)
            .WithOutputFrom(aggregation, uncertainHandler)
            .Build();

        return workflow;
    }

    #endregion

    #region Client Initialization

    /// <summary>
    /// Initializes all required clients without authentication.
    /// </summary>
    private static async Task<(McpClient?, AIAgent?, AIAgent?)> InitializeClientsWithoutAuthAsync(
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        var mcpClient = await GetMcpClientAsync(httpClientFactory, tokenAcquisition: null, cancellationToken);
        var (a2aBaristaAIAgent, a2aKitchenAIAgent) = await ResolveA2AClientsAsync(httpClientFactory, tokenAcquisition: null, cancellationToken);
        return (mcpClient, a2aBaristaAIAgent, a2aKitchenAIAgent);
    }

    #endregion

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

    /// <summary>
    /// Retrieves the contents of a markdown instruction file with the specified name from the Agents directory.
    /// </summary>
    /// <param name="instructName">The name of the instruction file (without extension) to retrieve from the Agents directory.</param>
    /// <returns>A string containing the full contents of the specified markdown instruction file.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown if the Agents directory does not exist in the expected location.</exception>
    internal static string GetInstruction(string instructName)
    {
        string solutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        string instructFolder = Path.Combine(solutionDir, "Agents");

        if (!Directory.Exists(instructFolder))
            throw new DirectoryNotFoundException("Instructions folder not found.");

        return File.ReadAllText(Path.Combine(instructFolder, $"{instructName}.md"));
    }

    #endregion
}
