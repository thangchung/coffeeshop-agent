using A2A;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceDefaults.Models;
using ServiceDefaults.Services;

namespace ServiceDefaults.Tests.Services;

/// <summary>
/// Unit tests for A2AMessageService to verify message routing and response handling
/// </summary>
public class A2AMessageServiceTests
{
    private readonly Mock<IA2AClientManager> _mockClientManager;
    private readonly Mock<IA2AResponseMapper> _mockResponseMapper;
    private readonly Mock<ILogger<A2AMessageService>> _mockLogger;
    private readonly Mock<A2AClient> _mockBaristaClient;
    private readonly Mock<A2AClient> _mockKitchenClient;
    private readonly A2AMessageService _messageService;

    public A2AMessageServiceTests()
    {
        _mockClientManager = new Mock<IA2AClientManager>();
        _mockResponseMapper = new Mock<IA2AResponseMapper>();
        _mockLogger = new Mock<ILogger<A2AMessageService>>();
        _mockBaristaClient = new Mock<A2AClient>();
        _mockKitchenClient = new Mock<A2AClient>();

        _messageService = new A2AMessageService(
            _mockClientManager.Object,
            _mockResponseMapper.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithAllValidDependencies_SucceedsWithoutException()
    {
        // Act & Assert - constructor call in setup should succeed
        Assert.NotNull(_messageService);
    }

    [Fact]
    public void Constructor_WithNullClientManager_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new A2AMessageService(
            null,
            _mockResponseMapper.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullResponseMapper_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new A2AMessageService(
            _mockClientManager.Object,
            null,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new A2AMessageService(
            _mockClientManager.Object,
            _mockResponseMapper.Object,
            null));
    }

    [Fact]
    public async Task SendOrderMessagesAsync_WithEmptyOrder_ReturnsEmptyList()
    {
        // Arrange
        var messageText = "Empty order";
        var order = new OrderDto(new List<ItemTypeDto>(), new List<ItemTypeDto>());

        _mockClientManager
            .Setup(x => x.GetClients())
            .Returns(new Dictionary<string, A2AClient>());

        // Act
        var result = await _messageService.SendOrderMessagesAsync(messageText, order);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SendOrderMessagesAsync_WithBaristaItemsOnly_SendsToBarista()
    {
        // Arrange
        var messageText = "I want a coffee";
        var baristaItems = new List<ItemTypeDto>
        {
            new() { Name = "Coffee", ItemType = ItemType.COFFEE_BLACK, Price = 3.0f }
        };
        var order = new OrderDto(baristaItems, new List<ItemTypeDto>());

        var expectedResponse = new A2AServiceResponse { Success = true, Message = "Success" };
        var taskResponse = new AgentTask { Id = "task-123" };

        _mockClientManager
            .Setup(x => x.GetClient(AgentConstants.AgentTypes.Barista))
            .Returns(_mockBaristaClient.Object);

        _mockBaristaClient
            .Setup(x => x.SendMessageAsync(It.IsAny<MessageSendParams>()))
            .ReturnsAsync(taskResponse);

        _mockResponseMapper
            .Setup(x => x.MapResponse(taskResponse))
            .Returns(expectedResponse);

        // Act
        var result = await _messageService.SendOrderMessagesAsync(messageText, order);

        // Assert
        Assert.Single(result);
        Assert.True(result[0].Success);
        
        _mockBaristaClient.Verify(
            x => x.SendMessageAsync(It.Is<MessageSendParams>(p => 
                p.Message.Parts.Any(part => part is TextPart tp && tp.Text == messageText))),
            Times.Once);
    }

    [Fact]
    public async Task SendOrderMessagesAsync_WithKitchenItemsOnly_SendsToKitchen()
    {
        // Arrange
        var messageText = "I want food";
        var kitchenItems = new List<ItemTypeDto>
        {
            new() { Name = "Sandwich", ItemType = ItemType.CAKEPOP, Price = 5.0f }
        };
        var order = new OrderDto(new List<ItemTypeDto>(), kitchenItems);

        var expectedResponse = new A2AServiceResponse { Success = true, Message = "Success" };
        var taskResponse = new AgentTask { Id = "task-456" };

        _mockClientManager
            .Setup(x => x.GetClient(AgentConstants.AgentTypes.Kitchen))
            .Returns(_mockKitchenClient.Object);

        _mockKitchenClient
            .Setup(x => x.SendMessageAsync(It.IsAny<MessageSendParams>()))
            .ReturnsAsync(taskResponse);

        _mockResponseMapper
            .Setup(x => x.MapResponse(taskResponse))
            .Returns(expectedResponse);

        // Act
        var result = await _messageService.SendOrderMessagesAsync(messageText, order);

        // Assert
        Assert.Single(result);
        Assert.True(result[0].Success);
        
        _mockKitchenClient.Verify(
            x => x.SendMessageAsync(It.Is<MessageSendParams>(p => 
                p.Message.Parts.Any(part => part is TextPart tp && tp.Text == messageText))),
            Times.Once);
    }

    [Fact]
    public async Task SendOrderMessagesAsync_WithBothItemTypes_SendsToBothServices()
    {
        // Arrange
        var messageText = "I want coffee and food";
        var baristaItems = new List<ItemTypeDto>
        {
            new() { Name = "Coffee", ItemType = ItemType.COFFEE_BLACK, Price = 3.0f }
        };
        var kitchenItems = new List<ItemTypeDto>
        {
            new() { Name = "Sandwich", ItemType = ItemType.CAKEPOP, Price = 5.0f }
        };
        var order = new OrderDto(baristaItems, kitchenItems);

        var expectedResponse = new A2AServiceResponse { Success = true, Message = "Success" };
        var baristaTaskResponse = new AgentTask { Id = "barista-task-123" };
        var kitchenTaskResponse = new AgentTask { Id = "kitchen-task-456" };

        _mockClientManager
            .Setup(x => x.GetClient(AgentConstants.AgentTypes.Barista))
            .Returns(_mockBaristaClient.Object);

        _mockClientManager
            .Setup(x => x.GetClient(AgentConstants.AgentTypes.Kitchen))
            .Returns(_mockKitchenClient.Object);

        _mockBaristaClient
            .Setup(x => x.SendMessageAsync(It.IsAny<MessageSendParams>()))
            .ReturnsAsync(baristaTaskResponse);

        _mockKitchenClient
            .Setup(x => x.SendMessageAsync(It.IsAny<MessageSendParams>()))
            .ReturnsAsync(kitchenTaskResponse);

        _mockResponseMapper
            .Setup(x => x.MapResponse(It.IsAny<A2AResponse>()))
            .Returns(expectedResponse);

        // Act
        var result = await _messageService.SendOrderMessagesAsync(messageText, order);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(r.Success));
        
        _mockBaristaClient.Verify(
            x => x.SendMessageAsync(It.IsAny<MessageSendParams>()),
            Times.Once);
        
        _mockKitchenClient.Verify(
            x => x.SendMessageAsync(It.IsAny<MessageSendParams>()),
            Times.Once);
    }

    [Fact]
    public async Task SendOrderMessagesAsync_WithUnavailableClient_LogsWarningAndSkips()
    {
        // Arrange
        var messageText = "I want coffee";
        var baristaItems = new List<ItemTypeDto>
        {
            new() { Name = "Coffee", ItemType = ItemType.COFFEE_BLACK, Price = 3.0f }
        };
        var order = new OrderDto(baristaItems, new List<ItemTypeDto>());

        _mockClientManager
            .Setup(x => x.GetClient(AgentConstants.AgentTypes.Barista))
            .Returns((A2AClient)null);

        // Act
        var result = await _messageService.SendOrderMessagesAsync(messageText, order);

        // Assert
        Assert.Empty(result);
        
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("not available")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendOrderMessagesAsync_WithClientException_ReturnsFailureResponse()
    {
        // Arrange
        var messageText = "I want coffee";
        var baristaItems = new List<ItemTypeDto>
        {
            new() { Name = "Coffee", ItemType = ItemType.COFFEE_BLACK, Price = 3.0f }
        };
        var order = new OrderDto(baristaItems, new List<ItemTypeDto>());

        _mockClientManager
            .Setup(x => x.GetClient(AgentConstants.AgentTypes.Barista))
            .Returns(_mockBaristaClient.Object);

        _mockBaristaClient
            .Setup(x => x.SendMessageAsync(It.IsAny<MessageSendParams>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _messageService.SendOrderMessagesAsync(messageText, order);

        // Assert
        Assert.Single(result);
        Assert.False(result[0].Success);
        Assert.Equal("Failed to send A2A message", result[0].Message);
        Assert.Equal("Communication error with downstream service", result[0].Error);
        
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Failed to send A2A message")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendOrderMessagesAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var messageText = "I want coffee";
        var baristaItems = new List<ItemTypeDto>
        {
            new() { Name = "Coffee", ItemType = ItemType.COFFEE_BLACK, Price = 3.0f }
        };
        var order = new OrderDto(baristaItems, new List<ItemTypeDto>());
        var cancellationToken = new CancellationToken(true);

        _mockClientManager
            .Setup(x => x.GetClient(AgentConstants.AgentTypes.Barista))
            .Returns(_mockBaristaClient.Object);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _messageService.SendOrderMessagesAsync(messageText, order, cancellationToken));
    }

    [Fact]
    public void Dispose_DisposesActivitySource()
    {
        // Act
        _messageService.Dispose();

        // Assert - No exception should be thrown
        Assert.True(true);
    }
}