using A2A;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceDefaults.Agents;
using ServiceDefaults;
using System.Diagnostics;

namespace ServiceDefaults.Tests.Agents;

/// <summary>
/// Unit tests for BaseAgent to verify the Template Method pattern and common functionality
/// </summary>
public class BaseAgentTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<ITaskManager> _mockTaskManager;
    private readonly TestAgent _agent;

    public BaseAgentTests()
    {
        _mockLogger = new Mock<ILogger>();
        _mockTaskManager = new Mock<ITaskManager>();
        _agent = new TestAgent(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TestAgent(null));
    }

    [Fact]
    public void Attach_SetsTaskManagerAndCallbacks()
    {
        // Act
        _agent.Attach(_mockTaskManager.Object);

        // Assert
        Assert.NotNull(_agent.TaskManager);
        _mockTaskManager.VerifySet(x => x.OnTaskCreated = It.IsAny<Func<AgentTask, CancellationToken, Task>>(), Times.Once);
        _mockTaskManager.VerifySet(x => x.OnTaskUpdated = It.IsAny<Func<AgentTask, CancellationToken, Task>>(), Times.Once);
        _mockTaskManager.VerifySet(x => x.OnAgentCardQuery = It.IsAny<Func<string, CancellationToken, Task<AgentCard>>>(), Times.Once);
    }

    [Fact]
    public async Task ProcessTaskAsync_WithoutTaskManager_ThrowsInvalidOperationException()
    {
        // Arrange
        var task = new AgentTask { Id = "test-task" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _agent.ProcessTaskAsync(task, CancellationToken.None));
        
        Assert.Equal(AgentConstants.ErrorMessages.TaskManagerNotAttached, exception.Message);
    }

    [Fact]
    public async Task ProcessTaskAsync_WithCancelledToken_LogsWarningAndReturns()
    {
        // Arrange
        var task = new AgentTask { Id = "test-task" };
        var cancellationToken = new CancellationToken(true);
        _agent.Attach(_mockTaskManager.Object);

        // Act
        await _agent.ProcessTaskAsync(task, cancellationToken);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("cancelled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessTaskAsync_WithValidTask_CallsProcessTaskCoreAsync()
    {
        // Arrange
        var task = new AgentTask { Id = "test-task" };
        _agent.Attach(_mockTaskManager.Object);

        // Act
        await _agent.ProcessTaskAsync(task, CancellationToken.None);

        // Assert
        Assert.True(_agent.ProcessTaskCoreAsyncCalled);
        Assert.Equal(task, _agent.LastProcessedTask);
    }

    [Fact]
    public async Task ProcessTaskAsync_WithException_CallsHandleTaskErrorAsync()
    {
        // Arrange
        var task = new AgentTask { Id = "test-task" };
        var testException = new InvalidOperationException("Test exception");
        _agent.Attach(_mockTaskManager.Object);
        _agent.ExceptionToThrow = testException;

        _mockTaskManager
            .Setup(x => x.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<TaskState>(), It.IsAny<Message>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _agent.ProcessTaskAsync(task, CancellationToken.None);

        // Assert
        _mockTaskManager.Verify(
            x => x.UpdateStatusAsync(
                task.Id,
                TaskState.Failed,
                It.IsAny<Message>(),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleTaskErrorAsync_UpdatesTaskStatus()
    {
        // Arrange
        var task = new AgentTask { Id = "test-task" };
        var exception = new Exception("Test exception");
        _agent.Attach(_mockTaskManager.Object);

        _mockTaskManager
            .Setup(x => x.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<TaskState>(), It.IsAny<Message>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _agent.CallHandleTaskErrorAsync(task, exception, CancellationToken.None);

        // Assert
        _mockTaskManager.Verify(
            x => x.UpdateStatusAsync(
                task.Id,
                TaskState.Failed,
                It.Is<Message>(m => m.Parts.Any(p => p is TextPart)),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void GetSanitizedErrorMessage_ReturnsGenericMessage()
    {
        // Arrange
        var exception = new Exception("Sensitive internal error message");

        // Act
        var result = _agent.CallGetSanitizedErrorMessage(exception);

        // Assert
        Assert.Equal("An error occurred while processing the request. Please try again later.", result);
    }

    [Fact]
    public void GetDefaultCapabilities_ReturnsExpectedValues()
    {
        // Act
        var capabilities = _agent.CallGetDefaultCapabilities();

        // Assert
        Assert.True(capabilities.Streaming);
        Assert.False(capabilities.PushNotifications);
    }

    [Fact]
    public void Dispose_DisposesActivitySource()
    {
        // Arrange
        var disposed = false;
        _agent.OnDispose = () => disposed = true;

        // Act
        _agent.Dispose();

        // Assert
        Assert.True(disposed);
    }

    /// <summary>
    /// Test implementation of BaseAgent for testing purposes
    /// </summary>
    private class TestAgent : BaseAgent
    {
        public bool ProcessTaskCoreAsyncCalled { get; private set; }
        public AgentTask? LastProcessedTask { get; private set; }
        public Exception? ExceptionToThrow { get; set; }
        public Action? OnDispose { get; set; }

        public ITaskManager? TaskManager => _taskManager;

        public TestAgent(ILogger logger) : base(logger, "test-activity-source") { }

        protected override Task ProcessTaskCoreAsync(AgentTask task, CancellationToken cancellationToken)
        {
            ProcessTaskCoreAsyncCalled = true;
            LastProcessedTask = task;

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            return Task.CompletedTask;
        }

        public override Task<AgentCard> GetAgentCardAsync(string agentUrl, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentCard
            {
                Name = "Test Agent",
                Description = "Test agent for unit testing",
                Url = agentUrl,
                Version = "1.0.0",
                DefaultInputModes = ["text"],
                DefaultOutputModes = ["text"],
                Capabilities = GetDefaultCapabilities()
            });
        }

        // Expose protected methods for testing
        public Task CallHandleTaskErrorAsync(AgentTask task, Exception ex, CancellationToken cancellationToken)
            => HandleTaskErrorAsync(task, ex, cancellationToken);

        public string CallGetSanitizedErrorMessage(Exception ex)
            => GetSanitizedErrorMessage(ex);

        public AgentCapabilities CallGetDefaultCapabilities()
            => GetDefaultCapabilities();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                OnDispose?.Invoke();
            }
            base.Dispose(disposing);
        }
    }
}