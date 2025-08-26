using A2A;
using ServiceDefaults.Services;
using ServiceDefaults;

namespace ServiceDefaults.Tests.Services;

/// <summary>
/// Unit tests for InputValidationService to verify security validation logic
/// </summary>
public class InputValidationServiceTests
{
    private readonly InputValidationService _service = new();

    [Fact]
    public void ValidateTask_WithNullTask_ReturnsInvalid()
    {
        // Arrange
        var task = new AgentTask { History = null };

        // Act
        var result = _service.ValidateTask(task);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(AgentConstants.ErrorMessages.NoMessageContent, result.ErrorMessage);
        Assert.Null(result.TextContent);
    }

    [Fact]
    public void ValidateTask_WithEmptyHistory_ReturnsInvalid()
    {
        // Arrange
        var task = new AgentTask { History = new List<Message>() };

        // Act
        var result = _service.ValidateTask(task);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(AgentConstants.ErrorMessages.NoMessageContent, result.ErrorMessage);
        Assert.Null(result.TextContent);
    }

    [Fact]
    public void ValidateTask_WithNoTextParts_ReturnsInvalid()
    {
        // Arrange
        var task = new AgentTask
        {
            History = new List<Message>
            {
                new Message
                {
                    Parts = new List<IPart>()
                }
            }
        };

        // Act
        var result = _service.ValidateTask(task);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(AgentConstants.ErrorMessages.NoTextContent, result.ErrorMessage);
        Assert.Null(result.TextContent);
    }

    [Fact]
    public void ValidateTask_WithEmptyTextPart_ReturnsInvalid()
    {
        // Arrange
        var task = new AgentTask
        {
            History = new List<Message>
            {
                new Message
                {
                    Parts = new List<IPart>
                    {
                        new TextPart { Text = "" }
                    }
                }
            }
        };

        // Act
        var result = _service.ValidateTask(task);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(AgentConstants.ErrorMessages.NoTextContent, result.ErrorMessage);
        Assert.Null(result.TextContent);
    }

    [Fact]
    public void ValidateTask_WithValidText_ReturnsValid()
    {
        // Arrange
        var messageText = "I would like to order a coffee";
        var task = new AgentTask
        {
            History = new List<Message>
            {
                new Message
                {
                    Parts = new List<IPart>
                    {
                        new TextPart { Text = messageText }
                    }
                }
            }
        };

        // Act
        var result = _service.ValidateTask(task);

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(messageText, result.TextContent);
    }

    [Fact]
    public void ValidateTask_WithTooLongText_ReturnsInvalid()
    {
        // Arrange
        var longText = new string('x', 10001); // Exceeds MaxTextLength of 10000
        var task = new AgentTask
        {
            History = new List<Message>
            {
                new Message
                {
                    Parts = new List<IPart>
                    {
                        new TextPart { Text = longText }
                    }
                }
            }
        };

        // Act
        var result = _service.ValidateTask(task);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("exceeds maximum length", result.ErrorMessage);
        Assert.Null(result.TextContent);
    }

    [Theory]
    [InlineData("<script>alert('xss')</script>", "alert('xss')")]
    [InlineData("Click javascript:void(0)", "Click void(0)")]
    [InlineData("onclick=\"alert('test')\"", "")]
    [InlineData("eval(document.cookie)", "document.cookie)")]
    [InlineData("vbscript:msgbox", "msgbox")]
    public void SanitizeTextInput_RemovesDangerousPatterns(string input, string expected)
    {
        // Act
        var result = _service.SanitizeTextInput(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeTextInput_RemovesControlCharacters()
    {
        // Arrange
        var input = "Normal text\x00\x01\x02with\x03control\x04chars\ttab\nline\rreturn";
        var expected = "Normal textwithcontrolcharstabline\rreturn";

        // Act
        var result = _service.SanitizeTextInput(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeTextInput_TrimsWhitespace()
    {
        // Arrange
        var input = "   Text with spaces   ";
        var expected = "Text with spaces";

        // Act
        var result = _service.SanitizeTextInput(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeTextInput_WithNullInput_ReturnsNull()
    {
        // Act
        var result = _service.SanitizeTextInput(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SanitizeTextInput_WithEmptyInput_ReturnsEmpty()
    {
        // Act
        var result = _service.SanitizeTextInput("");

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void ValidateTask_SanitizesTextContent()
    {
        // Arrange
        var maliciousText = "<script>alert('xss')</script>Order coffee";
        var expectedSanitized = "alert('xss')Order coffee";
        var task = new AgentTask
        {
            History = new List<Message>
            {
                new Message
                {
                    Parts = new List<IPart>
                    {
                        new TextPart { Text = maliciousText }
                    }
                }
            }
        };

        // Act
        var result = _service.ValidateTask(task);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(expectedSanitized, result.TextContent);
    }
}