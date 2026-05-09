using Microsoft.Extensions.AI;

namespace Shiny.AiConversation.Infrastructure;

public class DefaultContextProvider(
    TimeProvider timeProvider,
    IEnumerable<AITool> tools,
    IMessageStore? messageStore = null
) : IContextProvider
{
    public IEnumerable<string> GetSystemPrompts(AiAcknowledgement acknowledgement)
    {
        if (acknowledgement == AiAcknowledgement.LessWordy)
            yield return "Be concise and brief in your responses. Avoid unnecessary elaboration.";

        if (acknowledgement >= AiAcknowledgement.LessWordy)
            yield return "You are in a real-time voice conversation. Keep responses short and conversational. " +
                "When you need more information or want to clarify something, end your response with a question so the conversation flows naturally.";
        
        yield return $"The current time is {timeProvider.GetUtcNow().ToLocalTime():hh:mm tt} on {timeProvider.GetUtcNow().ToLocalTime():MMMM dd, yyyy}.";
        
    }

    public IEnumerable<AITool> GetTools()
        => tools;
}