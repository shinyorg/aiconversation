using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sample.Pages;
using Sample.Services;
using Shiny;
using Shiny.DocumentDb;
using Shiny.DocumentDb.Sqlite;
using Shiny.AiConversation;

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
                shell.Add<ChatPage, ChatViewModel>("chat");
                shell.Add<SettingsPage, SettingsViewModel>("settings");
                shell.Add<AuraPage, AuraViewModel>("aura");
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });
        
        builder.Services.AddSingleton<IContextProvider, SampleContextProvider>();
        builder.Services.AddShinyAiConversation(opts =>
        {
            opts.SetMessageStore<DocumentDbMessageStore>();
            opts.AddGithubCopilotChatClient();
        });
        
        builder.Services.AddDocumentStore(opts =>
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "sample_ai.db");
            opts.DatabaseProvider = new SqliteDatabaseProvider($"Data Source={dbPath}");
            opts.JsonSerializerOptions = AppJsonContext.Default.Options;
            opts.UseReflectionFallback = false;
        });

        // builder.Services.AddAzureSpeech("your-subscription-key", "eastus");

        // builder.Services.AddElevenLabsTextToSpeech(new ElevenLabsConfig
        // {
        //     ApiKey = "your-api-key",
        //     DefaultVoiceId = "21m00Tcm4TlvDq8ikWAM",  // Rachel (default)
        //     ModelId = "eleven_multilingual_v2"           // default
        // });


#if DEBUG
        builder.Logging.AddDebug();
#endif
        var app = builder.Build();
        return app;
    }
}


[JsonSerializable(typeof(AiChatMessage))]
public partial class AppJsonContext : JsonSerializerContext;
