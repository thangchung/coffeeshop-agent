using CounterService.Agents;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using CounterService.Extentions;

namespace CounterService.Workflows;

public static class OrderPlacementWorkflowExtentions
{
    public static async Task<Workflow> BuildWorkflowForDevUI(
        this IServiceProvider sp,
        string workflowName,
        CancellationToken cancellationToken = default)
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

        // Initialize clients based on auth mode
        var mcpClient = await AgenticExtentions.GetMcpClientAsync(httpClientFactory, tokenAcquisition: null, cancellationToken);
        var (a2aBaristaAIAgent, a2aKitchenAIAgent) = await AgenticExtentions.ResolveA2AClientsAsync(httpClientFactory, tokenAcquisition: null, cancellationToken);

        if (mcpClient == null || a2aBaristaAIAgent == null || a2aKitchenAIAgent == null)
        {
            throw new InvalidOperationException("Failed to initialize required clients");
        }

        // Build workflow using the common core method
        return await BuildWorkflowCoreAsync(
            chatClient,
            mcpClient,
            a2aBaristaAIAgent,
            a2aKitchenAIAgent,
            workflowName,
            cancellationToken);
    }

    #region Workflow Building

    internal static async Task<Workflow> BuildWorkflowCoreAsync(
        IChatClient chatClient,
        McpClient mcpClient,
        AIAgent a2aBaristaAIAgent,
        AIAgent a2aKitchenAIAgent,
        string? workflowName = null,
        CancellationToken cancellationToken = default)
    {
        var mcpTools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);

        var schema = AIJsonUtilities.CreateJsonSchema(typeof(OrderDto));
        var chatOptions = new ChatOptions()
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
            schema: schema,
            schemaName: "OrderDto",
            schemaDescription: "Information about an order including list of items (ItemTypeDto). Each item includes ItemType, Name, Quantity, Price.")
        };

        var agent = chatClient
            .CreateAIAgent(
                instructions: CounterChatAgent.SystemInstructionPrompt,
                tools: [.. mcpTools.Cast<AITool>()])
            .AsBuilder()
            .UseOpenTelemetry(sourceName: CounterChatAgent.AgentName, configure: (cfg) => cfg.EnableSensitiveData = true)
            .Build();

        var validator = new ValidatorExecutor(chatClient, mcpClient);
        var start = new SplitExecutor(agent!);
        var baristaExecutor = new BaristaExecutor(a2aBaristaAIAgent);
        var kitchenExecutor = new KitchenExecutor(a2aKitchenAIAgent);
        var aggregation = new AggregationExecutor();
        var uncertainHandler = new HandleUncertainExecutor(mcpClient);

        var getValid = (bool valid) => (Func<object?, bool>)(detectionResult => detectionResult is ValidResponse res && res.Valid == valid);

        var workflowBuilder = new WorkflowBuilder(validator);

        if (!string.IsNullOrEmpty(workflowName))
        {
            workflowBuilder = workflowBuilder.WithName(workflowName);
        }

        var workflow = workflowBuilder
            .AddSwitch(validator, switchBuilder => switchBuilder
                .AddCase(getValid(true), start)
                .AddCase(getValid(false), uncertainHandler)
            )
            .AddFanOutEdge(start, [baristaExecutor, kitchenExecutor])
            .AddFanInEdge([baristaExecutor, kitchenExecutor], aggregation)
            .WithOutputFrom(aggregation, uncertainHandler)
            .Build();

        return workflow;
    }

    #endregion
}
