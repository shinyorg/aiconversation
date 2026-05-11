using Microsoft.Extensions.AI;
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
        string modelName,
        Action<ChatClientBuilder>? action = null
    )
    {
        options.Services.TryAddSingleton<IChatClientProvider>(sp =>
            new OpenAiStaticChatProvider(sp, apiToken, endpointUri, modelName, action));
        return options;
    }

    public static AiConversationOptions AddStaticGithubCopilotChatClient(
        this AiConversationOptions options,
        string personalAccessToken,
        string modelName = "gpt-4o",
        Action<ChatClientBuilder>? action = null
    ) => options.AddStaticOpenAIChatClient(personalAccessToken, "https://api.githubcopilot.com", modelName, action);
}