using Microsoft.Extensions.AI;

namespace Shiny.AiConversation;

public interface IContextProvider
{
    IEnumerable<string> GetSystemPrompts(AiAcknowledgement acknowledgement);

    IEnumerable<AITool> GetTools();
}