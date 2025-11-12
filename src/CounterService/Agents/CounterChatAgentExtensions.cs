using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CounterService.Agents;

public static class CounterChatAgentExtensions
{
    public static AIAgent BuildAIAgentForAGUI(this IServiceProvider sp, string endpoint, string apiKey, string chatModelId)
    {
        var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var logger = sp.GetRequiredService<ILogger<CounterChatAgent>>();
        var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
        var configuration = sp.GetRequiredService<IConfiguration>();
        var chatClient = new AzureOpenAIClient(
                  new Uri(endpoint),
                  new ApiKeyCredential(apiKey))
                    .GetChatClient(chatModelId)
                    .AsIChatClient();

        var agent = new CounterChatAgent(chatClient, configuration, clientFactory, httpContextAccessor, logger);
        return agent;
    }
}
