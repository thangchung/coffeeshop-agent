using A2A;

namespace ServiceDefaults.Agents;

/// <summary>
/// Base interface for all agent implementations.
/// Implements Interface Segregation Principle (ISP) by keeping the interface focused and minimal.
/// Reference: SOLID Principles in C# - https://docs.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/architectural-principles#solid
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Attaches the agent to a task manager for task processing
    /// </summary>
    /// <param name="taskManager">The task manager instance</param>
    void Attach(ITaskManager taskManager);
}

/// <summary>
/// Interface for task processing operations
/// Follows Single Responsibility Principle (SRP) by separating task processing concerns
/// </summary>
public interface ITaskProcessor
{
    /// <summary>
    /// Processes an agent task asynchronously
    /// </summary>
    /// <param name="task">The task to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task ProcessTaskAsync(AgentTask task, CancellationToken cancellationToken);
}

/// <summary>
/// Interface for agent card operations
/// Follows Interface Segregation Principle (ISP) by separating agent card concerns
/// </summary>
public interface IAgentCardProvider
{
    /// <summary>
    /// Gets the agent card for the agent
    /// </summary>
    /// <param name="agentUrl">The agent URL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The agent card</returns>
    Task<AgentCard> GetAgentCardAsync(string agentUrl, CancellationToken cancellationToken);
}