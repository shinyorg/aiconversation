using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shiny.Maui.AiConversation.Infrastructure;

namespace Shiny.Maui.AiConversation;

/// <summary>
/// Extension methods for registering the Shiny AI service with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Shiny AI service and its dependencies. A chat client provider must be configured
    /// via the <paramref name="configure"/> callback or an <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Callback to configure the AI service options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddShinyAi(this IServiceCollection services, Action<AiServiceOptions> configure)
    {
        var options = new AiServiceOptions(services);
        configure.Invoke(options);
        if (!options.IsChatClientProvided)
            throw new InvalidOperationException("You must configure a token provider using SetTokenProvider<TTokenProvider>() when registering the AI service.");
        
        if (options.AutoAddSpeechServices)
        {
            services.AddAudioPlayer();
            services.AddSpeechServices();
        }
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAiService, AiService>();
        return services;
    }
}


/// <summary>
/// Configuration options for the Shiny AI service, used during registration
/// to set up the chat client provider, message store, and optional AI tools.
/// </summary>
public class AiServiceOptions(IServiceCollection services)
{
    /// <summary>
    /// Returns true if a chat client provider has been registered.
    /// </summary>
    public bool IsChatClientProvided => services.Any(x => x.ServiceType == typeof(IChatClientProvider));
    
    /// <summary>
    /// Will call for Shiny Speech Service registration if true (default)
    /// </summary>
    public bool AutoAddSpeechServices { get; set; } = true;

    /// <summary>
    /// Registers the <see cref="IChatClientProvider"/> implementation used to obtain chat clients.
    /// </summary>
    /// <typeparam name="TTokenProvider">The concrete provider type.</typeparam>
    /// <returns>This options instance for chaining.</returns>
    public AiServiceOptions SetChatClientProvider<TTokenProvider>()
        where TTokenProvider : class, IChatClientProvider
    {
        services.TryAddSingleton<IChatClientProvider, TTokenProvider>();
        return this;
    }

    /// <summary>
    /// Registers an <see cref="IMessageStore"/> implementation for persisting chat history.
    /// Optionally registers a <see cref="ChatLookupAITool"/> that allows the AI to search past conversations.
    /// </summary>
    /// <typeparam name="TMessageStore">The concrete message store type.</typeparam>
    /// <param name="addAiLookupTool">When true, registers an AI tool for searching chat history.</param>
    /// <returns>This options instance for chaining.</returns>
    public AiServiceOptions SetMessageStore<TMessageStore>(bool addAiLookupTool = true)
        where TMessageStore : class, IMessageStore
    {
        services.AddSingleton<IMessageStore, TMessageStore>();

        if (addAiLookupTool)
        {
            services.AddSingleton<ChatLookupAITool>();
            services.AddSingleton<AITool>(sp => sp.GetRequiredService<ChatLookupAITool>().AsTool());
        }
        return this;
    }

    // TODO: initial config?
    // public AiAcknowledgement Acknowledgement { get; set; } = AiAcknowledgement.Full;
    // public string? CancelSound { get; set; }
    // public string? ErrorSound { get; set; }
    // public string? ThinkSound { get; set; }
    // public string? RespondingSound { get; set; }
}