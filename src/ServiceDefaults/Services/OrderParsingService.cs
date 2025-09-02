using System.Text;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using ModelContextProtocol.Client;
using ServiceDefaults.Configuration;
using Microsoft.Extensions.Logging;

namespace ServiceDefaults.Services;

/// <summary>
/// Service for parsing customer messages into structured orders.
/// Follows Single Responsibility Principle (SRP) by focusing only on message parsing and order creation.
/// Now includes support for authenticated MCP client connections.
/// </summary>
public interface IOrderParsingService
{
    /// <summary>
    /// Parses a customer message into structured order items
    /// </summary>
    /// <param name="messageText">The customer's message</param>
    /// <param name="jwtToken">JWT token for authenticated MCP requests</param>
    /// <param name="isStub">Whether to use stub data for testing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed order with items categorized by service type</returns>
    Task<OrderDto> ParseOrderAsync(string messageText, string? jwtToken = null, bool isStub = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of order parsing service using Semantic Kernel and MCP
/// </summary>
public class OrderParsingService : IOrderParsingService
{
    private readonly Kernel _kernel;
    private readonly IAgentConfigurationService _configurationService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OrderParsingService> _logger;

    public OrderParsingService(
        Kernel kernel,
        IAgentConfigurationService configurationService,
        IHttpClientFactory httpClientFactory,
        ILogger<OrderParsingService> logger)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OrderDto> ParseOrderAsync(string messageText, string? jwtToken = null, bool isStub = false, CancellationToken cancellationToken = default)
    {
        if (isStub)
        {
            return ParseStubOrder();
        }

        try
        {
            var mcpConfig = _configurationService.GetMcpServerConfiguration();
            var orderJson = await ProcessWithMcpAsync(messageText, mcpConfig, jwtToken, cancellationToken);
            
            var order = JsonSerializer.Deserialize<OrderDto>(orderJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

            return order ?? new OrderDto([], []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing order from message: {Message}", messageText);
            return new OrderDto([], []);
        }
    }

    private OrderDto ParseStubOrder()
    {
        return new OrderDto(
            [
                new ItemTypeDto
                {
                    Name = "black coffee",
                    ItemType = ItemType.COFFEE_BLACK,
                    Quantity = 1,
                    Price = 3
                },
                new ItemTypeDto
                {
                    Name = "cappuccino",
                    ItemType = ItemType.CAPPUCCINO,
                    Quantity = 1,
                    Price = 3.5f
                }
            ],
            [
                new ItemTypeDto
                {
                    Name = "cake pop",
                    ItemType = ItemType.CAKEPOP,
                    Quantity = 2,
                    Price = 5
                }
            ]
        );
    }

    private async Task<string> ProcessWithMcpAsync(string messageText, McpServerConfiguration mcpConfig, string? jwtToken, CancellationToken cancellationToken)
    {
        // Create authenticated HTTP client for MCP communication
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Clear();
        
        if (!string.IsNullOrEmpty(jwtToken))
        {
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {jwtToken}");
            _logger.LogDebug("Added JWT authentication header for MCP client");
        }

        // Configure MCP transport with authenticated client
        httpClient.BaseAddress = new Uri(mcpConfig.Url);
        var transport = new SseClientTransport(new()
        {
            Endpoint = new Uri("http://product/mcp"), // Will be overridden by BaseAddress
            Name = mcpConfig.ClientName
        }, httpClient, ownsHttpClient: true);

        // Create MCP client using the official factory
        using var mcpClient = await McpClientFactory.CreateAsync(transport, cancellationToken: cancellationToken);
        var tools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);

        var options = JsonSerializerOptions.Default;
        var exporterOptions = new JsonSchemaExporterOptions()
        {
            TreatNullObliviousAsNonNullable = true,
        };
        var schema = options.GetJsonSchemaAsNode(typeof(OrderDto), exporterOptions);
        
        var prompt = CreateOrderParsingPrompt(schema?.ToString() ?? "");

        if (!_kernel.Plugins.Contains("Tools"))
        {
            _kernel.Plugins.AddFromFunctions("Tools", tools.Select(aiFunction => aiFunction.AsKernelFunction()));
        }

        var summaryAgent = new ChatCompletionAgent()
        {
            Name = "ClassificationAgent",
            Arguments = new KernelArguments(new PromptExecutionSettings()
            { 
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true }) 
            }),
            Instructions = prompt,
            Kernel = _kernel
        };

        var message = new ChatMessageContent(AuthorRole.User, messageText);
        var response = new StringBuilder();

        await foreach (var msg in summaryAgent.InvokeAsync(message, cancellationToken: cancellationToken))
        {
            response.Append(msg.Message?.Content);
        }

        return response.ToString();
    }

    private static string CreateOrderParsingPrompt(string schema)
    {
        return $$"""
            Parse a customer's message into a order object in valid JSON (in the camel-case format).
            Use your tool to extract the name, price, and item type of the customer's message.
            Use your tool to query and get the valid price of the item.
            The quantity of each item need keeping (if no quantity inputs from user, then auto-set to 1).
            Use the provided JSON schema for your reply (no markdown for formatting the JSON object needed):
            {{schema}}

            EXAMPLE 1:
            Customer's message: I want a black coffee and cappuccino.
            JSON Response:
            ```
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
    }
}

/// <summary>
/// Item types available in the coffee shop
/// </summary>
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

/// <summary>
/// Represents an item with its details
/// </summary>
public class ItemTypeDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemType ItemType { get; set; }

    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public float Price { get; set; }
}

/// <summary>
/// Represents a complete order with items categorized by service
/// </summary>
public record OrderDto(List<ItemTypeDto> BaristaItems, List<ItemTypeDto> KitchenItems);