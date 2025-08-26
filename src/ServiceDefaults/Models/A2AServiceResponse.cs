namespace ServiceDefaults.Models;

/// <summary>
/// Standardized response model for A2A service operations
/// </summary>
public class A2AServiceResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
    public string? Error { get; set; }
    public A2AResponseData A2AResponse { get; set; } = new();
    public McpResponseData McpResponse { get; set; } = new();
}

/// <summary>
/// Data model for A2A response details
/// </summary>
public class A2AResponseData
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Protocol { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Data model for MCP response details
/// </summary>
public class McpResponseData
{
    public bool Success { get; set; }
    public bool ToolExecuted { get; set; }
    public bool AdminAccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResponseContent { get; set; }
    public DateTime Timestamp { get; set; }
}