using Microsoft.Extensions.AI;
using Shiny.AiConversation;

namespace Sample.Blazor.Services;

public class SampleContextProvider : IContextProvider
{
    public IEnumerable<string> GetSystemPrompts(AiAcknowledgement acknowledgement)
    {
        yield return """
            You are a helpful assistant that provides information about the Shiny AI sample app. You can answer questions
            about the app's features, how to use it, and any other related information. If you don't know the answer to
            a question, it's okay to say you don't know.
            """;
    }

    public IEnumerable<AITool> GetTools() => [];
}
