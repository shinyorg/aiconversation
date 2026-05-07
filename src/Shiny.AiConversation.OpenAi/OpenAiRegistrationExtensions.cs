using Microsoft.Extensions.DependencyInjection.Extensions;
using Shiny.AiConversation;
using Shiny.AiConversation.OpenAi;

namespace Shiny;

public static class OpenAiRegistrationExtensions
{
    public static AiConversationOptions AddStaticOpenAIChatClient(
        this AiConversationOptions options,
        string apiToken,
        string endpointUri,
        string modelName
    )
    {
        options.Services.TryAddSingleton<IChatClientProvider>(_ =>
            new OpenAiStaticChatProvider(apiToken, endpointUri, modelName));
        return options;
    }
}