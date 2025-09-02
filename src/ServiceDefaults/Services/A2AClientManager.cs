using A2A;
using ServiceDefaults.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ServiceDefaults.Services;

/// <summary>
/// Interface for A2A client management operations.
/// Follows Single Responsibility Principle (SRP) by focusing only on A2A client management.
/// Now includes support for authenticated HTTP clients with JWT tokens.
/// Reference: SOLID Principles in C# - https://docs.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/architectural-principles#solid
/// </summary>
public interface IA2AClientManager
{
    /// <summary>
    /// Initializes A2A clients for downstream services with authentication
    /// </summary>
    /// <param name="jwtToken">JWT token for authenticated requests</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InitializeClientsAsync(string? jwtToken = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets A2A clients by their keys
    /// </summary>
    IReadOnlyDictionary<string, A2AClient> GetClients();

    /// <summary>
    /// Gets a specific A2A client by key
    /// </summary>
    /// <param name="key">The agent key</param>
    /// <returns>The A2A client or null if not found</returns>
    A2AClient? GetClient(string key);
}

/// <summary>
/// Service for managing A2A clients and their connections.
/// Implements Dependency Inversion Principle (DIP) by depending on abstractions.
/// </summary>
public class A2AClientManager : IA2AClientManager
{
    private readonly IAgentConfigurationService _configurationService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<A2AClientManager> _logger;
    private readonly Dictionary<string, A2AClient> _clients = new();
    private readonly ActivitySource _activitySource = new("A2A.ClientManager", "1.0.0");

    public A2AClientManager(
        IAgentConfigurationService configurationService,
        IHttpClientFactory httpClientFactory,
        ILogger<A2AClientManager> logger)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeClientsAsync(string? jwtToken = null, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("InitializeClients");

        var endpoints = _configurationService.GetDownstreamAgentEndpoints();
        
        foreach (var (key, endpoint) in endpoints)
        {
            try
            {
                using var clientActivity = _activitySource.StartActivity("InitializeClient");
                clientActivity?.SetTag("agent.key", key);
                clientActivity?.SetTag("agent.endpoint", endpoint);

                _logger.LogDebug("Initializing A2A client for agent: {AgentKey} at {Endpoint}", key, endpoint);

                // Create authenticated HTTP client with JWT token
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Clear();
                
                if (!string.IsNullOrEmpty(jwtToken))
                {
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {jwtToken}");
                    _logger.LogDebug("Added JWT authentication header for agent: {AgentKey}", key);
                }

                var cardResolver = new A2ACardResolver(new Uri(endpoint), httpClient: httpClient);
                var agentCard = await cardResolver.GetAgentCardAsync();
                
                clientActivity?.SetTag("agent.card.url", agentCard.Url);
                
                _logger.LogDebug("Resolved agent card for {AgentKey}: {AgentUrl}", key, agentCard.Url);

                var client = new A2AClient(new Uri(agentCard.Url), httpClient);
                
                if (!_clients.ContainsKey(key))
                {
                    _clients.Add(key, client);
                    _logger.LogInformation("Successfully initialized A2A client for agent: {AgentKey}", key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize A2A client for agent: {AgentKey} at {Endpoint}", key, endpoint);
                // Continue with other clients even if one fails
            }
        }

        activity?.SetTag("clients.count", _clients.Count);
        _logger.LogInformation("A2A client initialization completed. {ClientCount} clients ready", _clients.Count);
    }

    public IReadOnlyDictionary<string, A2AClient> GetClients()
    {
        return _clients.AsReadOnly();
    }

    public A2AClient? GetClient(string key)
    {
        _clients.TryGetValue(key, out var client);
        return client;
    }

    public void Dispose()
    {
        _activitySource?.Dispose();
    }
}