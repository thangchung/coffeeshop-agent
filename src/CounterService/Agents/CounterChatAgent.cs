using System.Diagnostics;
using System.Text.Json;
using CounterService.Extentions;
using CounterService.Instructions;
using CounterService.Workflows;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Web;
using ModelContextProtocol.Client;

namespace CounterService.Agents;

public partial class CounterChatAgent(IChatClient chatClient,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ILogger<CounterChatAgent> logger) : AIAgent
{
    internal const string AgentName = $"A2A.{nameof(CounterChatAgent)}";
    public static readonly ActivitySource ActivitySource = new(AgentName, "1.0.0");

    public IChatClient ChatClient { get; } = chatClient;
    public IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;
    public IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;
    public ILogger<CounterChatAgent> Logger { get; } = logger;
    public McpClient? McpClient { get; private set; }

    public IConfiguration Configuration { get; } = configuration;
    public bool IgnoreAuth { get; set; } = configuration.GetValue("IgnoreAuth", false);

    // Helper method to get ITokenAcquisition from current HTTP context
    private ITokenAcquisition? GetTokenAcquisition()
    {
        return HttpContextAccessor.HttpContext?.RequestServices.GetService<ITokenAcquisition>();
    }

    internal static string SystemInstructionPrompt => InstructionExtensions.GetInstruction("CounterChatAgentInstruction");

    public override AgentThread GetNewThread()
        => new CustomAgentThread();

    public override AgentThread DeserializeThread(JsonElement serializedThread, JsonSerializerOptions? jsonSerializerOptions = null)
        => new CustomAgentThread(serializedThread, jsonSerializerOptions);

    public override Task<AgentRunResponse> RunAsync(IEnumerable<ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Obtain tokens and initialize clients BEFORE streaming starts to avoid scope disposal issues
        var tokenAcquisition = GetTokenAcquisition();
        if (tokenAcquisition == null && !IgnoreAuth)
        {
            throw new InvalidOperationException("TokenAcquisition service is not available in the current context");
        }

        var disposables = new List<IDisposable>();
        var (mcpClient, a2aBaristaAIAgent, a2aKitchenAIAgent) = await InitializeClientsAsync(HttpClientFactory, tokenAcquisition, cancellationToken);
        if (mcpClient is IDisposable d1) disposables.Add(d1);
        if (a2aBaristaAIAgent is IDisposable d2) disposables.Add(d2);
        if (a2aKitchenAIAgent is IDisposable d3) disposables.Add(d3);

        ArgumentNullException.ThrowIfNull(mcpClient);
        ArgumentNullException.ThrowIfNull(a2aBaristaAIAgent);
        ArgumentNullException.ThrowIfNull(a2aKitchenAIAgent);

        var workflow = await OrderPlacementWorkflowExtentions.BuildWorkflowCoreAsync(ChatClient, mcpClient, a2aBaristaAIAgent, a2aKitchenAIAgent, workflowName: $"{nameof(CounterChatAgent)}-workflow", cancellationToken)
            ?? throw new InvalidOperationException("Failed to create workflow");

        try
        {
            var lastChatMsg = messages.Where(m => m.Role == ChatRole.User).LastOrDefault()!;

            await foreach (AgentRunResponseUpdate update in ExecuteWorkflowAsync(workflow, lastChatMsg.Text!, cancellationToken: cancellationToken))
            {
                yield return update;
            }
        }
        finally
        {
            if (workflow is IDisposable disposableWorkflow)
            {
                disposableWorkflow.Dispose();
            }

            foreach (var d in disposables) d.Dispose();
        }
    }

    private async IAsyncEnumerable<AgentRunResponseUpdate> ExecuteWorkflowAsync(
        Workflow workflow,
        string input,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using StreamingRun run = await InProcessExecution.StreamAsync(workflow!, input, cancellationToken: cancellationToken);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken))
        {
            switch (evt)
            {
                case BaristaOrderSplitted _:
                    yield return new AgentRunResponseUpdate
                    {
                        AgentId = Id,
                        AuthorName = "bot",
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent("Barista order sent.")],
                        ResponseId = Guid.NewGuid().ToString("N"),
                        MessageId = Guid.NewGuid().ToString("N")
                    };
                    break;
                case KitchenOrderSplitted _:
                    yield return new AgentRunResponseUpdate
                    {
                        AgentId = Id,
                        AuthorName = "bot",
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent("Kitchen order sent.")],
                        ResponseId = Guid.NewGuid().ToString("N"),
                        MessageId = Guid.NewGuid().ToString("N")
                    };
                    break;
                case WorkflowOutputEvent outputEvent:
                    yield return new AgentRunResponseUpdate
                    {
                        AgentId = Id,
                        AuthorName = "bot",
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(outputEvent.Data?.ToString() ?? "No data")],
                        ResponseId = Guid.NewGuid().ToString("N"),
                        MessageId = Guid.NewGuid().ToString("N")
                    };
                    break;
            }
        }
    }

    private static async Task<(McpClient?, AIAgent?, AIAgent?)> InitializeClientsAsync(
        IHttpClientFactory httpClientFactory,
        ITokenAcquisition? tokenAcquisition,
        CancellationToken cancellationToken)
    {
        // Delegate to extension methods - they handle both auth and non-auth scenarios
        var mcpClient = await AgenticExtentions.GetMcpClientAsync(httpClientFactory, tokenAcquisition, cancellationToken);
        var (a2aBaristaAIAgent, a2aKitchenAIAgent) = await AgenticExtentions.ResolveA2AClientsAsync(httpClientFactory, tokenAcquisition, cancellationToken);
        return (mcpClient, a2aBaristaAIAgent, a2aKitchenAIAgent);
    }
}

internal sealed class CustomAgentThread : InMemoryAgentThread
{
    internal CustomAgentThread() { }

    internal CustomAgentThread(JsonElement serializedThreadState, JsonSerializerOptions? jsonSerializerOptions = null)
        : base(serializedThreadState, jsonSerializerOptions) { }
}
