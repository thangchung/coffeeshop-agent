using ServiceDefaults;
using ServiceDefaults.Agents;
using Microsoft.Extensions.Logging;

namespace KitchenService.Agents;

/// <summary>
/// Kitchen agent implementation using the common SimpleAgent base class.
/// This eliminates code duplication and follows DRY principles.
/// Reference: Clean Code by Robert Martin - Chapter 17: Smells and Heuristics (G5: Duplication)
/// </summary>
public class KitchenAgent : SimpleAgent
{
    public KitchenAgent(IHttpContextAccessor httpContextAccessor, ILogger<KitchenAgent> logger)
        : base(
            logger,
            AgentConstants.ActivitySources.Kitchen,
            "Kitchen Service Agent",
            "A2A server agent that processes messages and integrates with MCP server for admin users.")
    {
        // Store httpContextAccessor if needed for future use
        HttpContextAccessor = httpContextAccessor;
    }

    public IHttpContextAccessor HttpContextAccessor { get; }
}
