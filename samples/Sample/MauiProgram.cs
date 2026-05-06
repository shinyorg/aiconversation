using Microsoft.Extensions.Logging;
using Sample.Pages;
using Sample.Services;
using Shiny;
using Shiny.DocumentDb;
using Shiny.DocumentDb.Sqlite;
using Shiny.Maui.AiConversation;

namespace Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseShinyControls()
            .UseShinyShell(shell =>
            {
                shell.Add<LoginPage, LoginViewModel>("login");
                shell.Add<ChatPage, ChatViewModel>("chat");
                shell.Add<SettingsPage, SettingsViewModel>("settings");
                shell.Add<AuraPage, AuraViewModel>("aura");
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });


        builder.Services.AddDocumentStore(opts =>
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "sample_ai.db");
            opts.DatabaseProvider = new SqliteDatabaseProvider($"Data Source={dbPath}");
            opts.UseReflectionFallback = false;
        });
        
        // builder.Services.AddAzureSpeech("your-subscription-key", "eastus");
        
        // builder.Services.AddElevenLabsTextToSpeech(new ElevenLabsConfig
        // {
        //     ApiKey = "your-api-key",
        //     DefaultVoiceId = "21m00Tcm4TlvDq8ikWAM",  // Rachel (default)
        //     ModelId = "eleven_multilingual_v2"           // default
        // });
        
        builder.Services.AddSingleton<GitHubCopilotChatClientProvider>(sp => (GitHubCopilotChatClientProvider)sp.GetRequiredService<IChatClientProvider>());
        builder.Services.AddShinyAiConversation(opts =>
        {
            opts.SetMessageStore<DocumentDbMessageStore>(true);
            opts.SetChatClientProvider<GitHubCopilotChatClientProvider>();
        });

#if DEBUG
        builder.Logging.AddDebug();
#endif
        var app = builder.Build();

        var aiService = app.Services.GetRequiredService<IAiConversationService>();
        aiService.SystemPrompts.Add(
            """
            You are a helpful assistant that provides information about the Maui AI sample app. You can answer questions 
            about the app's features, how to use it, and any other related information. If you don't know the answer to 
            a question, it's okay to say you don't know.
            """
        );
        aiService.SoundResolver = name => FileSystem.OpenAppPackageFileAsync(name);
        aiService.OkSound = "ok.mp3";
        aiService.CancelSound = "cancel.mp3";
        aiService.ErrorSound = "error.mp3";
        aiService.ThinkSound = "think.mp3";
        aiService.RespondingSound = "responding.mp3";

        return app;
    }
}
