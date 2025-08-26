using A2A;
using ServiceDefaults.Models;
using Microsoft.Extensions.Logging;

namespace ServiceDefaults.Services;

/// <summary>
/// Service for mapping A2A responses to standardized response objects.
/// Follows Single Responsibility Principle (SRP) by focusing only on response mapping.
/// </summary>
public interface IA2AResponseMapper
{
    /// <summary>
    /// Maps an A2A response to a standardized service response
    /// </summary>
    /// <param name="response">The A2A response to map</param>
    /// <returns>Mapped service response</returns>
    A2AServiceResponse MapResponse(A2AResponse response);
}

/// <summary>
/// Implementation of A2A response mapper
/// </summary>
public class A2AResponseMapper : IA2AResponseMapper
{
    private readonly ILogger<A2AResponseMapper> _logger;

    public A2AResponseMapper(ILogger<A2AResponseMapper> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public A2AServiceResponse MapResponse(A2AResponse response)
    {
        return response switch
        {
            AgentTask task => MapTaskResponse(task),
            Message messageResponse => MapMessageResponse(messageResponse),
            _ => MapUnknownResponse(response)
        };
    }

    private A2AServiceResponse MapTaskResponse(AgentTask task)
    {
        _logger.LogInformation("A2A task created successfully with ID: {TaskId}, Status: {TaskStatus}",
                        task.Id, task.Status.State.ToString());

        return new A2AServiceResponse
        {
            Success = true,
            Message = "A2A task created successfully",
            Data = new
            {
                TaskId = task.Id,
                Status = task.Status.State.ToString(),
                Response = task.Artifacts?.FirstOrDefault()?.Parts?.OfType<TextPart>()?.FirstOrDefault()?.Text ?? "Task created"
            }
        };
    }

    private A2AServiceResponse MapMessageResponse(Message messageResponse)
    {
        _logger.LogInformation("Received A2A message response");

        var responseText = messageResponse.Parts?.OfType<TextPart>()?.FirstOrDefault()?.Text ?? "No response content";

        return new A2AServiceResponse
        {
            Success = true,
            Message = "A2A message sent successfully",
            Data = new
            {
                Response = responseText,
                MessageId = messageResponse.MessageId
            }
        };
    }

    private A2AServiceResponse MapUnknownResponse(A2AResponse? response)
    {
        _logger.LogWarning("Unexpected A2A response type: {ResponseType}", response?.GetType().Name ?? "null");

        return new A2AServiceResponse
        {
            Success = false,
            Message = AgentConstants.ErrorMessages.UnexpectedResponseType,
            Error = $"Unknown response format: {response?.GetType().Name ?? "null"}"
        };
    }
}