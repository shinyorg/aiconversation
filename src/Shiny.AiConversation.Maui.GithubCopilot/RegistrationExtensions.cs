using Microsoft.Extensions.DependencyInjection;
using Shiny.AiConversation;
using Shiny.AiConversation.Maui.GithubCopilot;

namespace Shiny;

public static class GithubCopilotRegistrationExtensions
{
    extension(AiConversationOptions options)
    {
        public AiConversationOptions AddGithubCopilotChatClient()
        {
            options.Services.AddSingleton<GitHubCopilotChatClientProvider>();
            options.Services.AddSingleton<IChatClientProvider>(sp =>
                sp.GetRequiredService<GitHubCopilotChatClientProvider>());
            return options;
        }
    }
}
