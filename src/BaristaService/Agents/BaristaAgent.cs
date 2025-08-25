using System.Diagnostics;
using A2A;

namespace BaristaService.Agents;

public class BaristaAgent(IHttpContextAccessor httpContextAccessor, ILogger<BaristaAgent> logger)
{
    private ITaskManager? _taskManager;
    public IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;
    public ILogger<BaristaAgent> Logger { get; } = logger;

    public static readonly ActivitySource ActivitySource = new($"A2A.{nameof(BaristaAgent)}", "1.0.0");

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

        try
        {
            // Complete the task
            await _taskManager.UpdateStatusAsync(
                task.Id,
                TaskState.Completed,
                new Message
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

        // Note: Authentication is implemented at the HTTP transport level using Microsoft Entra ID
        // JWT Bearer tokens are required for all endpoints and are validated by the middleware
        // The authentication scheme used is "Bearer" with JWT tokens containing required scopes
        return Task.FromResult(new AgentCard
        {
            Name = "Barista Service Agent",
            Description = "A2A server agent that processes messages and integrates with MCP server for admin users. " +
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
                    Name = "process_order",
                    Description = "Process messages and communicate with MCP server for admin users. " +
                                 "Requires JWT authentication with admin role and 'access_as_user' scope."
                }
            ],
        });
    }
}
