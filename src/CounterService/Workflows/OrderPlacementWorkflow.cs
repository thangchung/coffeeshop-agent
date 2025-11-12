using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CounterService.Agents;
using CounterService.Instructions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;

namespace CounterService.Workflows;

public enum ItemType
{
    // Beverages
    CAPPUCCINO,
    COFFEE_BLACK,
    COFFEE_WITH_ROOM,
    ESPRESSO,
    ESPRESSO_DOUBLE,
    LATTE,
    // Food
    CAKEPOP,
    CROISSANT,
    MUFFIN,
    CROISSANT_CHOCOLATE,
    // Others
    CHICKEN_MEATBALLS,
}

public class ItemTypeDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemType ItemType { get; set; }

    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public float Price { get; set; }
}

public record OrderDto(List<ItemTypeDto> BaristaItems, List<ItemTypeDto> KitchenItems);

public class BaristaOrderSplitted : WorkflowEvent
{
    public List<ItemTypeDto> Items { get; set; } = [];
}

public class KitchenOrderSplitted : WorkflowEvent
{
    public List<ItemTypeDto> Items { get; set; } = [];
}

public class CustomAgentResponse : WorkflowEvent
{
    public required AgentRunResponse Response { get; set; }
}

public class ValidResponse
{
    public string Query { get; set; } = default!;
    public required bool Valid { get; set; } = false;
}

internal sealed class ValidatorExecutor(IChatClient chatClient, McpClient mcpClient) : Executor<string, ValidResponse>(nameof(ValidatorExecutor))
{
    public static readonly ActivitySource ActivitySource = new($"MAF.{nameof(ValidatorExecutor)}", "1.0.0");

    public override async ValueTask<ValidResponse> HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("HandleAsync", ActivityKind.Server);

        var instructions = InstructionExtensions.GetInstruction("ValidationAgentInstruction");

        var mcpTools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);

        var agent = chatClient
          .CreateAIAgent(instructions: instructions, tools: [.. mcpTools.Cast<AITool>()])
                .AsBuilder()
                .UseOpenTelemetry(sourceName: $"A2A.{nameof(CounterChatAgent)}", configure: (cfg) => cfg.EnableSensitiveData = true)
                .Build();

        var updates = agent.RunStreamingAsync(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, message), cancellationToken: cancellationToken);
        var agentResponse = await updates.ToAgentRunResponseAsync(cancellationToken: cancellationToken);

        ValidResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<ValidResponse>(agentResponse.Text, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            });
        }
        catch (JsonException ex)
        {
            activity?.SetTag("ValidatorExecutor.json_error", ex.Message);
            activity?.SetTag("ValidatorExecutor.response_text", agentResponse.Text);

            // Fallback: assume invalid if we can't parse the response
            response = new ValidResponse { Valid = false };
        }

        activity?.SetTag("ValidatorExecutor.validated", response?.Valid ?? false);

        response.Query = message;

        return response;
    }
}

internal sealed class SplitExecutor(AIAgent agent) : Executor<ValidResponse>(nameof(SplitExecutor))
{
    public static readonly ActivitySource ActivitySource = new($"MAF.{nameof(SplitExecutor)}", "1.0.0");
    private readonly AIAgent _agent = agent;

    public override async ValueTask HandleAsync(ValidResponse validResponse, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("HandleAsync", ActivityKind.Server);

        string messageClassified = string.Empty;
        await foreach (var msg in _agent.RunStreamingAsync(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, validResponse.Query), cancellationToken: cancellationToken))
        {
            messageClassified += msg.Text;
        }

        var orders = JsonSerializer.Deserialize<OrderDto>(messageClassified, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        activity?.SetTag("SplitBaristaOrderComplete.send", orders?.BaristaItems.Count ?? 0);
        await context.SendMessageAsync(new BaristaOrderSplitted() { Items = orders.BaristaItems }, cancellationToken: cancellationToken);
        await context.AddEventAsync(new BaristaOrderSplitted(), cancellationToken);

        activity?.SetTag("SplitKitchenOrderComplete.send", orders?.KitchenItems.Count ?? 0);
        await context.SendMessageAsync(new KitchenOrderSplitted() { Items = orders.KitchenItems }, cancellationToken: cancellationToken);
        await context.AddEventAsync(new KitchenOrderSplitted(), cancellationToken);

        // Broadcast the turn token to kick off the agents.
        await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken: cancellationToken);
    }
}

