using System.Diagnostics;
using A2A;

namespace KitchenService.Agents;

public class KitchenAgent(IHttpContextAccessor httpContextAccessor, IConfiguration configuration, ILogger<KitchenAgent> logger)
{
    private ITaskManager? _taskManager;
    public IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;
    public IConfiguration Configuration { get; } = configuration;
    public ILogger<KitchenAgent> Logger { get; } = logger;

    public static readonly ActivitySource ActivitySource = new($"A2A.{nameof(KitchenAgent)}", "1.0.0");

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
        using var activity = ActivitySource.StartActivity("OnTaskUpdated", ActivityKind.Server);
        activity?.SetTag("task.id", task.Id);

        if (_taskManager == null)
        {
            throw new InvalidOperationException("TaskManager is not attached.");
        }

        var httpContext = HttpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            await _taskManager.UpdateStatusAsync(
                task.Id,
                TaskState.AuthRequired,
                new AgentMessage
                {
                    Parts = [new TextPart { Text = "User is not authenticated" }]
                },
                final: true,
                cancellationToken: cancellationToken);
            return;
        }

        try
        {
            // Complete the task
            await _taskManager.UpdateStatusAsync(
                task.Id,
                TaskState.Completed,
                new AgentMessage
                {
                    Parts = [new TextPart { Text = "Message processed successfully" }]
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
                new AgentMessage
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
            Name = "Kitchen Service Agent",
            Description = "Kitchen service hosts an A2A server agent that processes an incoming messages with payload contains kitchen items.",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = capabilities,
            Skills = [
                new AgentSkill
                {
                    Name = "process_order",
                    Description = "Process an incoming messages with payload contains kitchen items."
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
                                { $"api://{Configuration["AzureAd:ClientId"]}/CoffeeShop.Kitchen.ReadWrite", "Access the Kitchen Service as the signed-in user" }
                            })
                    },
                    "OAuth2 with JWT Bearer tokens"
                )
            },
            Security =
            [
                new Dictionary<string, string[]>
                {
                    { "Bearer", ["CoffeeShop.Kitchen.ReadWrite"] }
                }
            ]
        });
    }
}
