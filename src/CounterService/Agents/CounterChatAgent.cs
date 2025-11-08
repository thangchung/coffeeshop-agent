using System.Diagnostics;
using System.Security.Authentication;
using System.Security.Claims;
using System.Text.Json;
using A2A;
using CounterService.Workflows;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Web;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;

namespace CounterService.Agents;

public partial class CounterChatAgent(
    ChatClient chatClient,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ITokenAcquisition tokenAcquisition,
    ILogger<CounterChatAgent> logger) : AIAgent, IDisposable
{
    private const string AgentName = $"A2A.{nameof(CounterChatAgent)}";
    public static readonly ActivitySource ActivitySource = new(AgentName, "1.0.0");

    public ILogger<CounterChatAgent> Logger { get; } = logger;
    public ChatClient ChatClient { get; } = chatClient;
    public McpClient? McpClient { get; private set; }
    public IConfiguration Configuration { get; } = configuration;
    public IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;
    public IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;
    public ITokenAcquisition TokenAcquisition { get; } = tokenAcquisition;

    private static string SystemInstructionPrompt => $$"""
    You are a counter/staff member in the coffee shop, and only serve customers who order food and beverages.
    If the customer asks for anything else, please politely refuse and tell them you only serve food and beverages.

    Use your tool to extract the name, price, and item type of the customer's message.
    Use your tool to query and get the valid price of the item (If you have a list of item types, then call to GetItemPrices tool at a priority.).
    The quantity of each item needs to be kept (if no quantity input from the user, then auto-set to 1).

    {{AFewShotPrompt}}
    """;

    private static string AFewShotPrompt => """
    EXAMPLE 1:
    Customer's message: I want a black coffee and cappuccino.
    JSON Response:
    {
        "baristaItems": [
            {
                "name": "black coffee",
                "itemType": "BLACK_COFFEE",
                "quantity": 1,
                "price": 3
            },
            {
                "name": "cappuccino",
                "itemType": "CAPPUCCINO",
                "quantity": 1,
                "price": 3.5
            }
        ],
        "kitchenItems": []
    }

    EXAMPLE 2:
    Customer's message: I want a black coffee, 2 cappuccino and 2 cakepops.
    JSON Response:
    {
        "baristaItems": [
            {
                "name": "black coffee",
                "itemType": "BLACK_COFFEE",
                "quantity": 1,
                "price": 3
            },
            {
                "name": "cappuccino",
                "itemType": "CAPPUCCINO",
                "quantity": 2,
                "price": 3.5
            }
        ],
        "kitchenItems": [
            {
                "name": "cakepop",
                "itemType": "CAKEPOP",
                "quantity": 2,
                "price": 5
            }
        ]
    }

    EXAMPLE 3:
    Customer's message: I want a croissant chocolate.
    JSON Response:
    {
        "baristaItems": [],
        "kitchenItems": [
            {
                "name": "croissant chocolate",
                "itemType": "CROISSANT_CHOCOLATE",
                "quantity": 1,
                "price": 5.5
            }
        ]
    }

    EXAMPLE 4:
    If you don't know how to parse the order object, respond with:
    {
        "baristaItems": [],
        "kitchenItems": []
    }
    """;

    public override AgentThread GetNewThread()
        => new CustomAgentThread();

    public override AgentThread DeserializeThread(JsonElement serializedThread, JsonSerializerOptions? jsonSerializerOptions = null)
        => new CustomAgentThread(serializedThread, jsonSerializerOptions);

    public override Task<AgentRunResponse> RunAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    private async IAsyncEnumerable<AgentRunResponseUpdate> ExecuteWorkflowAsync(
        Workflow workflow,
        string input,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using StreamingRun run = await InProcessExecution.StreamAsync(workflow!, input, cancellationToken: cancellationToken);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is BaristaOrderSplitted baristaOrderSplitted)
            {
                yield return new AgentRunResponseUpdate
                {
                    AgentId = Id,
                    AuthorName = "bot",
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent("Barista order sent.")],
                    ResponseId = Guid.NewGuid().ToString("N"),
                    MessageId = Guid.NewGuid().ToString("N")
                };
            }
            else if (evt is KitchenOrderSplitted kitchenOrderSplitted)
            {
                yield return new AgentRunResponseUpdate
                {
                    AgentId = Id,
                    AuthorName = "bot",
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent("Kitchen order sent.")],
                    ResponseId = Guid.NewGuid().ToString("N"),
                    MessageId = Guid.NewGuid().ToString("N")
                };
            }
            else if (evt is WorkflowOutputEvent outputEvent)
            {
                yield return new AgentRunResponseUpdate
                {
                    AgentId = Id,
                    AuthorName = "bot",
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(outputEvent.Data?.ToString() ?? "No data")],
                    ResponseId = Guid.NewGuid().ToString("N"),
                    MessageId = Guid.NewGuid().ToString("N")
                };
            }
        }
    }

    public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var workflow = await GetAgenticWorkflow(ChatClient, Configuration, HttpClientFactory, HttpContextAccessor, TokenAcquisition, Logger, cancellationToken);

        var lastChatMsg = messages.Where(m => m.Role == ChatRole.User).LastOrDefault()!;

        await foreach (AgentRunResponseUpdate update in ExecuteWorkflowAsync(workflow, lastChatMsg.Text!, cancellationToken: cancellationToken))
        {
            yield return update;
        }
    }

    public async Task<Workflow?> GetAgenticWorkflow(
        ChatClient chatClient,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        ITokenAcquisition tokenAcquisition,
        ILogger<CounterChatAgent> logger,
        CancellationToken cancellationToken)
    {
        EnsureAuthentication(httpContextAccessor);

        var mcpClient = await GetMcpClient(httpClientFactory, tokenAcquisition, cancellationToken);
        var mcpTools = await mcpClient!.ListToolsAsync(cancellationToken: cancellationToken);

        var (a2aBaristaAIAgent, a2aKitchenAIAgent) = await ResolveA2AClients(httpClientFactory, tokenAcquisition, cancellationToken);

        var schema = AIJsonUtilities.CreateJsonSchema(typeof(OrderDto));
        var chatOptions = new ChatOptions()
        {
            ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema(
            schema: schema,
            schemaName: "OrderDto",
            schemaDescription: "Information about an order including list of items (ItemTypeDto). Each item includes ItemType, Name, Quantity, Price.")
        };

        var agent = chatClient.CreateAIAgent(
                new ChatClientAgentOptions(instructions: SystemInstructionPrompt, tools: [.. mcpTools.Cast<AITool>()])
                {
                    ChatOptions = chatOptions,
                })
            .AsBuilder()
            .UseOpenTelemetry(sourceName: AgentName, configure: (cfg) => cfg.EnableSensitiveData = true)
            .Build();

        var validator = new ValidatorExecutor(ChatClient, mcpClient);
        var start = new SplitExecutor(agent!);
        var baristaExecutor = new BaristaExecuter(a2aBaristaAIAgent);
        var kitchenExecutor = new KitchenExecuter(a2aKitchenAIAgent);
        var aggregation = new AggregationExecutor();
        var uncertainHandler = new HandleUncertainExecutor(mcpClient!);

        var getValid = (bool valid) => (Func<object?, bool>)(detectionResult => detectionResult is ValidResponse res && res.Valid == valid);

        var workflow = new WorkflowBuilder(validator)
            .AddSwitch(validator, switchBuilder => switchBuilder
                .AddCase(getValid(true), start)
                .AddCase(getValid(false), uncertainHandler)
            )
            .AddFanOutEdge(start, [baristaExecutor, kitchenExecutor])
            .AddFanInEdge([baristaExecutor, kitchenExecutor], aggregation)
            .WithOutputFrom(aggregation, uncertainHandler)
            .Build();

        // string mermaid = workflow.ToMermaidString();

        return workflow;
    }

    public static async Task<Workflow> BuildWorkflowAsync(IServiceProvider sp, string workflowName, CancellationToken cancellationToken)
    {
        var chatClient = sp.GetRequiredService<ChatClient>();
        var configuration = sp.GetRequiredService<IConfiguration>();
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
        var tokenAcquisition = sp.GetRequiredService<ITokenAcquisition>();
        var logger = sp.GetRequiredService<ILogger<CounterChatAgent>>();
        var environment = sp.GetRequiredService<IHostEnvironment>();

        // In development, skip authentication for DevUI; in production, require authentication
        bool isDevelopment = environment.IsDevelopment();
        bool hasAuthenticatedUser = httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;

        McpClient? mcpClient = null;
        List<AITool> mcpTools = new();

        if (isDevelopment && !hasAuthenticatedUser)
        {
            // Development mode without authentication - skip MCP client initialization
            logger.LogWarning("Running in Development mode without authentication. MCP tools will not be available.");
        }
        else
        {
            // Production mode or authenticated development session - use full authentication
            mcpClient = await GetMcpClient(httpClientFactory, tokenAcquisition, CancellationToken.None);
            var tools = await mcpClient!.ListToolsAsync(cancellationToken: CancellationToken.None);
            mcpTools.AddRange(tools.Cast<AITool>());
        }

        AIAgent? a2aBaristaAIAgent = null;
        AIAgent? a2aKitchenAIAgent = null;

        if (isDevelopment && !hasAuthenticatedUser)
        {
            // Development mode without authentication - skip A2A client initialization
            logger.LogWarning("Running in Development mode without authentication. A2A agents will not be available.");
        }
        else
        {
            // Production mode or authenticated development session - use full authentication
            (a2aBaristaAIAgent, a2aKitchenAIAgent) = await ResolveA2AClients(httpClientFactory, tokenAcquisition, CancellationToken.None);
        }

        var schema = AIJsonUtilities.CreateJsonSchema(typeof(OrderDto));
        var chatOptions = new ChatOptions()
        {
            ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema(
            schema: schema,
            schemaName: "OrderDto",
            schemaDescription: "Information about an order including list of items (ItemTypeDto). Each item includes ItemType, Name, Quantity, Price.")
        };

        var agent = chatClient.CreateAIAgent(
                new ChatClientAgentOptions(instructions: SystemInstructionPrompt, tools: [.. mcpTools])
                {
                    ChatOptions = chatOptions,
                })
            .AsBuilder()
            .UseOpenTelemetry(sourceName: AgentName, configure: (cfg) => cfg.EnableSensitiveData = true)
            .Build();

        var validator = new ValidatorExecutor(chatClient, mcpClient!);
        var start = new SplitExecutor(agent!);
        var baristaExecutor = new BaristaExecuter(a2aBaristaAIAgent!);
        var kitchenExecutor = new KitchenExecuter(a2aKitchenAIAgent!);
        var aggregation = new AggregationExecutor();
        var uncertainHandler = new HandleUncertainExecutor(mcpClient!);

        var getValid = (bool valid) => (Func<object?, bool>)(detectionResult => detectionResult is ValidResponse res && res.Valid == valid);

        var workflow = new WorkflowBuilder(validator)
            .WithName(workflowName)
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

    private static async Task<McpClient?> GetMcpClient(IHttpClientFactory httpClientFactory, ITokenAcquisition tokenAcquisition, CancellationToken cancellationToken)
    {
        var medata = await DiscoverAuthServerMetadata(httpClientFactory, new Uri("https+http://product/"));
        var scope = medata.ScopesSupported.FirstOrDefault() ?? throw new AuthenticationException("Couldn't find scope for MCP server.");

        var accessToken = await tokenAcquisition.GetAccessTokenForUserAsync([scope]);

        var httpClient = httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        httpClient.BaseAddress = new Uri("https+http://product/mcp");

        var transport = new HttpClientTransport(new()
        {
            Endpoint = new Uri("http://product/mcp"),// set anything with valid URI, because we override it with our own HttpClient
            Name = "product-catalog-service"
        }, httpClient, ownsHttpClient: true);

        // Create MCP client using the official factory
        var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        return mcpClient;
    }

    private static async Task<(AIAgent, AIAgent)> ResolveA2AClients(IHttpClientFactory httpClientFactory, ITokenAcquisition tokenAcquisition, CancellationToken cancellationToken)
    {
        var a2aClientUrls = new string[] { "https+http://barista", "https+http://kitchen" };

        List<AIAgent> a2aAgents = [];

        foreach (var url in a2aClientUrls)
        {
            var httpClient = httpClientFactory.CreateClient();

            var cardResolver = new A2ACardResolver(new Uri(url), httpClient: httpClient);
            var agentCard = await cardResolver.GetAgentCardAsync(cancellationToken);

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

            var agent = await cardResolver.GetAIAgentAsync(httpClient, cancellationToken: cancellationToken);
            a2aAgents.Add(agent);
        }

        return (a2aAgents[0], a2aAgents[1]);
    }

    private static async Task<ProtectedResourceMetadata> DiscoverAuthServerMetadata(IHttpClientFactory httpClientFactory, Uri authServerUri)
    {
        var metadataEndpoint = ".well-known/oauth-protected-resource";
        using var httpClient = httpClientFactory.CreateClient();
        httpClient.BaseAddress = authServerUri;

        var jsonResponse = await httpClient.GetStringAsync(metadataEndpoint);
        var metadata = JsonSerializer.Deserialize<ProtectedResourceMetadata>(jsonResponse);
        return metadata!;
    }

    private void EnsureAuthentication(IHttpContextAccessor httpContextAccessor)
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

    public void Dispose()
    {
        if (McpClient != null)
#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
            GC.SuppressFinalize(McpClient);
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    }
}

internal sealed class CustomAgentThread : InMemoryAgentThread
{
    internal CustomAgentThread() { }

    internal CustomAgentThread(JsonElement serializedThreadState, JsonSerializerOptions? jsonSerializerOptions = null)
        : base(serializedThreadState, jsonSerializerOptions) { }
}
