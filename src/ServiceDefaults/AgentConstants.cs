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
        public const string BaristaServiceUrl = "http://localhost:5002";
        public const string KitchenServiceUrl = "http://localhost:5003";
        public const string McpServerUrl = "http://localhost:5001";
        public const string McpServerClientName = "product-catalog-service";
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