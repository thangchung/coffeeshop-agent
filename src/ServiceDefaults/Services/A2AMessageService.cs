using System.Text.Json;
using A2A;
using ServiceDefaults.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ServiceDefaults.Services;

/// <summary>
/// Service for handling A2A message operations.
/// Follows Single Responsibility Principle (SRP) by focusing only on A2A messaging.
/// </summary>
public interface IA2AMessageService
{
    /// <summary>
    /// Sends A2A messages to appropriate downstream services based on order items
    /// </summary>
    /// <param name="messageText">The original message text</param>
    /// <param name="order">The parsed order</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of A2A service responses</returns>
    Task<List<A2AServiceResponse>> SendOrderMessagesAsync(string messageText, OrderDto order, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of A2A message service
/// </summary>
public class A2AMessageService : IA2AMessageService
{
    private readonly IA2AClientManager _clientManager;
    private readonly IA2AResponseMapper _responseMapper;
    private readonly ILogger<A2AMessageService> _logger;
    private readonly ActivitySource _activitySource = new("A2A.MessageService", "1.0.0");

    public A2AMessageService(
        IA2AClientManager clientManager,
        IA2AResponseMapper responseMapper,
        ILogger<A2AMessageService> logger)
    {
        _clientManager = clientManager ?? throw new ArgumentNullException(nameof(clientManager));
        _responseMapper = responseMapper ?? throw new ArgumentNullException(nameof(responseMapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<A2AServiceResponse>> SendOrderMessagesAsync(string messageText, OrderDto order, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("SendOrderMessages");
        var responses = new List<A2AServiceResponse>();

        var clientOrders = GetClientOrderMapping(order);
        activity?.SetTag("clients.count", clientOrders.Count);

        _logger.LogInformation("Sending A2A messages to {ClientCount} downstream services", clientOrders.Count);

        foreach (var (client, items) in clientOrders)
        {
            try
            {
                var response = await SendMessageToClient(client, messageText, items, cancellationToken);
                responses.Add(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send A2A message to client");
                responses.Add(new A2AServiceResponse
                {
                    Success = false,
                    Message = "Failed to send A2A message",
                    Error = "Communication error with downstream service"
                });
            }
        }

        return responses;
    }

    private Dictionary<A2AClient, List<ItemTypeDto>> GetClientOrderMapping(OrderDto order)
    {
        var clientOrders = new Dictionary<A2AClient, List<ItemTypeDto>>();
        var clients = _clientManager.GetClients();

        // Map barista items
        if (order.BaristaItems.Count > 0)
        {
            var baristaClient = _clientManager.GetClient(AgentConstants.AgentTypes.Barista);
            if (baristaClient != null)
            {
                clientOrders.Add(baristaClient, order.BaristaItems);
            }
            else
            {
                _logger.LogWarning("Barista client not available for {ItemCount} items", order.BaristaItems.Count);
            }
        }

        // Map kitchen items
        if (order.KitchenItems.Count > 0)
        {
            var kitchenClient = _clientManager.GetClient(AgentConstants.AgentTypes.Kitchen);
            if (kitchenClient != null)
            {
                clientOrders.Add(kitchenClient, order.KitchenItems);
            }
            else
            {
                _logger.LogWarning("Kitchen client not available for {ItemCount} items", order.KitchenItems.Count);
            }
        }

        return clientOrders;
    }

    private async Task<A2AServiceResponse> SendMessageToClient(A2AClient client, string messageText, List<ItemTypeDto> items, CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("SendMessageToClient");
        activity?.SetTag("items.count", items.Count);

        // Create A2A message with minimal metadata (authentication is in HTTP headers)
        var a2aMessage = new Message
        {
            Role = MessageRole.User,
            MessageId = Guid.NewGuid().ToString(),
            ContextId = Guid.NewGuid().ToString(),
            Parts = [new TextPart { Text = messageText }],
            Metadata = new Dictionary<string, JsonElement>
            {
                ["items"] = JsonSerializer.SerializeToElement(items),
                ["timestamp"] = JsonSerializer.SerializeToElement(DateTime.UtcNow.ToString("O"))
            }
        };

        // Create MessageSendParams for A2A protocol
        var messageSendParams = new MessageSendParams
        {
            Message = a2aMessage,
            Configuration = new MessageSendConfiguration
            {
                AcceptedOutputModes = ["text"],
                Blocking = true
            }
        };

        _logger.LogDebug("Sending A2A message with {ItemCount} items", items.Count);

        // Send message via A2A protocol with authenticated HTTP client
        var a2aResponse = await client.SendMessageAsync(messageSendParams);
        activity?.SetTag("response.type", a2aResponse?.GetType().Name ?? "null");

        return _responseMapper.MapResponse(a2aResponse);
    }

    public void Dispose()
    {
        _activitySource?.Dispose();
    }
}