internal sealed class BaristaExecutor(AIAgent agent) : Executor<BaristaOrderSplitted>(nameof(BaristaExecutor))
{
    private readonly AIAgent _agent = agent;

    public override async ValueTask HandleAsync(BaristaOrderSplitted message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var response = await _agent.RunAsync(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, JsonSerializer.Serialize(message.Items)), cancellationToken: cancellationToken);
        await context.SendMessageAsync(new CustomAgentResponse { Response = response }, cancellationToken: cancellationToken);
    }
}

internal sealed class KitchenExecutor(AIAgent agent) : Executor<KitchenOrderSplitted>(nameof(KitchenExecutor))
{
    private readonly AIAgent _agent = agent;

    public override async ValueTask HandleAsync(KitchenOrderSplitted message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var response = await _agent.RunAsync(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, JsonSerializer.Serialize(message.Items)), cancellationToken: cancellationToken);
        await context.SendMessageAsync(new CustomAgentResponse { Response = response }, cancellationToken: cancellationToken);
    }
}

internal sealed class AggregationExecutor() : Executor<CustomAgentResponse>(nameof(AggregationExecutor))
{
    public static readonly ActivitySource ActivitySource = new($"MAF.{nameof(AggregationExecutor)}", "1.0.0");
    private readonly List<string> _messages = [];

    public override async ValueTask HandleAsync(CustomAgentResponse message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("HandleAsync", ActivityKind.Server);

        if (message.Response.RawRepresentation is A2A.AgentTask task)
        {
            _messages.Add(task.Status.Message?.Parts?.LastOrDefault()?.AsTextPart()?.Text ?? "");
        }

        if (_messages.Count == 2)
        {
            activity?.SetTag("YieldOutputAsync.completed", true);

            await context.YieldOutputAsync(_messages.Aggregate("", (x, y) => $"{x}, {y}"), cancellationToken);
        }
    }
}

internal sealed class HandleUncertainExecutor(McpClient mcpClient) : Executor<ValidResponse>(nameof(HandleUncertainExecutor))
{
    public static readonly ActivitySource ActivitySource = new($"MAF.{nameof(HandleUncertainExecutor)}", "1.0.0");
    public override async ValueTask HandleAsync(ValidResponse validResponse, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("HandleAsync", ActivityKind.Server);

        var productsResource = await mcpClient.ReadResourceAsync(new Uri("data://products"), cancellationToken: cancellationToken);
        if (productsResource.Contents?.FirstOrDefault() is not TextResourceContents textResourceContents)
        {
            throw new Exception("Unable to retrieve products resource.");
        }

        // Handle uncertain order here, e.g., send a message back to the user for clarification
        var clarificationMessage = $"""
            I'm sorry, but I couldn't understand your order clearly. 🤔

            Could you please clarify what you'd like to order from our menu?

            📋 Our Available Menu Items:

            {FormatProductsAsMarkdown(textResourceContents.Text)}

            **Please specify the items you'd like and their quantities.** For example:
            - "I'd like 2 cappuccinos and 1 croissant chocolate"
            - "Can I get a latte and a muffin?"

            Thank you! ☕
         """;

        activity?.SetTag("HandleUncertainExecutor.clarificationSent", true);
        await context.YieldOutputAsync(clarificationMessage, cancellationToken);
    }

    private static string FormatProductsAsMarkdown(string? productsJson)
    {
        if (string.IsNullOrEmpty(productsJson))
            return "No products available.";

        try
        {
            var products = JsonSerializer.Deserialize<List<ItemTypeDto>>(productsJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            if (products == null || products.Count == 0)
                return "No products available.";

            var beverages = products.Where(p => (int)p.ItemType <= 5).OrderBy(p => p.Name);
            var food = products.Where(p => (int)p.ItemType > 5).OrderBy(p => p.Name);

            var markdown = new StringBuilder();

            if (beverages.Any())
            {
                markdown.AppendLine("Beverages:");
                foreach (var item in beverages)
                {
                    markdown.AppendLine($"- **{item.Name}** - ${item.Price:F2}");
                }
                markdown.AppendLine();
            }

            if (food.Any())
            {
                markdown.AppendLine("Food:");
                foreach (var item in food)
                {
                    markdown.AppendLine($"- **{item.Name}** - ${item.Price:F2}");
                }
            }

            return markdown.AppendLine("\r\n\r\n\r\n").ToString().TrimEnd();
        }
        catch (JsonException)
        {
            return "Unable to parse product information.";
        }
    }
}
