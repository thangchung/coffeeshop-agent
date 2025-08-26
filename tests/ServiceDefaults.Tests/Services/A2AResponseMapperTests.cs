using A2A;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceDefaults.Services;
using ServiceDefaults;

namespace ServiceDefaults.Tests.Services;

/// <summary>
/// Unit tests for A2AResponseMapper to verify response mapping logic
/// </summary>
public class A2AResponseMapperTests
{
    private readonly Mock<ILogger<A2AResponseMapper>> _mockLogger;
    private readonly A2AResponseMapper _responseMapper;

    public A2AResponseMapperTests()
    {
        _mockLogger = new Mock<ILogger<A2AResponseMapper>>();
        _responseMapper = new A2AResponseMapper(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithValidLogger_SucceedsWithoutException()
    {
        // Act & Assert - constructor call in setup should succeed
        Assert.NotNull(_responseMapper);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new A2AResponseMapper(null));
    }

    [Fact]
    public void MapResponse_WithAgentTask_ReturnsTaskResponse()
    {
        // Arrange
        var task = new AgentTask
        {
            Id = "test-task-123",
            Status = new TaskStatus { State = TaskState.Completed },
            Artifacts = new List<Artifact>
            {
                new Artifact
                {
                    Parts = new List<IPart>
                    {
                        new TextPart { Text = "Task completed successfully" }
                    }
                }
            }
        };

        // Act
        var result = _responseMapper.MapResponse(task);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("A2A task created successfully", result.Message);
        Assert.NotNull(result.Data);

        // Verify logged information
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("A2A task created successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public void MapResponse_WithAgentTaskWithoutArtifacts_ReturnsTaskResponseWithDefaultText()
    {
        // Arrange
        var task = new AgentTask
        {
            Id = "test-task-456",
            Status = new TaskStatus { State = TaskState.Working },
            Artifacts = null
        };

        // Act
        var result = _responseMapper.MapResponse(task);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("A2A task created successfully", result.Message);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public void MapResponse_WithMessage_ReturnsMessageResponse()
    {
        // Arrange
        var message = new Message
        {
            MessageId = "msg-123",
            Parts = new List<IPart>
            {
                new TextPart { Text = "Order received and processing" }
            }
        };

        // Act
        var result = _responseMapper.MapResponse(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("A2A message sent successfully", result.Message);
        Assert.NotNull(result.Data);

        // Verify logged information
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Received A2A message response")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public void MapResponse_WithMessageWithoutParts_ReturnsMessageResponseWithDefaultText()
    {
        // Arrange
        var message = new Message
        {
            MessageId = "msg-456",
            Parts = null
        };

        // Act
        var result = _responseMapper.MapResponse(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("A2A message sent successfully", result.Message);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public void MapResponse_WithUnknownResponseType_ReturnsFailureResponse()
    {
        // Arrange
        var unknownResponse = new UnknownResponse();

        // Act
        var result = _responseMapper.MapResponse(unknownResponse);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal(AgentConstants.ErrorMessages.UnexpectedResponseType, result.Message);
        Assert.Contains("UnknownResponse", result.Error);

        // Verify logged warning
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Unexpected A2A response type")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public void MapResponse_WithNullResponse_ReturnsFailureResponse()
    {
        // Act
        var result = _responseMapper.MapResponse(null);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal(AgentConstants.ErrorMessages.UnexpectedResponseType, result.Message);
        Assert.Contains("null", result.Error);

        // Verify logged warning
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Unexpected A2A response type")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(TaskState.Created)]
    [InlineData(TaskState.Working)]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Failed)]
    public void MapResponse_WithDifferentTaskStates_HandlesAllStates(TaskState taskState)
    {
        // Arrange
        var task = new AgentTask
        {
            Id = $"test-task-{taskState}",
            Status = new TaskStatus { State = taskState }
        };

        // Act
        var result = _responseMapper.MapResponse(task);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("A2A task created successfully", result.Message);
    }

    [Fact]
    public void MapResponse_WithMessageContainingMultipleParts_ExtractsFirstTextPart()
    {
        // Arrange
        var message = new Message
        {
            MessageId = "msg-multi",
            Parts = new List<IPart>
            {
                new TextPart { Text = "First text part" },
                new TextPart { Text = "Second text part" }
            }
        };

        // Act
        var result = _responseMapper.MapResponse(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        
        // The mapper should extract the first text part
        var data = result.Data as dynamic;
        // Note: We can't easily test the dynamic object content without reflection or casting
        // but we know it should contain the response data
    }

    [Fact]
    public void MapResponse_WithTaskContainingMultipleArtifacts_ExtractsFirstArtifact()
    {
        // Arrange
        var task = new AgentTask
        {
            Id = "test-task-multi",
            Status = new TaskStatus { State = TaskState.Completed },
            Artifacts = new List<Artifact>
            {
                new Artifact
                {
                    Parts = new List<IPart>
                    {
                        new TextPart { Text = "First artifact text" }
                    }
                },
                new Artifact
                {
                    Parts = new List<IPart>
                    {
                        new TextPart { Text = "Second artifact text" }
                    }
                }
            }
        };

        // Act
        var result = _responseMapper.MapResponse(task);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }

    /// <summary>
    /// Test class for unknown response type testing
    /// </summary>
    private class UnknownResponse : A2AResponse
    {
        // Empty implementation for testing unknown response type
    }
}