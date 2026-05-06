using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shiny.AiConversation;
using Shiny.AiConversation.Infrastructure;

namespace Shiny;

/// <summary>
/// Extension methods for registering the Shiny AI service with dependency injection.
/// </summary>
public static class AiConversationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Shiny AI service and its dependencies. A chat client provider must be configured
    /// via the <paramref name="configure"/> callback or an <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Callback to configure the AI service options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddShinyAiConversation(this IServiceCollection services, Action<AiConversationOptions> configure)
    {
        var options = new AiConversationOptions(services);
        configure.Invoke(options);
        if (!options.IsChatClientProvided)
            throw new InvalidOperationException("You must configure a token provider using SetTokenProvider<TTokenProvider>() when registering the AI service.");
        
        if (options.AutoAddSpeechServices)
        {
            services.AddAudioPlayer();
            services.AddSpeechServices();
        }
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAiConversationService, AiConversationService>();
        return services;
    }
}