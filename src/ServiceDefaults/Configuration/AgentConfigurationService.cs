using Microsoft.Extensions.Configuration;

namespace ServiceDefaults.Configuration;

/// <summary>
/// Service for validating and loading agent configurations securely.
/// Implements security best practice of validating configuration inputs.
/// Reference: .NET Security Best Practices - https://docs.microsoft.com/en-us/dotnet/standard/security/
/// </summary>
public interface IAgentConfigurationService
{
    /// <summary>
    /// Gets validated downstream agent endpoints
    /// </summary>
    Dictionary<string, string> GetDownstreamAgentEndpoints();

    /// <summary>
    /// Gets MCP server configuration
    /// </summary>
    McpServerConfiguration GetMcpServerConfiguration();

    /// <summary>
    /// Validates all configuration values
    /// </summary>
    /// <returns>Validation result with any errors</returns>
    ConfigurationValidationResult ValidateConfiguration();
}

/// <summary>
/// MCP server configuration
/// </summary>
public record McpServerConfiguration(string Url, string ClientName);

/// <summary>
/// Configuration validation result
/// </summary>
public record ConfigurationValidationResult(bool IsValid, List<string> Errors);

/// <summary>
/// Implementation of agent configuration service with validation
/// </summary>
public class AgentConfigurationService : IAgentConfigurationService
{
    private readonly IConfiguration _configuration;

    public AgentConfigurationService(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public Dictionary<string, string> GetDownstreamAgentEndpoints()
    {
        var endpoints = new Dictionary<string, string>
        {
            { 
                _configuration[AgentConstants.ConfigurationKeys.BaristaServiceKey] ?? AgentConstants.AgentTypes.Barista,
                _configuration[AgentConstants.ConfigurationKeys.BaristaServiceUrl] ?? AgentConstants.Defaults.BaristaServiceUrl 
            },
            { 
                _configuration[AgentConstants.ConfigurationKeys.KitchenServiceKey] ?? AgentConstants.AgentTypes.Kitchen,
                _configuration[AgentConstants.ConfigurationKeys.KitchenServiceUrl] ?? AgentConstants.Defaults.KitchenServiceUrl 
            }
        };

        return endpoints;
    }

    public McpServerConfiguration GetMcpServerConfiguration()
    {
        var url = _configuration[AgentConstants.ConfigurationKeys.McpServerUrl] ?? AgentConstants.Defaults.McpServerUrl;
        var clientName = _configuration[AgentConstants.ConfigurationKeys.McpServerClientName] ?? AgentConstants.Defaults.McpServerClientName;

        return new McpServerConfiguration(url, clientName);
    }

    public ConfigurationValidationResult ValidateConfiguration()
    {
        var errors = new List<string>();

        // Validate URLs
        var endpoints = GetDownstreamAgentEndpoints();
        foreach (var (key, url) in endpoints)
        {
            if (!IsValidUrl(url))
            {
                errors.Add($"Invalid URL for agent {key}: {url}");
            }
        }

        var mcpConfig = GetMcpServerConfiguration();
        if (!IsValidUrl(mcpConfig.Url))
        {
            errors.Add($"Invalid MCP server URL: {mcpConfig.Url}");
        }

        if (string.IsNullOrWhiteSpace(mcpConfig.ClientName))
        {
            errors.Add("MCP client name is required");
        }

        return new ConfigurationValidationResult(errors.Count == 0, errors);
    }

    private static bool IsValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && 
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}