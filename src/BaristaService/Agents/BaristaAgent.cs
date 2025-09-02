using ServiceDefaults;
using ServiceDefaults.Agents;
using Microsoft.Extensions.Logging;

namespace BaristaService.Agents;

/// <summary>
/// Barista agent implementation using the common SimpleAgent base class.
/// This eliminates code duplication and follows DRY principles.
/// Reference: Clean Code by Robert Martin - Chapter 17: Smells and Heuristics (G5: Duplication)
/// </summary>
public class BaristaAgent : SimpleAgent
{
    public BaristaAgent(IHttpContextAccessor httpContextAccessor, ILogger<BaristaAgent> logger)
        : base(
            logger,
            AgentConstants.ActivitySources.Barista,
            httpContextAccessor,
            "Barista Service Agent",
            "A2A server agent that processes messages and integrates with MCP server for admin users.")
    {
    }
}
