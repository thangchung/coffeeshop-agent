using A2A;
using CounterService.Agents;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceDefaults.Configuration;
using ServiceDefaults.Services;
using ServiceDefaults.Models;

namespace CounterService.Tests.Agents;

/// <summary>
/// Unit tests for CounterAgent to verify SOLID principles implementation and service integration
/// </summary>
public class CounterAgentTests
{
    private readonly Mock<IAgentConfigurationService> _mockConfigService;
    private readonly Mock<IA2AClientManager> _mockClientManager;
    private readonly Mock<IInputValidationService> _mockValidationService;
    private readonly Mock<IOrderParsingService> _mockOrderParsingService;
    private readonly Mock<IA2AMessageService> _mockMessageService;
    private readonly Mock<ILogger<CounterAgent>> _mockLogger;
    private readonly Mock<ITaskManager> _mockTaskManager;
    private readonly CounterAgent _counterAgent;

    public CounterAgentTests()
    {
        _mockConfigService = new Mock<IAgentConfigurationService>();
        _mockClientManager = new Mock<IA2AClientManager>();
        _mockValidationService = new Mock<IInputValidationService>();
        _mockOrderParsingService = new Mock<IOrderParsingService>();
        _mockMessageService = new Mock<IA2AMessageService>();
        _mockLogger = new Mock<ILogger<CounterAgent>>();
        _mockTaskManager = new Mock<ITaskManager>();

        _counterAgent = new CounterAgent(
            _mockConfigService.Object,
            _mockClientManager.Object,
            _mockValidationService.Object,
            _mockOrderParsingService.Object,
            _mockMessageService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithAllValidDependencies_SucceedsWithoutException()
    {
        // Act & Assert - constructor call in setup should succeed
        Assert.NotNull(_counterAgent);
    }

    [Fact]
    public void Constructor_WithNullConfigurationService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CounterAgent(
            null,
            _mockClientManager.Object,
            _mockValidationService.Object,
            _mockOrderParsingService.Object,
            _mockMessageService.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullClientManager_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CounterAgent(
            _mockConfigService.Object,
            null,
            _mockValidationService.Object,
            _mockOrderParsingService.Object,
            _mockMessageService.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullValidationService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CounterAgent(
            _mockConfigService.Object,
            _mockClientManager.Object,
            null,
            _mockOrderParsingService.Object,
            _mockMessageService.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullOrderParsingService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CounterAgent(
            _mockConfigService.Object,
            _mockClientManager.Object,
            _mockValidationService.Object,
            null,
            _mockMessageService.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullMessageService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CounterAgent(
            _mockConfigService.Object,
            _mockClientManager.Object,
            _mockValidationService.Object,
            _mockOrderParsingService.Object,
            null,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CounterAgent(
            _mockConfigService.Object,
            _mockClientManager.Object,
            _mockValidationService.Object,
            _mockOrderParsingService.Object,
            _mockMessageService.Object,
            null));
    }

    [Fact]
    public async Task ProcessTaskCoreAsync_WithInvalidInput_UpdatesTaskToFailed()
    {
        // Arrange
        var task = new AgentTask { Id = "test-task" };
        var validationResult = new TaskValidationResult(false, "Invalid input", null);

        _counterAgent.Attach(_mockTaskManager.Object);
        _mockValidationService
            .Setup(x => x.ValidateTask(task))
            .Returns(validationResult);

        _mockTaskManager
            .Setup(x => x.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<TaskState>(), It.IsAny<Message>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _counterAgent.ProcessTaskAsync(task, CancellationToken.None);

        // Assert
        _mockTaskManager.Verify(
            x => x.UpdateStatusAsync(
                task.Id,
                TaskState.Failed,
                It.Is<Message>(m => m.Parts.Any(p => p is TextPart tp && tp.Text == validationResult.ErrorMessage)),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessTaskCoreAsync_WithValidInput_FollowsCompleteWorkflow()
    {
        // Arrange
        var task = new AgentTask { Id = "test-task" };
        var messageText = "I want a coffee and a sandwich";
        var validationResult = new TaskValidationResult(true, null, messageText);
        var order = new OrderDto(
            new List<ItemTypeDto> { new() { Name = "Coffee", ItemType = ItemType.COFFEE_BLACK, Price = 3.0f } },
            new List<ItemTypeDto> { new() { Name = "Sandwich", ItemType = ItemType.CAKEPOP, Price = 5.0f } }
        );
        var responses = new List<A2AServiceResponse>
        {
            new() { Success = true, Message = "Order received", Data = "task-123" },
            new() { Success = true, Message = "Food order received", Data = "task-456" }
        };

        _counterAgent.Attach(_mockTaskManager.Object);

        _mockValidationService
            .Setup(x => x.ValidateTask(task))
            .Returns(validationResult);

        _mockOrderParsingService
            .Setup(x => x.ParseOrderAsync(messageText, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _mockMessageService
            .Setup(x => x.SendOrderMessagesAsync(messageText, order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(responses);

        _mockTaskManager
            .Setup(x => x.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<TaskState>(), It.IsAny<Message>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockTaskManager
            .Setup(x => x.ReturnArtifactAsync(It.IsAny<string>(), It.IsAny<Artifact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _counterAgent.ProcessTaskAsync(task, CancellationToken.None);

        // Assert
        // Verify Working status update
        _mockTaskManager.Verify(
            x => x.UpdateStatusAsync(
                task.Id,
                TaskState.Working,
                It.Is<Message>(m => m.Parts.Any(p => p is TextPart tp && tp.Text.Contains("Processing order"))),
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify Completed status update
        _mockTaskManager.Verify(
            x => x.UpdateStatusAsync(
                task.Id,
                TaskState.Completed,
                It.Is<Message>(m => m.Parts.Any(p => p is TextPart tp && tp.Text.Contains("Order processed successfully"))),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify artifacts returned
        _mockTaskManager.Verify(
            x => x.ReturnArtifactAsync(
                task.Id,
                It.IsAny<Artifact>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2)); // One for each response

        // Verify service calls
        _mockOrderParsingService.Verify(
            x => x.ParseOrderAsync(messageText, true, It.IsAny<CancellationToken>()),
            Times.Once);

        _mockMessageService.Verify(
            x => x.SendOrderMessagesAsync(messageText, order, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnTaskCreatedAsync_InitializesClientsAndProcessesTask()
    {
        // Arrange
        var task = new AgentTask { Id = "test-task" };
        var validationResult = new TaskValidationResult(true, null, "Test message");

        _counterAgent.Attach(_mockTaskManager.Object);

        _mockClientManager
            .Setup(x => x.InitializeClientsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockValidationService
            .Setup(x => x.ValidateTask(task))
            .Returns(validationResult);

        _mockOrderParsingService
            .Setup(x => x.ParseOrderAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderDto(new List<ItemTypeDto>(), new List<ItemTypeDto>()));

        _mockMessageService
            .Setup(x => x.SendOrderMessagesAsync(It.IsAny<string>(), It.IsAny<OrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<A2AServiceResponse>());

        _mockTaskManager
            .Setup(x => x.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<TaskState>(), It.IsAny<Message>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _counterAgent.ProcessTaskAsync(task, CancellationToken.None);

        // Assert
        _mockClientManager.Verify(
            x => x.InitializeClientsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAgentCardAsync_ReturnsValidAgentCard()
    {
        // Arrange
        var agentUrl = "https://localhost:5000/agent";

        // Act
        var agentCard = await _counterAgent.GetAgentCardAsync(agentUrl, CancellationToken.None);

        // Assert
        Assert.NotNull(agentCard);
        Assert.Equal("Counter Service Agent", agentCard.Name);
        Assert.Equal(agentUrl, agentCard.Url);
        Assert.Equal("1.0.0", agentCard.Version);
        Assert.Contains("A2A client agent", agentCard.Description);
        Assert.Contains("AUTHENTICATION REQUIRED", agentCard.Description);
        Assert.Single(agentCard.Skills);
        Assert.Equal("process_order", agentCard.Skills[0].Name);
        Assert.True(agentCard.Capabilities.Streaming);
        Assert.False(agentCard.Capabilities.PushNotifications);
    }

    [Fact]
    public async Task GetAgentCardAsync_WithCancelledToken_ReturnsCancelledTask()
    {
        // Arrange
        var agentUrl = "https://localhost:5000/agent";
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => _counterAgent.GetAgentCardAsync(agentUrl, cancellationToken));
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException), "Service configuration error")]
    [InlineData(typeof(HttpRequestException), "Unable to communicate with downstream services")]
    [InlineData(typeof(TaskCanceledException), "Request timed out")]
    [InlineData(typeof(ArgumentException), "An error occurred while processing the request")]
    public void GetSanitizedErrorMessage_ReturnsExpectedMessages(Type exceptionType, string expectedMessage)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Test message");

        // Act
        var result = _counterAgent.GetType()
            .GetMethod("GetSanitizedErrorMessage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(_counterAgent, new object[] { exception }) as string;

        // Assert
        Assert.Contains(expectedMessage, result);
    }
}