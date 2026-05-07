using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Shiny.AiConversation.OpenAi;

public class OpenAiStaticChatProvider : IChatClientProvider
{
    readonly IChatClient chatClient;
    
    
    public OpenAiStaticChatProvider(string apiToken, string endpointUri, string modelName)
    {
        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(apiToken),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(endpointUri)
            }
        );
        this.chatClient = new ChatClientBuilder(openAiClient.GetChatClient(modelName).AsIChatClient())
            .UseLogging()
            .UseFunctionInvocation()
            .Build();
    }
    public Task<IChatClient> GetChatClient(CancellationToken cancelToken = default)
        => Task.FromResult(this.chatClient);
}