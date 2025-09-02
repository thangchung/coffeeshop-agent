namespace ServiceDefaults;

/// <summary>
/// Shared constants for agent services to eliminate magic strings and improve maintainability.
/// Reference: Clean Code by Robert Martin - Chapter 17: Smells and Heuristics (G25: Replace Magic Numbers with Named Constants)
/// </summary>
public static class AgentConstants
{
    /// <summary>
    /// Agent type identifiers for downstream service configuration
    /// </summary>
    public static class AgentTypes
    {
        public const string Barista = "BARISTA";
        public const string Kitchen = "KITCHEN";
        public const string Counter = "COUNTER";
    }

    /// <summary>
    /// Configuration section keys for agent services
    /// </summary>
    public static class ConfigurationKeys
    {
        public const string BaristaServiceKey = "BaristaService:Key";
        public const string BaristaServiceUrl = "BaristaService:Url";
        public const string KitchenServiceKey = "KitchenService:Key";
        public const string KitchenServiceUrl = "KitchenService:Url";
        public const string McpServerUrl = "McpServer:Url";
        public const string McpServerClientName = "McpServer:ClientName";
    }

    /// <summary>
    /// Default values for agent configurations
    /// </summary>
    public static class Defaults
    {
        public const string BaristaServiceUrl = "https+http://barista";
        public const string KitchenServiceUrl = "https+http://kitchen";
        public const string ProductServiceUrl = "https+http://product";
        public const string McpServerUrl = "https+http://product/mcp";
        public const string McpServerClientName = "product-catalog-service";
    }

    /// <summary>
    /// Azure AD configuration keys for authentication
    /// </summary>
    public static class AzureAdKeys
    {
        public const string Instance = "AzureAd:Instance";
        public const string TenantId = "AzureAd:TenantId";
        public const string ClientId = "AzureAd:ClientId";
        public const string Scope = "AzureAd:Scope";
    }

    /// <summary>
    /// Authentication policy names
    /// </summary>
    public static class AuthenticationPolicies
    {
        public const string AdminOnly = "AdminOnly";
        public const string RequiredScope = "http://schemas.microsoft.com/identity/claims/scope";
        public const string RequiredScopeValue = "admin";
    }

    /// <summary>
    /// Common error messages for consistent error handling
    /// </summary>
    public static class ErrorMessages
    {
        public const string TaskManagerNotAttached = "TaskManager is not attached.";
        public const string NoMessageContent = "No message content found in task";
        public const string NoTextContent = "No text content found in message";
        public const string TaskProcessingCancelled = "Task processing cancelled for ID: {0}";
        public const string UnexpectedResponseType = "Unexpected response type from A2A protocol";
        public const string UserNotAuthenticated = "User is not authenticated";
        public const string AuthenticationRequired = "Authentication is required. Please provide a valid JWT token.";
        public const string MissingAuthenticationInfo = "Missing authentication information";
    }

    /// <summary>
    /// Activity source names for OpenTelemetry tracing
    /// </summary>
    public static class ActivitySources
    {
        public const string Counter = "A2A.CounterAgent";
        public const string Barista = "A2A.BaristaAgent";
        public const string Kitchen = "A2A.KitchenAgent";
    }
}