using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.Serialization;
using System.Security.Authentication;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using A2A;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Validation;
using Microsoft.Identity.Web;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;

namespace CounterService.Agents;

public class CounterAgent(
    ChatClient chatClient,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ITokenAcquisition tokenAcquisition,
    ILogger<CounterAgent> logger) : IDisposable
{
    private ITaskManager? _taskManager;
    private const string AgentName = $"A2A.{nameof(CounterAgent)}";
    public static readonly ActivitySource ActivitySource = new(AgentName, "1.0.0");

    public ILogger<CounterAgent> Logger { get; } = logger;
    public ChatClient ChatClient { get; } = chatClient;
    public McpClient? McpClient { get; private set; }
    public IConfiguration Configuration { get; } = configuration;
    public IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;
    public IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;
    public ITokenAcquisition TokenAcquisition { get; } = tokenAcquisition;
    public Dictionary<string, string> DownstreamAgentEndpoints { get; set; }
        = new Dictionary<string, string> {
            { "BARISTA", "https+http://barista" },
            { "KITCHEN", "https+http://kitchen" }
    };
    public Dictionary<string, AIAgent> A2AAIAgents { get; set; } = [];
    public string JwtToken { get; set; } = string.Empty;
    public bool IsStubLLMResponse { get; set; } = false;

    public void Attach(ITaskManager taskManager)
    {
        _taskManager = taskManager;
        _taskManager.OnTaskCreated = OnTaskCreatedAsync;
        _taskManager.OnTaskUpdated = OnTaskUpdatedAsync;
        _taskManager.OnAgentCardQuery = GetAgentCardAsync;
    }

    private async Task OnTaskCreatedAsync(AgentTask task, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("OnTaskCreated", ActivityKind.Server);
        activity?.SetTag("task.id", task.Id);

        if (_taskManager == null)
        {
            throw new InvalidOperationException("TaskManager is not attached.");
        }

        var httpContext = HttpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            throw new AuthenticationException("User is not authenticated.");
        }

        var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
        if (authHeader != null && authHeader.StartsWith("Bearer "))
        {
            JwtToken = authHeader.Substring("Bearer ".Length).Trim();
        }

        var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(JwtToken) || string.IsNullOrEmpty(role) || role?.ToLowerInvariant() is not "admin")
        {
            throw new AuthenticationException("JWT token: missing or Role: admin is required.");
        }

        foreach (var (key, endpoint) in DownstreamAgentEndpoints)
        {
            activity?.SetTag($"downstream.{key.ToLower()}.endpoint", endpoint);
            Logger.LogDebug("Configured downstream agent endpoint: {Endpoint}", endpoint);

            var httpClient = HttpClientFactory.CreateClient();

            var cardResolver = new A2ACardResolver(new Uri(endpoint), httpClient: httpClient);
            var agentCard = await cardResolver.GetAgentCardAsync(cancellationToken);
            activity?.SetTag($"downstream.{key.ToLower()}.agentCard.url", agentCard.Url);
            Logger.LogDebug("Resolved Agent card: {Endpoint}", $"{agentCard.Url}");

            //todo: handle the exception later
            var scope = ((OAuth2SecurityScheme)agentCard.SecuritySchemes["root"]).Flows.AuthorizationCode.Scopes.FirstOrDefault().Key;
            activity?.SetTag($"downstream.{key.ToLower()}.agentCard.security_schema_scope", scope);
            string accessToken = await TokenAcquisition.GetAccessTokenForUserAsync([scope]);

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

            var agent = await cardResolver.GetAIAgentAsync(httpClient, cancellationToken: cancellationToken);
            A2AAIAgents.TryAdd(key, agent);
        }

        Logger.LogInformation("Task created with ID: {TaskId}", task.Id);
        await ProcessTaskAsync(task, cancellationToken);
    }

    private async Task OnTaskUpdatedAsync(AgentTask task, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("OnTaskUpdated", ActivityKind.Server);
        activity?.SetTag("task.id", task.Id);

        if (_taskManager == null)
        {
            throw new InvalidOperationException("TaskManager is not attached.");
        }

        var httpContext = HttpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            throw new AuthenticationException("User is not authenticated.");
        }

        Logger.LogInformation("Task updated with ID: {TaskId}", task.Id);
        await ProcessTaskAsync(task, cancellationToken);
    }

    private async Task ProcessTaskAsync(AgentTask task, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("ProcessTask", ActivityKind.Server);
        activity?.SetTag("task.id", task.Id);

        if (_taskManager == null)
        {
            throw new InvalidOperationException("TaskManager is not attached.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning("Task processing cancelled for ID: {TaskId}", task.Id);
            return;
        }

        try
        {
            // Extract the message from task history
            var lastMessage = task.History?.LastOrDefault();
            if (lastMessage?.Parts == null)
            {
                await _taskManager.UpdateStatusAsync(
                    task.Id,
                    TaskState.Failed,
                    new AgentMessage
                    {
                        Parts = [new TextPart { Text = "No message content found in task" }]
                    },
                    final: true,
                    cancellationToken: cancellationToken);
                return;
            }

            var messageText = lastMessage.Parts.OfType<TextPart>().FirstOrDefault()?.Text;
            if (string.IsNullOrEmpty(messageText))
            {
                await _taskManager.UpdateStatusAsync(
                    task.Id,
                    TaskState.Failed,
                    new AgentMessage
                    {
                        Parts = [new TextPart { Text = "No text content found in message" }]
                    },
                    final: true,
                    cancellationToken: cancellationToken);
                return;
            }

            Logger.LogInformation("Sending A2A message with authentication in HTTP headers");

            var chatAgent = await GetChatAgent(isStub: IsStubLLMResponse, cancellationToken: cancellationToken);

            var validator = new ValidatorExecutor(ChatClient, McpClient!);
            var start = new SplitExecutor(chatAgent!);
            var baristaExecutor = new BaristaExecuter(A2AAIAgents["BARISTA"]);
            var kitchenExecutor = new KitchenExecuter(A2AAIAgents["KITCHEN"]);
            var aggregation = new AggregationExecutor();
            var uncertainHandler = new HandleUncertainExecutor();

            var workflow = new WorkflowBuilder(validator)
                .AddSwitch(validator, switchBuilder => switchBuilder
                    .AddCase(GetValidCondition(true), start)
                    .AddCase(GetValidCondition(false), uncertainHandler)
                )
                .AddFanOutEdge(start, targets: [baristaExecutor, kitchenExecutor])
                .AddFanInEdge(aggregation, sources: [baristaExecutor, kitchenExecutor])
                .WithOutputFrom(aggregation, uncertainHandler)
                .Build();

            // string mermaid = workflow.ToMermaidString();

            await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, messageText, cancellationToken: cancellationToken);
            await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken))
            {
                if(evt is BaristaOrderSplitted baristaOrderSplitted)
                {
                    await _taskManager.UpdateStatusAsync(
                        task.Id,
                        TaskState.Working,
                        new AgentMessage
                        {
                            Parts = [new TextPart { Text = "Barista order sent." }]
                        },
                        cancellationToken: cancellationToken);
                }
                else if (evt is KitchenOrderSplitted kitchenOrderSplitted)
                {
                    await _taskManager.UpdateStatusAsync(
                        task.Id,
                        TaskState.Working,
                        new AgentMessage
                        {
                            Parts = [new TextPart { Text = "Kitchen order sent." }]
                        },
                        cancellationToken: cancellationToken);
                }
                else if (evt is WorkflowOutputEvent outputEvent)
                {
                    var msg = outputEvent;

                    // Complete the task
                    await _taskManager.UpdateStatusAsync(
                        task.Id,
                        TaskState.Completed,
                        new AgentMessage
                        {
                            Parts = [new TextPart { Text = msg?.Data?.ToString() ?? "Order is created." }]
                        },
                        final: true,
                        cancellationToken: cancellationToken);
                }
            }

            Logger.LogInformation("Task {TaskId} completed successfully", task.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing task {TaskId}", task.Id);

            await _taskManager.UpdateStatusAsync(
                task.Id,
                TaskState.Failed,
                new AgentMessage
                {
                    Parts = [new TextPart { Text = $"Error processing the message: {ex.Message}" }]
                },
                final: true,
                cancellationToken: cancellationToken);
        }
    }

    private static Func<object?, bool> GetValidCondition(bool valid) => detectionResult => detectionResult is ValidResponse res && res.Valid == valid;

    private Task<AgentCard> GetAgentCardAsync(string agentUrl, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AgentCard>(cancellationToken);
        }

        var capabilities = new AgentCapabilities
        {
            Streaming = true,
            PushNotifications = false,
        };

        return Task.FromResult(new AgentCard
        {
            Name = "Counter Service Agent",
            Description = "A2A client agent that sends messages through the A2A protocol to the Barista and Kitchen services.",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = capabilities,
            Skills = [
                new AgentSkill
                {
                    Name = "send_order",
                    Description = "Send messages via A2A protocol to Barista and Kitchen services with MCP integration to get price of each item."
                }
            ],
            SecuritySchemes = new()
            {
                ["root"] = new OAuth2SecurityScheme(
                    new OAuthFlows
                    {
                        AuthorizationCode = new AuthorizationCodeOAuthFlow(
                            authorizationUrl: new Uri($"{Configuration["AzureAd:Instance"]}{Configuration["AzureAd:TenantId"]}/oauth2/v2.0/authorize"),
                            tokenUrl: new Uri($"{Configuration["AzureAd:Instance"]}{Configuration["AzureAd:TenantId"]}/oauth2/v2.0/token"),
                            scopes: new Dictionary<string, string>
                            {
                                { $"api://{Configuration["AzureAd:ClientId"]}/CoffeeShop.Counter.ReadWrite", "Access the Counter Service as the signed-in user" }
                            })
                    },
                    "OAuth2 with JWT Bearer tokens"
                )
            },
            Security =
            [
                new Dictionary<string, string[]>
                {
                    { "Bearer", ["CoffeeShop.Counter.ReadWrite"] }
                }
            ]
        });
    }

    private async Task<AIAgent?> GetChatAgent(bool isStub = false, CancellationToken cancellationToken = default)
    {
        var messageClassified = !isStub ? string.Empty :
            """
            {
                "baristaItems": [
                    {
                        "name": "black coffee",
                        "itemType": "COFFEE_BLACK",
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
                "kitchenItems": [
                    {
                        "name": "cake pop",
                        "itemType": "CAKEPOP",
                        "quantity": 2,
                        "price": 5
                    }
                ]
            }
            """;

        var medata = await DiscoverAuthServerMetadata(new Uri("https+http://product/"));
        var scope = medata.ScopesSupported.FirstOrDefault() ?? throw new AuthenticationException("Couldn't find scope for MCP server.");

        AIAgent? agent = null;
        if (!isStub)
        {
            var accessToken = await TokenAcquisition.GetAccessTokenForUserAsync([scope]);

            // mcp
            var httpClient = HttpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            httpClient.BaseAddress = new Uri("https+http://product/mcp");

            var transport = new HttpClientTransport(new()
            {
                Endpoint = new Uri("http://product/mcp"),// set anything with valid URI, because we override it with our own HttpClient
                Name = "product-catalog-service"
            }, httpClient, ownsHttpClient: true);

            // Create MCP client using the official factory
            McpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

            var mcpTools = await McpClient.ListToolsAsync(cancellationToken: cancellationToken);

            var productsResource = await McpClient.ReadResourceAsync(new Uri("data://products"), cancellationToken: cancellationToken);

            var options = JsonSerializerOptions.Default;
            var exporterOptions = new JsonSchemaExporterOptions()
            {
                TreatNullObliviousAsNonNullable = true,
            };
            var schema = options.GetJsonSchemaAsNode(typeof(OrderDto), exporterOptions);
            var instructions = $$"""
            You are a counter/staff member in the coffee shop, and only serve customers who order food and beverages. If the customer asks for anything else, please politely refuse and tell them you only serve food and beverages.

            Parse a customer's message into an order object in valid JSON (in the camel-case format).
            Use your tool to extract the name, price, and item type of the customer's message.
            Use your tool to query and get the valid price of the item (If you have a list of item types, then call to GetItemPrices tool at a priority.).
            The quantity of each item needs to be kept (if no quantity input from the user, then auto-set to 1).
            Use the provided JSON schema for your reply (no markdown for formatting the JSON object needed):
            {{schema}}

            The itemType (products) should be one of the following values: {{productsResource.Contents[0].ToAIContent()}}, and if customers provide other value, please tell them the store doesn't have it and request them to change to the valid one.

            EXAMPLE 1:
            Customer's message: I want a black coffee and cappuccino.
            JSON Response:
            ```
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


            agent = ChatClient
                .CreateAIAgent(instructions: instructions,
                                tools: [.. mcpTools.Cast<AITool>()])
                .AsBuilder()
                .UseOpenTelemetry(sourceName: AgentName, configure: (cfg) => cfg.EnableSensitiveData = true)
                .Build();
        }

        return agent;
    }

    public async Task<ProtectedResourceMetadata> DiscoverAuthServerMetadata(Uri authServerUri)
    {
        var metadataEndpoint = ".well-known/oauth-protected-resource";
        using var httpClient = HttpClientFactory.CreateClient();
        httpClient.BaseAddress = authServerUri;

        var jsonResponse = await httpClient.GetStringAsync(metadataEndpoint);
        var metadata = JsonSerializer.Deserialize<ProtectedResourceMetadata>(jsonResponse);
        return metadata;
    }

    public void Dispose()
    {
        if (McpClient != null)
#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
            GC.SuppressFinalize(McpClient);
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    }

    public enum ItemType
    {
        // Beverages
        CAPPUCCINO,
        COFFEE_BLACK,
        COFFEE_WITH_ROOM,
        ESPRESSO,
        ESPRESSO_DOUBLE,
        LATTE,
        // Food
        CAKEPOP,
        CROISSANT,
        MUFFIN,
        CROISSANT_CHOCOLATE,
        // Others
        CHICKEN_MEATBALLS,
    }
    public class ItemTypeDto
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ItemType ItemType { get; set; }

        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public float Price { get; set; }
    }

    public record OrderDto(List<ItemTypeDto> BaristaItems, List<ItemTypeDto> KitchenItems);

    public class ProtectedResourceMetadata
    {
        [JsonPropertyName("resource_type")]
        public string ResourceType { get; set; }

        [JsonPropertyName("resource")]
        public Uri Resource { get; set; }

        [JsonPropertyName("authorization_servers")]
        public Uri[] AuthorizationServers { get; set; }

        [JsonPropertyName("scopes_supported")]
        public string[] ScopesSupported { get; set; }

        [JsonPropertyName("bearer_methods_supported")]
        public string[] BearerMethodsSupported { get; set; }
    }

    public class BaristaOrderSplitted : WorkflowEvent
    {
        public List<ItemTypeDto> Items { get; set; } = [];
    }

    public class KitchenOrderSplitted : WorkflowEvent
    {
        public List<ItemTypeDto> Items { get; set; } = [];
    }

    public class CustomAgentResponse : WorkflowEvent
    {
        public required AgentRunResponse Response { get; set; }
    }

    public class ValidResponse
    {
        public string Query { get; set; } = default!;
        public required bool Valid { get; set; } = false;
    }

    public sealed class ValidatorExecutor(ChatClient chatClient, McpClient mcpClient) : Executor<string, ValidResponse>(nameof(ValidatorExecutor))
    {
        public static readonly ActivitySource ActivitySource = new($"MAF.{nameof(ValidatorExecutor)}", "1.0.0");

        public override async ValueTask<ValidResponse> HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("HandleAsync", ActivityKind.Server);

            var instructions = $$"""
            You are a validator that checks if customer orders contain valid items.

            Use your tool (GetItemTypes) to check if the customer order contains valid items from our inventory.

            IMPORTANT: You must respond with ONLY valid JSON in this exact format. Do not include any additional text, explanations, or markdown formatting:

               If any item is not valid: { "valid": false }
               If all items are valid: { "valid": true }

               Examples of CORRECT responses:
                  { "valid": true }
                  { "valid": false }

               Do NOT respond with anything else. No explanations, no additional text, just the JSON object.
            """;

            var mcpTools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);

            var agent = chatClient
              .CreateAIAgent(instructions: instructions, tools: [.. mcpTools.Cast<AITool>()])
                    .AsBuilder()
                    .UseOpenTelemetry(sourceName: AgentName, configure: (cfg) => cfg.EnableSensitiveData = true)
                    .Build();

            var updates = agent.RunStreamingAsync(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, message), cancellationToken: cancellationToken);
            var agentResponse = await updates.ToAgentRunResponseAsync(cancellationToken: cancellationToken);

            ValidResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<ValidResponse>(agentResponse.Text, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                });
            }
            catch (JsonException ex)
            {
                activity?.SetTag("ValidatorExecutor.json_error", ex.Message);
                activity?.SetTag("ValidatorExecutor.response_text", agentResponse.Text);

                // Fallback: assume invalid if we can't parse the response
                response = new ValidResponse { Valid = false };
            }

            activity?.SetTag("ValidatorExecutor.validated", response?.Valid ?? false);

            response.Query = message;

            return response;
        }
    }

    public sealed class SplitExecutor(AIAgent agent) : Executor<ValidResponse>(nameof(SplitExecutor))
    {
        public static readonly ActivitySource ActivitySource = new($"MAF.{nameof(SplitExecutor)}", "1.0.0");
        private readonly AIAgent _agent = agent;

        public override async ValueTask HandleAsync(ValidResponse validResponse, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("HandleAsync", ActivityKind.Server);

            string messageClassified = string.Empty;
            await foreach (var msg in _agent.RunStreamingAsync(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, validResponse.Query), cancellationToken: cancellationToken))
            {
                messageClassified += msg.Text;
            }

            var orders = JsonSerializer.Deserialize<OrderDto>(messageClassified, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

            activity?.SetTag("SplitBaristaOrderComplete.send", orders?.BaristaItems.Count ?? 0);
            await context.SendMessageAsync(new BaristaOrderSplitted() { Items = orders.BaristaItems }, cancellationToken: cancellationToken);
            await context.AddEventAsync(new BaristaOrderSplitted(), cancellationToken);

            activity?.SetTag("SplitKitchenOrderComplete.send", orders?.KitchenItems.Count ?? 0);
            await context.SendMessageAsync(new KitchenOrderSplitted() { Items = orders.KitchenItems }, cancellationToken: cancellationToken);
            await context.AddEventAsync(new KitchenOrderSplitted(), cancellationToken);

            // Broadcast the turn token to kick off the agents.
            await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken: cancellationToken);
        }
    }

    public class BaristaExecuter(AIAgent agent) : Executor<BaristaOrderSplitted>(nameof(BaristaExecuter))
    {
        private readonly AIAgent _agent = agent;

        public override async ValueTask HandleAsync(BaristaOrderSplitted message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            var response = await _agent.RunAsync(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, JsonSerializer.Serialize(message.Items)), cancellationToken: cancellationToken);
            await context.SendMessageAsync(new CustomAgentResponse { Response = response }, cancellationToken: cancellationToken);
        }
    }

    public class KitchenExecuter(AIAgent agent) : Executor<KitchenOrderSplitted>(nameof(KitchenExecuter))
    {
        private readonly AIAgent _agent = agent;

        public override async ValueTask HandleAsync(KitchenOrderSplitted message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            var response = await _agent.RunAsync(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, JsonSerializer.Serialize(message.Items)), cancellationToken: cancellationToken);
            await context.SendMessageAsync(new CustomAgentResponse { Response = response }, cancellationToken: cancellationToken);
        }
    }

    private sealed class AggregationExecutor() : Executor<CustomAgentResponse>(nameof(AggregationExecutor))
    {
        public static readonly ActivitySource ActivitySource = new($"MAF.{nameof(AggregationExecutor)}", "1.0.0");
        private readonly List<string> _messages = [];

        public override async ValueTask HandleAsync(CustomAgentResponse message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("HandleAsync", ActivityKind.Server);

            if(message.Response.RawRepresentation is A2A.AgentTask task)
            {
                _messages.Add(task.Status.Message?.Parts?.LastOrDefault()?.AsTextPart()?.Text ?? "");
            }

            if (_messages.Count == 2)
            {
                activity?.SetTag("YieldOutputAsync.completed", true);

                await context.YieldOutputAsync(_messages.Aggregate("", (x, y) => $"{x}, {y}"), cancellationToken);
            }
        }
    }

    private sealed class HandleUncertainExecutor() : Executor<ValidResponse>(nameof(HandleUncertainExecutor))
    {
        public static readonly ActivitySource ActivitySource = new($"MAF.{nameof(HandleUncertainExecutor)}", "1.0.0");
        public override async ValueTask HandleAsync(ValidResponse validResponse, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("HandleAsync", ActivityKind.Server);
            // Handle uncertain order here, e.g., send a message back to the user for clarification
            var clarificationMessage = "Your order is unclear. Could you please clarify your order?";
            activity?.SetTag("HandleUncertainExecutor.clarificationSent", true);
            await context.YieldOutputAsync(clarificationMessage, cancellationToken);
        }
    }
}
