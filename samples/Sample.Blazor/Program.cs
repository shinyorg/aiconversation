using Sample.Blazor.Services;
using Shiny;
using Shiny.AiConversation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<GitHubCopilotChatClientProvider>(sp =>
    (GitHubCopilotChatClientProvider)sp.GetRequiredService<IChatClientProvider>());

builder.Services.AddSingleton<InMemoryMessageStore>();
builder.Services.AddShinyAiConversation(opts =>
{
    opts.SetChatClientProvider<GitHubCopilotChatClientProvider>();
    opts.SetMessageStore<InMemoryMessageStore>();
});

var app = builder.Build();

var aiService = app.Services.GetRequiredService<IAiConversationService>();
aiService.SystemPrompts.Add(
    """
    You are a helpful assistant that provides information about the Shiny AI sample app. You can answer questions
    about the app's features, how to use it, and any other related information. If you don't know the answer to
    a question, it's okay to say you don't know.
    """
);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<Sample.Blazor.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
