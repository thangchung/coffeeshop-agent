using System.Diagnostics;
using A2A;
using ServiceDefaults;
using ServiceDefaults.Agents;
using ServiceDefaults.Configuration;
using ServiceDefaults.Services;
using ServiceDefaults.Models;
using Microsoft.Extensions.Logging;

namespace CounterService.Agents;

/// <summary>
/// Refactored CounterAgent following SOLID principles and using dependency injection.
/// This implementation splits the previous monolithic CounterAgent into focused, 
/// single-responsibility services while maintaining the same functionality.
/// 
/// Improvements:
/// - SRP: Uses specialized services for each concern (validation, parsing, messaging, etc.)
/// - OCP: Extensible through dependency injection and configuration
/// - DIP: Depends on abstractions rather than concrete implementations
/// - Security: Implements input validation and secure error handling
/// 
/// Reference: Clean Code by Robert Martin - Chapter 14: Successive Refinement
/// Reference: SOLID Principles in C# - https://docs.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/architectural-principles#solid
/// </summary>
public class CounterAgent : BaseAgent
{
    private readonly IAgentConfigurationService _configurationService;
    private readonly IA2AClientManager _clientManager;
    private readonly IInputValidationService _validationService;
    private readonly IOrderParsingService _orderParsingService;
    private readonly IA2AMessageService _messageService;

    public CounterAgent(
        IAgentConfigurationService configurationService,
        IA2AClientManager clientManager,
        IInputValidationService validationService,
        IOrderParsingService orderParsingService,
        IA2AMessageService messageService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<CounterAgent> logger)
        : base(logger, AgentConstants.ActivitySources.Counter, httpContextAccessor)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _clientManager = clientManager ?? throw new ArgumentNullException(nameof(clientManager));
        _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        _orderParsingService = orderParsingService ?? throw new ArgumentNullException(nameof(orderParsingService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
    }

    /// <summary>
    /// Override to initialize A2A clients when task is created with authentication
    /// </summary>
    protected override async Task OnTaskCreatedAsync(AgentTask task, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("OnTaskCreated", ActivityKind.Server);
        activity?.SetTag("task.id", task.Id);

        // Get authentication information first
        var authResult = await ValidateAuthenticationAsync(task, cancellationToken);
        if (!authResult.IsAuthenticated)
        {
            return; // Error already handled in ValidateAuthenticationAsync
        }

        // Initialize A2A clients for downstream services with authentication
        await _clientManager.InitializeClientsAsync(authResult.JwtToken, cancellationToken);
        
        Logger.LogInformation("Task created with ID: {TaskId}", task.Id);
        await ProcessTaskCoreAsync(task, cancellationToken);
    }

    /// <summary>
    /// Core task processing implementation - much more focused and readable
    /// Now includes JWT token handling for authenticated service calls
    /// </summary>
    protected override async Task ProcessTaskCoreAsync(AgentTask task, CancellationToken cancellationToken)
    {
        // Get authentication info again for this call
        var authResult = await ValidateAuthenticationAsync(task, cancellationToken);
        if (!authResult.IsAuthenticated)
        {
            return; // Error already handled
        }

        // Step 1: Validate input
        var validationResult = _validationService.ValidateTask(task);
        if (!validationResult.IsValid)
        {
            await _taskManager!.UpdateStatusAsync(
                task.Id,
                TaskState.Failed,
                new AgentMessage { Parts = [new TextPart { Text = validationResult.ErrorMessage! }] },
                final: true,
                cancellationToken: cancellationToken);
            return;
        }

        var messageText = validationResult.TextContent!;

        // Step 2: Update task status to Working
        await _taskManager!.UpdateStatusAsync(
            task.Id,
            TaskState.Working,
            new AgentMessage
            {
                Parts = [new TextPart { Text = $"Processing order via A2A protocol: {messageText}" }]
            },
            cancellationToken: cancellationToken);

        // Step 3: Parse the order from the message with authentication
        Logger.LogInformation("Parsing customer order for task {TaskId}", task.Id);
        var order = await _orderParsingService.ParseOrderAsync(messageText, authResult.JwtToken, isStub: false, cancellationToken);

        // Step 4: Send A2A messages to appropriate services
        Logger.LogInformation("Sending A2A messages for {BaristaItems} barista items and {KitchenItems} kitchen items", 
            order.BaristaItems.Count, order.KitchenItems.Count);

        var responses = await _messageService.SendOrderMessagesAsync(messageText, order, cancellationToken);

        // Step 5: Process responses and return artifacts
        await ProcessA2AResponsesAsync(task, responses, cancellationToken);

        // Step 6: Complete the task
        await _taskManager.UpdateStatusAsync(
            task.Id,
            TaskState.Completed,
            new AgentMessage
            {
                Parts = [new TextPart { Text = "Order processed successfully via A2A protocol" }]
            },
            final: true,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Processes A2A responses and returns clean, readable responses to the user
    /// </summary>
    private async Task ProcessA2AResponsesAsync(AgentTask task, List<A2AServiceResponse> responses, CancellationToken cancellationToken)
    {
        foreach (var response in responses)
        {
            var responseText = ExtractReadableResponse(response);

            await _taskManager!.ReturnArtifactAsync(
                task.Id,
                new Artifact
                {
                    Parts = [new TextPart { Text = responseText }]
                },
                cancellationToken);
        }
    }

    /// <summary>
    /// Extracts readable response text from A2A service response
    /// </summary>
    private static string ExtractReadableResponse(A2AServiceResponse response)
    {
        if (response.Success && response.Data != null)
        {
            // Try to extract the actual response from the task
            if (response.Data.GetType().GetProperty("Response")?.GetValue(response.Data) is string taskResponse)
            {
                return $"Success! Service responded: {taskResponse}";
            }
            else
            {
                return $"A2A task completed successfully. Task ID: {response.Data}";
            }
        }
        else
        {
            return $"A2A communication failed: {response.Message ?? "Unknown error"}";
        }
    }

    /// <summary>
    /// Provides agent card with improved description
    /// </summary>
    public override Task<AgentCard> GetAgentCardAsync(string agentUrl, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AgentCard>(cancellationToken);
        }

        var capabilities = GetDefaultCapabilities();

        return Task.FromResult(new AgentCard
        {
            Name = "Counter Service Agent",
            Description = "A2A client agent that processes customer orders and coordinates with Barista and Kitchen services. " +
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
                    Description = "Process customer orders and coordinate with downstream services via A2A protocol. " +
                                 "Requires JWT authentication with valid user identity and 'access_as_user' scope."
                }
            ],
        });
    }

    /// <summary>
    /// Override to provide more specific error messages for counter agent
    /// </summary>
    protected override string GetSanitizedErrorMessage(Exception ex)
    {
        return ex switch
        {
            InvalidOperationException => "Service configuration error. Please contact support.",
            HttpRequestException => "Unable to communicate with downstream services. Please try again later.",
            TaskCanceledException => "Request timed out. Please try again.",
            _ => base.GetSanitizedErrorMessage(ex)
        };
    }
}