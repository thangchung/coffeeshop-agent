using System.ClientModel;
using System.Diagnostics;
using System.Security.Authentication;
using System.Security.Claims;
using System.Text.Json;
using A2A;
using Azure.AI.OpenAI;
using CounterService.Workflows;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Web;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;

namespace CounterService.Agents;

public partial class CounterChatAgent(ChatClient chatClient,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ILogger<CounterChatAgent> logger) : AIAgent, IDisposable
{
    internal const string AgentName = $"A2A.{nameof(CounterChatAgent)}";
    public static readonly ActivitySource ActivitySource = new(AgentName, "1.0.0");

    public ChatClient ChatClient { get; } = chatClient;
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

    internal static string SystemInstructionPrompt => $$"""
    You are a counter/staff member in the coffee shop, and only serve customers who order food and beverages.
    If the customer asks for anything else, please politely refuse and tell them you only serve food and beverages.

    Use your tool to extract the name, price, and item type of the customer's message.
    Use your tool to query and get the valid price of the item (If you have a list of item types, then call to GetItemPrices tool at a priority.).
    The quantity of each item needs to be kept (if no quantity input from the user, then auto-set to 1).

    {{AFewShotPrompt}}
    """;

    private static string AFewShotPrompt => """
    EXAMPLE 1:
    Customer's message: I want a black coffee and cappuccino.
    JSON Response:
    {
        "baristaItems": [
            {
                "name": "black coffee",
                "itemType": "BLACK_COFFEE",
                "quantity": 1,
                "price": 3
            },
            {
                "name": "cappuccino",
                "itemType": "CAPPUCCINO",
                "quantity": 1,
                "price": 3.5
            }
        ],
        "kitchenItems": []
    }

    EXAMPLE 2:
    Customer's message: I want a black coffee, 2 cappuccino and 2 cakepops.
    JSON Response:
    {
        "baristaItems": [
            {
                "name": "black coffee",
                "itemType": "BLACK_COFFEE",
                "quantity": 1,
                "price": 3
            },
            {
                "name": "cappuccino",
                "itemType": "CAPPUCCINO",
                "quantity": 2,
                "price": 3.5
            }
        ],
        "kitchenItems": [
            {
                "name": "cakepop",
                "itemType": "CAKEPOP",
                "quantity": 2,
                "price": 5
            }
        ]
    }

    EXAMPLE 3:
    Customer's message: I want a croissant chocolate.
    JSON Response:
    {
        "baristaItems": [],
        "kitchenItems": [
            {
                "name": "croissant chocolate",
                "itemType": "CROISSANT_CHOCOLATE",
                "quantity": 1,
                "price": 5.5
            }
        ]
    }

    EXAMPLE 4:
    If you don't know how to parse the order object, respond with:
    {
        "baristaItems": [],
        "kitchenItems": []
    }
    """;

    public override AgentThread GetNewThread()
        => new CustomAgentThread();

    public override AgentThread DeserializeThread(JsonElement serializedThread, JsonSerializerOptions? jsonSerializerOptions = null)
        => new CustomAgentThread(serializedThread, jsonSerializerOptions);

    public override Task<AgentRunResponse> RunAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
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

    public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
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

        var (mcpClient, a2aBaristaAIAgent, a2aKitchenAIAgent) = await InitializeClientsAsync(HttpClientFactory, tokenAcquisition, cancellationToken);
        
        var workflow = await GetAgenticWorkflow(ChatClient, Configuration, mcpClient, a2aBaristaAIAgent, a2aKitchenAIAgent, Logger, cancellationToken);

        if (workflow == null)
        {
            throw new InvalidOperationException("Failed to create workflow");
        }

        var lastChatMsg = messages.Where(m => m.Role == ChatRole.User).LastOrDefault()!;

        await foreach (AgentRunResponseUpdate update in ExecuteWorkflowAsync(workflow, lastChatMsg.Text!, cancellationToken: cancellationToken))
        {
            yield return update;
        }
    }

    private async Task<(McpClient?, AIAgent?, AIAgent?)> InitializeClientsAsync(
        IHttpClientFactory httpClientFactory,
        ITokenAcquisition? tokenAcquisition,
        CancellationToken cancellationToken)
    {
        // Delegate to extension methods - they handle both auth and non-auth scenarios
        var mcpClient = await CounterChatAgentExtensions.GetMcpClientAsync(httpClientFactory, tokenAcquisition, cancellationToken);
        var (a2aBaristaAIAgent, a2aKitchenAIAgent) = await CounterChatAgentExtensions.ResolveA2AClientsAsync(httpClientFactory, tokenAcquisition, cancellationToken);
        return (mcpClient, a2aBaristaAIAgent, a2aKitchenAIAgent);
    }

    public async Task<Workflow?> GetAgenticWorkflow(
        ChatClient chatClient,
        IConfiguration configuration,
        McpClient? mcpClient,
        AIAgent? a2aBaristaAIAgent,
        AIAgent? a2aKitchenAIAgent,
        ILogger<CounterChatAgent> logger,
        CancellationToken cancellationToken)
    {
        if (mcpClient == null || a2aBaristaAIAgent == null || a2aKitchenAIAgent == null)
        {
            throw new ArgumentNullException("Required clients cannot be null");
        }

        // Delegate to extension method for workflow building
        return await CounterChatAgentExtensions.BuildWorkflowCoreAsync(
            chatClient,
            mcpClient,
            a2aBaristaAIAgent,
            a2aKitchenAIAgent,
            workflowName: null,
            cancellationToken);
    }

    public void Dispose()
    {
        if (McpClient != null)
#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
            GC.SuppressFinalize(McpClient);
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    }
}

internal sealed class CustomAgentThread : InMemoryAgentThread
{
    internal CustomAgentThread() { }

    internal CustomAgentThread(JsonElement serializedThreadState, JsonSerializerOptions? jsonSerializerOptions = null)
        : base(serializedThreadState, jsonSerializerOptions) { }
}
