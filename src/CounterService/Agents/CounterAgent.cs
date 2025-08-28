using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using A2A;
using CounterService.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using ModelContextProtocol.Client;

namespace CounterService.Agents;

public class CounterAgent(
    Kernel kernel,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ILogger<CounterAgent> logger)
{
    private ITaskManager? _taskManager;
    public static readonly ActivitySource ActivitySource = new($"A2A.{nameof(CounterAgent)}", "1.0.0");

    public ILogger<CounterAgent> Logger { get; } = logger;
    public Kernel Kernel { get; } = kernel;
    public IConfiguration Configuration { get; } = configuration;
    public IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;
    public IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;
    public Dictionary<string, string> DownStreamAgentEndpoints { get; set; } = new Dictionary<string, string> {
            { configuration["BaristaService:Key"] ?? "BARISTA", configuration["BaristaService:Url"] ?? "http://localhost:5002" },
            { configuration["KitchenService:Key"] ?? "KITCHEN", configuration["KitchenService:Url"] ?? "http://localhost:5003" }
    };
    public Dictionary<string, A2AClient> A2AClients { get; set; } = [];

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

        foreach (var (key, endpoint) in DownStreamAgentEndpoints)
        {
            activity?.SetTag($"downstream.{key.ToLower()}.endpoint", endpoint);
            Logger.LogDebug("Configured downstream agent endpoint: {Endpoint}", endpoint);

            var httpClient = HttpClientFactory.CreateClient();

            A2ACardResolver cardResolver = new(new Uri(endpoint));
            AgentCard agentCard = await cardResolver.GetAgentCardAsync();
            activity?.SetTag($"downstream.{key.ToLower()}.agentCard.url", agentCard.Url);
            Logger.LogDebug("Resolved Agent card: {Endpoint}", $"{agentCard.Url}");

            var client = new A2AClient(new Uri(agentCard.Url), httpClient);
            Logger.LogDebug("Created A2A client for endpoint: {Endpoint}", $"{agentCard.Url}");

            if(!A2AClients.ContainsKey(key))
                A2AClients.Add(key, client);
        }

        Logger.LogInformation("Task created with ID: {TaskId}", task.Id);
        await ProcessTaskAsync(task, cancellationToken);
    }

    private async Task OnTaskUpdatedAsync(AgentTask task, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("OnTaskUpdated", ActivityKind.Server);
        activity?.SetTag("task.id", task.Id);

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
                    new Message
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
                    new Message
                    {
                        Parts = [new TextPart { Text = "No text content found in message" }]
                    },
                    final: true,
                    cancellationToken: cancellationToken);
                return;
            }

            // todo: process authn and authz info
            // ...

            // Update task status to Working
            await _taskManager.UpdateStatusAsync(
                task.Id,
                TaskState.Working,
                new Message
                {
                    Parts = [new TextPart { Text = $"Processing ping message via A2A protocol: {messageText}" }]
                },
                cancellationToken: cancellationToken);

            // Send message via A2A protocol to Pong Service
            Logger.LogInformation("Sending A2A message to Pong Service for user: {UserEmail}", "todo@todo.com");

            var a2aClients = await ParseInputMessage(Kernel, messageText, isStub: true, cancellationToken: cancellationToken);
            activity?.SetTag("a2a.clients.count", a2aClients.Count);

            Logger.LogInformation("Sending A2A message with authentication in HTTP headers");

            foreach (var (a2aClient, items) in a2aClients)
            {
                // Create A2A message with minimal metadata (authentication is in HTTP headers now)
                var a2aMessage = new Message
                {
                    Role = MessageRole.User,
                    MessageId = Guid.NewGuid().ToString(),
                    ContextId = Guid.NewGuid().ToString(),
                    Parts = [new TextPart { Text = messageText }],
                    Metadata = new Dictionary<string, JsonElement>
                    {
                        ["items"] = JsonSerializer.SerializeToElement(items),
                        ["timestamp"] = JsonSerializer.SerializeToElement(DateTime.UtcNow.ToString("O"))
                    }
                };

                // Create MessageSendParams for A2A protocol
                var messageSendParams = new MessageSendParams
                {
                    Message = a2aMessage,
                    Configuration = new MessageSendConfiguration
                    {
                        AcceptedOutputModes = ["text"],
                        Blocking = true
                    }
                };

                // Send message via A2A protocol with authenticated HTTP client
                var a2aResponse = await a2aClient.SendMessageAsync(messageSendParams);
                activity?.SetTag("a2a.response.type", a2aResponse?.GetType().Name ?? "null");
                var response = MapResponseMessage(a2aResponse);

                // Extract the response content in a readable format
                string responseText;
                if (response.Success && response.Data != null)
                {
                    // Try to extract the actual response from the task
                    if (response.Data.GetType().GetProperty("Response")?.GetValue(response.Data) is string taskResponse)
                    {
                        responseText = $"Success! Pong Service responded: {taskResponse}";
                    }
                    else
                    {
                        responseText = $"A2A task completed successfully. Task ID: {response.Data}";
                    }
                }
                else
                {
                    responseText = $"A2A communication failed: {response.Message ?? "Unknown error"}";
                }

                // Return a clean, readable response
                await _taskManager.ReturnArtifactAsync(
                    task.Id,
                    new Artifact
                    {
                        Parts = [new TextPart { Text = responseText }]
                    },
                    cancellationToken);
            }

            // Complete the task
            await _taskManager.UpdateStatusAsync(
                task.Id,
                TaskState.Completed,
                new Message
                {
                    Parts = [new TextPart { Text = "Ping message sent successfully via A2A protocol" }]
                },
                final: true,
                cancellationToken: cancellationToken);

            Logger.LogInformation("Task {TaskId} completed successfully", task.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing task {TaskId}", task.Id);

            await _taskManager.UpdateStatusAsync(
                task.Id,
                TaskState.Failed,
                new Message
                {
                    Parts = [new TextPart { Text = $"Error processing ping message: {ex.Message}" }]
                },
                final: true,
                cancellationToken: cancellationToken);
        }
    }

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
            Description = "A2A client agent that sends messages through the A2A protocol to the Barista and Kitchen services. " +
                         "AUTHENTICATION REQUIRED: This agent requires Microsoft Entra ID JWT Bearer token authentication " +
                         "with 'access_as_user' scope. All requests must include valid JWT tokens in the Authorization header.",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = capabilities,
            Skills = [
                new AgentSkill
                {
                    Name = "send_order",
                    Description = "Send messages via A2A protocol to Pong Service with MCP integration. " +
                                 "Requires JWT authentication with valid user identity and 'access_as_user' scope."
                }
            ],
        });
    }

    private A2AServiceResponse MapResponseMessage(A2AResponse response)
    {
        switch (response)
        {
            case AgentTask task:
                {
                    Logger.LogInformation("A2A task created successfully with ID: {TaskId}, Status: {TaskStatus}",
                                    task.Id, task.Status.State.ToString());

                    return new A2AServiceResponse
                    {
                        Success = true,
                        Message = "A2A task created successfully",
                        Data = new
                        {
                            TaskId = task.Id,
                            Status = task.Status.State.ToString(),
                            Response = task.Artifacts?.FirstOrDefault()?.Parts?.OfType<TextPart>()?.FirstOrDefault()?.Text ?? "Task created"
                        }
                    };
                }
            case Message messageResponse:
                {
                    Logger.LogInformation("Received A2A message response");

                    var responseText = messageResponse.Parts?.OfType<TextPart>()?.FirstOrDefault()?.Text ?? "No response content";

                    return new A2AServiceResponse
                    {
                        Success = true,
                        Message = "A2A message sent successfully",
                        Data = new
                        {
                            Response = responseText,
                            MessageId = messageResponse.MessageId
                        }
                    };
                }

            default:
                {
                    Logger.LogWarning("Unexpected A2A response type: {ResponseType}", response?.GetType().Name ?? "null");

                    return new A2AServiceResponse
                    {
                        Success = false,
                        Message = "Unexpected response type from A2A protocol",
                        Error = $"Unknown response format: {response?.GetType().Name ?? "null"}"
                    };
                }
        }
    }

    private async Task<Dictionary<A2AClient, List<ItemTypeDto>>> ParseInputMessage(Kernel kernel, string messageText, bool isStub = false, CancellationToken cancellationToken = default)
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

        IMcpClient mcpClient = null;
        if (!isStub)
        {
            // mcp
            var serverUrl = Configuration["McpServer:Url"] ?? "http://localhost:5001";
            var clientName = Configuration["McpServer:ClientName"] ?? "product-catalog-service";

            var transport = new SseClientTransport(new()
            {
                Endpoint = new Uri($"{serverUrl}/mcp"),
                Name = clientName
            }, HttpClientFactory.CreateClient());

            // Create MCP client using the official factory
            mcpClient = await McpClientFactory.CreateAsync(transport, cancellationToken: cancellationToken);

            var tools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);

            var options = JsonSerializerOptions.Default;
            var exporterOptions = new JsonSchemaExporterOptions()
            {
                TreatNullObliviousAsNonNullable = true,
            };
            var schema = options.GetJsonSchemaAsNode(typeof(OrderDto), exporterOptions);
            var promptChunked = $$"""
            Parse a customer's message into a order object in valid JSON.
            Use your tool to extract the name, price, and item type of the customer's message.
            Use your tool to query and get the valid price of the item.
            The quantity of each item need keeping (if no quantity inputs from user, then auto-set to 1).
            Use the provided JSON schema for your reply (no markdown for formatting the JSON object needed):
            {{schema}}

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

            if (!kernel.Plugins.Contains("Tools"))
            {
                kernel.Plugins.AddFromFunctions("Tools", tools.Select(aiFunction => aiFunction.AsKernelFunction()));
            }

            var summaryAgent =
               new ChatCompletionAgent()
               {
                   Name = "ClassificationAgent",
                   Arguments = new KernelArguments(new PromptExecutionSettings()
                   { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true }) }),
                   Instructions = promptChunked,
                   Kernel = kernel
               };

            var message = new ChatMessageContent(AuthorRole.User, messageText);


            await foreach (var msg in summaryAgent.InvokeAsync(message, cancellationToken: cancellationToken))
            {
                messageClassified += msg.Message?.Content;
            }
        }

        var orders = JsonSerializer.Deserialize<OrderDto>(messageClassified, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        Dictionary<A2AClient, List<ItemTypeDto>> selectedClients = [];

        if (orders?.BaristaItems.Count > 0)
        {
            selectedClients.Add(A2AClients["BARISTA"], orders?.BaristaItems);
        }

        if (orders?.KitchenItems.Count > 0)
        {
            selectedClients.Add(A2AClients["KITCHEN"], orders?.KitchenItems);
        }

        if (mcpClient != null)
            GC.SuppressFinalize(mcpClient);

        return selectedClients;
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
}
