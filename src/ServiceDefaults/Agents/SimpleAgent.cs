using A2A;
using Microsoft.Extensions.Logging;

namespace ServiceDefaults.Agents;

/// <summary>
/// Simple agent implementation for basic task processing (used by Barista and Kitchen services).
/// This consolidates the common functionality that was duplicated between BaristaAgent and KitchenAgent.
/// Follows DRY principle by eliminating code duplication.
/// Reference: Clean Code by Robert Martin - Chapter 17: Smells and Heuristics (G5: Duplication)
/// </summary>
public class SimpleAgent : BaseAgent
{
    private readonly string _agentName;
    private readonly string _agentDescription;
    private readonly string _skillName;
    private readonly string _skillDescription;

    public SimpleAgent(
        ILogger logger,
        string activitySourceName,
        string agentName,
        string agentDescription,
        string skillName = "process_order",
        string skillDescription = "Process messages and communicate with MCP server for admin users. Requires JWT authentication with admin role and 'access_as_user' scope.")
        : base(logger, activitySourceName)
    {
        _agentName = agentName;
        _agentDescription = agentDescription;
        _skillName = skillName;
        _skillDescription = skillDescription;
    }

    /// <summary>
    /// Core task processing for simple agents - completes tasks with success message
    /// </summary>
    protected override async Task ProcessTaskCoreAsync(AgentTask task, CancellationToken cancellationToken)
    {
        // Complete the task with a success message
        await _taskManager!.UpdateStatusAsync(
            task.Id,
            TaskState.Completed,
            new Message
            {
                Parts = [new TextPart { Text = "Message processed successfully" }]
            },
            final: true,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Gets the agent card with configurable details
    /// </summary>
    public override Task<AgentCard> GetAgentCardAsync(string agentUrl, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AgentCard>(cancellationToken);
        }

        var capabilities = GetDefaultCapabilities();

        // Note: Authentication is implemented at the HTTP transport level using Microsoft Entra ID
        // JWT Bearer tokens are required for all endpoints and are validated by the middleware
        // The authentication scheme used is "Bearer" with JWT tokens containing required scopes
        return Task.FromResult(new AgentCard
        {
            Name = _agentName,
            Description = $"{_agentDescription} " +
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
                    Name = _skillName,
                    Description = _skillDescription
                }
            ],
        });
    }
}