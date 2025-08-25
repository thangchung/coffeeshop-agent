using System.Diagnostics;
using System.Text.Json;
using A2A;
using CounterService.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

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
    public IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;
    public IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;
    public Dictionary<string, string> DownStreamAgentEndpoints { get; set; } = new Dictionary<string, string> {
            { configuration["BaristaService:Key"] ?? "BARISTA", configuration["BaristaService:Url"] ?? "http://localhost:5002" }
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
            Logger.LogDebug("Configured downstream agent endpoint: {Endpoint}", endpoint);

            var httpClient = HttpClientFactory.CreateClient();

            A2ACardResolver cardResolver = new(new Uri(endpoint));
            AgentCard agentCard = await cardResolver.GetAgentCardAsync();
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
            // var a2aResponse = await A2AClientService.SendMessageAsync(messageText, "todo: no jwt token", "todo@todo.com");

            var a2aClient = await SmartAgentRouting(Kernel, messageText, cancellationToken);

            // Create A2A message with minimal metadata (authentication is in HTTP headers now)
            var a2aMessage = new Message
            {
                Role = MessageRole.User,
                MessageId = Guid.NewGuid().ToString(),
                ContextId = Guid.NewGuid().ToString(),
                Parts = [new TextPart { Text = messageText }],
                Metadata = new Dictionary<string, JsonElement>
                {
                    //["user_email"] = JsonSerializer.SerializeToElement(userEmail),
                    //["user_id"] = JsonSerializer.SerializeToElement(userEmail),
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

            Logger.LogInformation("Sending A2A message with authentication in HTTP headers");

            // Send message via A2A protocol with authenticated HTTP client
            var a2aResponse = await a2aClient.SendMessageAsync(messageSendParams);
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

    private async Task<A2AClient> SmartAgentRouting(Kernel kernel, string messageText, CancellationToken cancellationToken)
    {
        string promptChunked = """
                Classify the input message as BARISTA, or KITCHEN.
                Review: If the message is related to ordering, beverages, or counter service, classify it as BARISTA.
                If the message is related to food preparation, cooking, or kitchen service, classify it as KITCHEN.
                Otherwise, classify it as UNKNOWN.
                Respond with only the classification label: BARISTA, KITCHEN, or UNKNOWN.
                """;

        ChatCompletionAgent summaryAgent =
            new()
            {
                Name = "ClassificationAgent",
                Instructions = promptChunked,
                Kernel = kernel
            };

        var message = new ChatMessageContent(AuthorRole.User, messageText);
        var messageClassified = string.Empty;

        await foreach (var msg in summaryAgent.InvokeAsync(message, cancellationToken: cancellationToken))
        {
            messageClassified += msg.Message?.Content ?? "UNKNOWN";
        }

        switch (messageClassified.Trim().ToUpperInvariant())
        {
            case "BARISTA":
                Logger.LogInformation("Routing to Barista Service based on message classification");
                return A2AClients["BARISTA"];
            case "KITCHEN":
                Logger.LogInformation("Routing to Kitchen Service based on message classification");
                return A2AClients["KITCHEN"];
            default:
                Logger.LogWarning("Message classification UNKNOWN, defaulting to first available A2A client");
                throw new Exception("Cannot route to correct agents.");
        }
    }
}
