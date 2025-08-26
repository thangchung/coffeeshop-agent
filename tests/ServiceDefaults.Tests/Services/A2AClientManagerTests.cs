using A2A;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceDefaults.Configuration;
using ServiceDefaults.Services;

namespace ServiceDefaults.Tests.Services;

/// <summary>
/// Unit tests for A2AClientManager to verify client management functionality
/// </summary>
public class A2AClientManagerTests
{
    private readonly Mock<IAgentConfigurationService> _mockConfigService;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<ILogger<A2AClientManager>> _mockLogger;
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly A2AClientManager _clientManager;

    public A2AClientManagerTests()
    {
        _mockConfigService = new Mock<IAgentConfigurationService>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<A2AClientManager>>();
        _mockHttpClient = new Mock<HttpClient>();
        
        _mockHttpClientFactory
            .Setup(x => x.CreateClient())
            .Returns(_mockHttpClient.Object);

        _clientManager = new A2AClientManager(
            _mockConfigService.Object,
            _mockHttpClientFactory.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithNullConfigurationService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new A2AClientManager(
            null,
            _mockHttpClientFactory.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullHttpClientFactory_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new A2AClientManager(
            _mockConfigService.Object,
            null,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new A2AClientManager(
            _mockConfigService.Object,
            _mockHttpClientFactory.Object,
            null));
    }

    [Fact]
    public void GetClients_InitiallyEmpty_ReturnsEmptyDictionary()
    {
        // Act
        var clients = _clientManager.GetClients();

        // Assert
        Assert.Empty(clients);
    }

    [Fact]
    public void GetClient_WithNonExistentKey_ReturnsNull()
    {
        // Act
        var client = _clientManager.GetClient("non-existent");

        // Assert
        Assert.Null(client);
    }

    [Fact]
    public async Task InitializeClientsAsync_WithEmptyEndpoints_CompletesSuccessfully()
    {
        // Arrange
        _mockConfigService
            .Setup(x => x.GetDownstreamAgentEndpoints())
            .Returns(new Dictionary<string, string>());

        // Act
        await _clientManager.InitializeClientsAsync();

        // Assert
        var clients = _clientManager.GetClients();
        Assert.Empty(clients);
    }

    [Fact]
    public async Task InitializeClientsAsync_WithValidEndpoints_LogsInformation()
    {
        // Arrange
        var endpoints = new Dictionary<string, string>
        {
            { "barista", "https://localhost:5001/agent" },
            { "kitchen", "https://localhost:5002/agent" }
        };

        _mockConfigService
            .Setup(x => x.GetDownstreamAgentEndpoints())
            .Returns(endpoints);

        // Act
        await _clientManager.InitializeClientsAsync();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("initialization completed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InitializeClientsAsync_WithException_LogsErrorAndContinues()
    {
        // Arrange
        var endpoints = new Dictionary<string, string>
        {
            { "barista", "invalid-url" },
            { "kitchen", "https://localhost:5002/agent" }
        };

        _mockConfigService
            .Setup(x => x.GetDownstreamAgentEndpoints())
            .Returns(endpoints);

        // Act
        await _clientManager.InitializeClientsAsync();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Failed to initialize")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task InitializeClientsAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var endpoints = new Dictionary<string, string>
        {
            { "barista", "https://localhost:5001/agent" }
        };

        _mockConfigService
            .Setup(x => x.GetDownstreamAgentEndpoints())
            .Returns(endpoints);

        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _clientManager.InitializeClientsAsync(cancellationToken));
    }

    [Fact]
    public void Dispose_DisposesActivitySource()
    {
        // Act
        _clientManager.Dispose();

        // Assert - No exception should be thrown
        Assert.True(true);
    }
}