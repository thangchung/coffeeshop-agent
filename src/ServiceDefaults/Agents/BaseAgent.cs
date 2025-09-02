using System.Diagnostics;
using A2A;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

namespace ServiceDefaults.Agents;

/// <summary>
/// Abstract base class for all agent implementations.
/// Implements the Template Method Pattern to provide common task processing logic while allowing 
/// specialized implementations to override specific behavior.
/// Follows DRY principle by consolidating common agent functionality.
/// 
/// Now includes Azure AD authentication integration for secure agent communication.
/// All agents require Microsoft Entra ID JWT Bearer token authentication with 'access_as_user' scope.
/// 
/// Reference: Clean Code by Robert Martin - Chapter 14: Successive Refinement
/// Reference: Gang of Four Design Patterns - Template Method Pattern
/// Reference: .NET Security Best Practices - https://docs.microsoft.com/en-us/dotnet/standard/security/
/// </summary>
public abstract class BaseAgent : IAgent, ITaskProcessor, IAgentCardProvider
{
    protected ITaskManager? _taskManager;
    protected readonly ILogger Logger;
    protected readonly ActivitySource ActivitySource;
    protected readonly IHttpContextAccessor HttpContextAccessor;

    protected BaseAgent(ILogger logger, string activitySourceName, IHttpContextAccessor httpContextAccessor)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ActivitySource = new ActivitySource(activitySourceName, "1.0.0");
        HttpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    /// <summary>
    /// Attaches the agent to a task manager
    /// </summary>
    public virtual void Attach(ITaskManager taskManager)
    {
        _taskManager = taskManager;
        _taskManager.OnTaskCreated = OnTaskCreatedAsync;
        _taskManager.OnTaskUpdated = OnTaskUpdatedAsync;
        _taskManager.OnAgentCardQuery = GetAgentCardAsync;
    }

    /// <summary>
    /// Handles task creation events with common logging and processing
    /// </summary>
    protected virtual async Task OnTaskCreatedAsync(AgentTask task, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("OnTaskCreated", ActivityKind.Server);
        activity?.SetTag("task.id", task.Id);

        Logger.LogInformation("Task created with ID: {TaskId}", task.Id);
        await ProcessTaskAsync(task, cancellationToken);
    }

    /// <summary>
    /// Handles task update events with common logging and processing
    /// </summary>
    protected virtual async Task OnTaskUpdatedAsync(AgentTask task, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("OnTaskUpdated", ActivityKind.Server);
        activity?.SetTag("task.id", task.Id);

        Logger.LogInformation("Task updated with ID: {TaskId}", task.Id);
        await ProcessTaskAsync(task, cancellationToken);
    }

    /// <summary>
    /// Template method for task processing - provides common error handling and validation
    /// while delegating specific processing logic to derived classes.
    /// Now includes authentication validation for secure task processing.
    /// </summary>
    public virtual async Task ProcessTaskAsync(AgentTask task, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("ProcessTask", ActivityKind.Server);
        activity?.SetTag("task.id", task.Id);

        if (_taskManager == null)
        {
            throw new InvalidOperationException(AgentConstants.ErrorMessages.TaskManagerNotAttached);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning(AgentConstants.ErrorMessages.TaskProcessingCancelled, task.Id);
            return;
        }

        // Authentication validation
        var authValidationResult = await ValidateAuthenticationAsync(task, cancellationToken);
        if (!authValidationResult.IsAuthenticated)
        {
            return; // Error already handled in ValidateAuthenticationAsync
        }

        try
        {
            // Template method: delegate specific processing to derived classes
            await ProcessTaskCoreAsync(task, cancellationToken);

            Logger.LogInformation("Task {TaskId} completed successfully", task.Id);
        }
        catch (Exception ex)
        {
            await HandleTaskErrorAsync(task, ex, cancellationToken);
        }
    }

    /// <summary>
    /// Validates authentication for the current request.
    /// Returns authentication details including JWT token for downstream service calls.
    /// </summary>
    protected virtual async Task<AuthenticationResult> ValidateAuthenticationAsync(AgentTask task, CancellationToken cancellationToken)
    {
        var httpContext = HttpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            await _taskManager!.UpdateStatusAsync(
                task.Id,
                TaskState.AuthRequired,
                new Message
                {
                    Parts = [new TextPart { Text = AgentConstants.ErrorMessages.UserNotAuthenticated }]
                },
                final: true,
                cancellationToken: cancellationToken);
            return new AuthenticationResult { IsAuthenticated = false };
        }

        var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
        string? jwtToken = null;
        if (authHeader != null && authHeader.StartsWith("Bearer "))
        {
            jwtToken = authHeader.Substring("Bearer ".Length).Trim();
        }

        var userEmail = httpContext.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn")?.Value;

        if (string.IsNullOrEmpty(jwtToken) || string.IsNullOrEmpty(userEmail))
        {
            await _taskManager!.UpdateStatusAsync(
                task.Id,
                TaskState.AuthRequired,
                new Message
                {
                    Parts = [new TextPart { Text = $"Missing authentication information - JWT token: {(jwtToken != null ? "present" : "missing")}, User email: {(userEmail != null ? "present" : "missing")}" }]
                },
                final: true,
                cancellationToken: cancellationToken);
            return new AuthenticationResult { IsAuthenticated = false };
        }

        return new AuthenticationResult 
        { 
            IsAuthenticated = true, 
            JwtToken = jwtToken, 
            UserEmail = userEmail 
        };
    }

    /// <summary>
    /// Abstract method for core task processing logic - must be implemented by derived classes
    /// This implements the Template Method pattern
    /// </summary>
    protected abstract Task ProcessTaskCoreAsync(AgentTask task, CancellationToken cancellationToken);

    /// <summary>
    /// Common error handling for all agents with secure error reporting
    /// Follows security best practice of not exposing sensitive error details
    /// Reference: .NET Security Best Practices - https://docs.microsoft.com/en-us/dotnet/standard/security/
    /// </summary>
    protected virtual async Task HandleTaskErrorAsync(AgentTask task, Exception ex, CancellationToken cancellationToken)
    {
        // Log full exception details for debugging (logs should be secure)
        Logger.LogError(ex, "Error processing task {TaskId}", task.Id);

        // Return sanitized error message to prevent information disclosure
        var errorMessage = GetSanitizedErrorMessage(ex);

        await _taskManager!.UpdateStatusAsync(
            task.Id,
            TaskState.Failed,
            new Message
            {
                Parts = [new TextPart { Text = errorMessage }]
            },
            final: true,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sanitizes error messages to prevent sensitive information disclosure
    /// </summary>
    protected virtual string GetSanitizedErrorMessage(Exception ex)
    {
        // Return generic error message to prevent information disclosure
        // Log specific details separately for debugging
        return "An error occurred while processing the request. Please try again later.";
    }

    /// <summary>
    /// Abstract method for getting agent card - must be implemented by derived classes
    /// </summary>
    public abstract Task<AgentCard> GetAgentCardAsync(string agentUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Common agent capabilities used by most agents
    /// </summary>
    protected virtual AgentCapabilities GetDefaultCapabilities()
    {
        return new AgentCapabilities
        {
            Streaming = true,
            PushNotifications = false,
        };
    }

    /// <summary>
    /// Dispose pattern for ActivitySource cleanup
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            ActivitySource?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Result of authentication validation containing authentication status and user details
/// </summary>
public class AuthenticationResult
{
    public bool IsAuthenticated { get; set; }
    public string? JwtToken { get; set; }
    public string? UserEmail { get; set; }
}