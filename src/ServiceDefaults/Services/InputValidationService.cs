using A2A;

namespace ServiceDefaults.Services;

/// <summary>
/// Service for validating input messages and data.
/// Implements security best practice of input validation to prevent injection attacks.
/// Reference: .NET Security Best Practices - https://docs.microsoft.com/en-us/dotnet/standard/security/
/// </summary>
public interface IInputValidationService
{
    /// <summary>
    /// Validates an agent task for required content
    /// </summary>
    /// <param name="task">The task to validate</param>
    /// <returns>Validation result</returns>
    TaskValidationResult ValidateTask(AgentTask task);

    /// <summary>
    /// Sanitizes text input to prevent injection attacks
    /// </summary>
    /// <param name="input">The input text to sanitize</param>
    /// <returns>Sanitized text</returns>
    string SanitizeTextInput(string input);
}

/// <summary>
/// Task validation result
/// </summary>
public record TaskValidationResult(bool IsValid, string? ErrorMessage, string? TextContent);

/// <summary>
/// Implementation of input validation service
/// </summary>
public class InputValidationService : IInputValidationService
{
    private const int MaxTextLength = 10000; // Prevent large inputs
    private static readonly string[] ForbiddenPatterns = 
    {
        "<script", "</script>", "javascript:", "vbscript:", "onclick=", "onerror=",
        "eval(", "expression(", "url(", "@import"
    };

    public TaskValidationResult ValidateTask(AgentTask task)
    {
        // Check if task has history
        var lastMessage = task.History?.LastOrDefault();
        if (lastMessage?.Parts == null)
        {
            return new TaskValidationResult(false, AgentConstants.ErrorMessages.NoMessageContent, null);
        }

        // Extract text content
        var messageText = lastMessage.Parts.OfType<TextPart>().FirstOrDefault()?.Text;
        if (string.IsNullOrEmpty(messageText))
        {
            return new TaskValidationResult(false, AgentConstants.ErrorMessages.NoTextContent, null);
        }

        // Validate text length
        if (messageText.Length > MaxTextLength)
        {
            return new TaskValidationResult(false, $"Message text exceeds maximum length of {MaxTextLength} characters", null);
        }

        // Sanitize the text
        var sanitizedText = SanitizeTextInput(messageText);

        return new TaskValidationResult(true, null, sanitizedText);
    }

    public string SanitizeTextInput(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sanitized = input;

        // Remove potentially dangerous patterns (case-insensitive)
        foreach (var pattern in ForbiddenPatterns)
        {
            sanitized = sanitized.Replace(pattern, "", StringComparison.OrdinalIgnoreCase);
        }

        // Remove control characters except for newlines, tabs, and carriage returns
        sanitized = new string(sanitized.Where(c => 
            char.IsControl(c) ? c == '\n' || c == '\r' || c == '\t' : true).ToArray());

        // Trim excessive whitespace
        sanitized = sanitized.Trim();

        return sanitized;
    }
}