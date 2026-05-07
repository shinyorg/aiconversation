using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Sample.Blazor.Services;
using Shiny;
using Shiny.AiConversation;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Sample.Blazor.Components.App>("#app");

builder.Services.AddSingleton<InMemoryMessageStore>();
builder.Services.AddShinyAiConversation(opts =>
{
    opts.AddStaticOpenAIChatClient(
        "YOUR API KEY HERE",
        "https://api.openai.com/v1",
        "gpt-4o"
    );
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

await app.RunAsync();
