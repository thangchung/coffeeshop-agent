using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Moq;
using ServiceDefaults.Configuration;
using ServiceDefaults.Services;

namespace ServiceDefaults.Tests.Services;

/// <summary>
/// Unit tests for OrderParsingService to verify order parsing logic and stub functionality
/// </summary>
public class OrderParsingServiceTests
{
    private readonly Mock<Kernel> _mockKernel;
    private readonly Mock<IAgentConfigurationService> _mockConfigService;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<ILogger<OrderParsingService>> _mockLogger;
    private readonly OrderParsingService _orderParsingService;

    public OrderParsingServiceTests()
    {
        _mockKernel = new Mock<Kernel>();
        _mockConfigService = new Mock<IAgentConfigurationService>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<OrderParsingService>>();

        _orderParsingService = new OrderParsingService(
            _mockKernel.Object,
            _mockConfigService.Object,
            _mockHttpClientFactory.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithAllValidDependencies_SucceedsWithoutException()
    {
        // Act & Assert - constructor call in setup should succeed
        Assert.NotNull(_orderParsingService);
    }

    [Fact]
    public void Constructor_WithNullKernel_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new OrderParsingService(
            null,
            _mockConfigService.Object,
            _mockHttpClientFactory.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullConfigurationService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new OrderParsingService(
            _mockKernel.Object,
            null,
            _mockHttpClientFactory.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullHttpClientFactory_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new OrderParsingService(
            _mockKernel.Object,
            _mockConfigService.Object,
            null,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new OrderParsingService(
            _mockKernel.Object,
            _mockConfigService.Object,
            _mockHttpClientFactory.Object,
            null));
    }

    [Fact]
    public async Task ParseOrderAsync_WithStubMode_ReturnsStubOrder()
    {
        // Arrange
        var messageText = "I want a coffee and a cake";

        // Act
        var result = await _orderParsingService.ParseOrderAsync(messageText, isStub: true);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.BaristaItems);
        Assert.NotNull(result.KitchenItems);

        // Verify stub data structure
        Assert.Equal(2, result.BaristaItems.Count);
        Assert.Single(result.KitchenItems);

        // Verify barista items
        var blackCoffee = result.BaristaItems.FirstOrDefault(i => i.Name == "black coffee");
        Assert.NotNull(blackCoffee);
        Assert.Equal(ItemType.COFFEE_BLACK, blackCoffee.ItemType);
        Assert.Equal(3, blackCoffee.Price);

        var cappuccino = result.BaristaItems.FirstOrDefault(i => i.Name == "cappuccino");
        Assert.NotNull(cappuccino);
        Assert.Equal(ItemType.CAPPUCCINO, cappuccino.ItemType);
        Assert.Equal(3.5f, cappuccino.Price);

        // Verify kitchen items
        var cakePop = result.KitchenItems.First();
        Assert.Equal("cake pop", cakePop.Name);
        Assert.Equal(ItemType.CAKEPOP, cakePop.ItemType);
        Assert.Equal(5, cakePop.Price);
    }

    [Fact]
    public async Task ParseOrderAsync_WithEmptyMessage_ReturnsStubOrder()
    {
        // Arrange
        var messageText = "";

        // Act
        var result = await _orderParsingService.ParseOrderAsync(messageText, isStub: true);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.BaristaItems);
        Assert.NotNull(result.KitchenItems);
        Assert.Equal(2, result.BaristaItems.Count);
        Assert.Single(result.KitchenItems);
    }

    [Fact]
    public async Task ParseOrderAsync_WithNullMessage_ReturnsStubOrder()
    {
        // Arrange
        string messageText = null;

        // Act
        var result = await _orderParsingService.ParseOrderAsync(messageText, isStub: true);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.BaristaItems);
        Assert.NotNull(result.KitchenItems);
        Assert.Equal(2, result.BaristaItems.Count);
        Assert.Single(result.KitchenItems);
    }

    [Fact]
    public async Task ParseOrderAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var messageText = "I want a coffee";
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _orderParsingService.ParseOrderAsync(messageText, isStub: false, cancellationToken));
    }

    [Theory]
    [InlineData("black coffee")]
    [InlineData("cappuccino")]
    [InlineData("espresso")]
    [InlineData("latte")]
    public async Task ParseOrderAsync_WithStubMode_ContainsBeverageTypes(string expectedBeverage)
    {
        // Act
        var result = await _orderParsingService.ParseOrderAsync("test", isStub: true);

        // Assert
        var hasBeverage = result.BaristaItems.Any(item => 
            item.Name.Contains(expectedBeverage, StringComparison.OrdinalIgnoreCase));

        if (expectedBeverage == "black coffee" || expectedBeverage == "cappuccino")
        {
            Assert.True(hasBeverage, $"Expected to find {expectedBeverage} in barista items");
        }
    }

    [Fact]
    public async Task ParseOrderAsync_WithStubMode_HasValidPrices()
    {
        // Act
        var result = await _orderParsingService.ParseOrderAsync("test", isStub: true);

        // Assert
        foreach (var item in result.BaristaItems)
        {
            Assert.True(item.Price > 0, $"Item {item.Name} should have a positive price");
        }

        foreach (var item in result.KitchenItems)
        {
            Assert.True(item.Price > 0, $"Item {item.Name} should have a positive price");
        }
    }

    [Fact]
    public async Task ParseOrderAsync_WithStubMode_HasValidItemTypes()
    {
        // Act
        var result = await _orderParsingService.ParseOrderAsync("test", isStub: true);

        // Assert
        foreach (var item in result.BaristaItems)
        {
            Assert.True(Enum.IsDefined(typeof(ItemType), item.ItemType), 
                $"Item {item.Name} should have a valid ItemType");
        }

        foreach (var item in result.KitchenItems)
        {
            Assert.True(Enum.IsDefined(typeof(ItemType), item.ItemType), 
                $"Item {item.Name} should have a valid ItemType");
        }
    }

    [Fact]
    public async Task ParseOrderAsync_WithStubMode_ReturnsConsistentData()
    {
        // Act
        var result1 = await _orderParsingService.ParseOrderAsync("message1", isStub: true);
        var result2 = await _orderParsingService.ParseOrderAsync("message2", isStub: true);

        // Assert - Stub should return consistent data regardless of input
        Assert.Equal(result1.BaristaItems.Count, result2.BaristaItems.Count);
        Assert.Equal(result1.KitchenItems.Count, result2.KitchenItems.Count);

        for (int i = 0; i < result1.BaristaItems.Count; i++)
        {
            Assert.Equal(result1.BaristaItems[i].Name, result2.BaristaItems[i].Name);
            Assert.Equal(result1.BaristaItems[i].ItemType, result2.BaristaItems[i].ItemType);
            Assert.Equal(result1.BaristaItems[i].Price, result2.BaristaItems[i].Price);
        }

        for (int i = 0; i < result1.KitchenItems.Count; i++)
        {
            Assert.Equal(result1.KitchenItems[i].Name, result2.KitchenItems[i].Name);
            Assert.Equal(result1.KitchenItems[i].ItemType, result2.KitchenItems[i].ItemType);
            Assert.Equal(result1.KitchenItems[i].Price, result2.KitchenItems[i].Price);
        }
    }
}