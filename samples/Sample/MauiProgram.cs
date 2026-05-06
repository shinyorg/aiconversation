using Microsoft.Extensions.Logging;
using Sample.Pages;
using Sample.Services;
using Shiny;
using Shiny.DocumentDb;
using Shiny.DocumentDb.Sqlite;
using Shiny.Maui.AI;

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
        
        builder.Services.AddSingleton<GitHubCopilotChatClientProvider>();
        builder.Services.AddSingleton<IChatClientProvider>(sp => sp.GetRequiredService<GitHubCopilotChatClientProvider>());
        builder.Services.AddShinyAi(opts =>
        {
            opts.SetMessageStore<DocumentDbMessageStore>();
        });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        var aiService = app.Services.GetRequiredService<IAiService>();
        aiService.SystemPrompts.Add(
            """
            You are a helpful assistant that provides information about the Maui AI sample app. You can answer questions 
            about the app's features, how to use it, and any other related information. If you don't know the answer to 
            a question, it's okay to say you don't know.
            """
        );
        aiService.OkSound = "ok.mp3";
        aiService.CancelSound = "cancel.mp3";
        aiService.ErrorSound = "error.mp3";
        aiService.ThinkSound = "think.mp3";
        aiService.RespondingSound = "responding.mp3";

        return app;
    }
}
