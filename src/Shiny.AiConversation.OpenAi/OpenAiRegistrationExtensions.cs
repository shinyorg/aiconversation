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
    
    public static AiConversationOptions AddStaticGithubCopilotChatClient(
        this AiConversationOptions options,
        string personalAccessToken,
        string modelName = "gpt-4o"
    )
    {
        options.Services.TryAddSingleton<IChatClientProvider>(_ =>
            new OpenAiStaticChatProvider(personalAccessToken, "https://api.githubcopilot.com", modelName));
        return options;
    }
}