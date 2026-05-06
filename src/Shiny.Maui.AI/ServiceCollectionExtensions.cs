using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Plugin.Maui.Audio;
using Shiny.Maui.AI.Infrastructure;

namespace Shiny.Maui.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShinyAi(this IServiceCollection services, Action<AiServiceOptions> configure)
    {
        var options = new AiServiceOptions(services);
        configure.Invoke(options);
        if (!options.IsTokenProviderSet)
            throw new InvalidOperationException("You must configure a token provider using SetTokenProvider<TTokenProvider>() when registering the AI service.");
        
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(AudioManager.Current);

        services.AddSingleton<IAiService, AiService>();
        return services;
    }
}


public class AiServiceOptions(IServiceCollection services)
{
    public bool IsTokenProviderSet => services.Any(x => x.ServiceType == typeof(IChatClientProvider));
    
    public AiServiceOptions SetTokenProvider<TTokenProvider>()
        where TTokenProvider : class, IChatClientProvider
    {
        services.TryAddSingleton<IChatClientProvider, TTokenProvider>();
        return this;
    }

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