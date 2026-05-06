namespace Shiny.Maui.AI;

public record AiChatMessage(
    string Id,
    string Message,
    DateTimeOffset Timestamp,
    ChatMessageDirection Direction
);

public enum ChatMessageDirection
{
    User,
    AI
}