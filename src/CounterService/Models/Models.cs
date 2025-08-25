namespace CounterService.Models;

public class A2AServiceResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
    public string? Error { get; set; }
    public A2AResponseData A2AResponse { get; set; } = new();
    public McpResponseData McpResponse { get; set; } = new();
}

public class A2AResponseData
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Protocol { get; set; }
    public DateTime Timestamp { get; set; }
}

public class McpResponseData
{
    public bool Success { get; set; }
    public bool ToolExecuted { get; set; }
    public bool AdminAccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResponseContent { get; set; }
    public DateTime Timestamp { get; set; }
}
