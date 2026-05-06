namespace Shiny.Maui.AI;

public interface IMessageStore
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="chatMessage"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task Store(AiChatMessage chatMessage, CancellationToken cancellationToken);

    /// <summary>
    /// Clears all messages from beforeDate or all if date is null
    /// </summary>
    /// <param name="beforeDate"></param>
    /// <returns></returns>
    Task Clear(DateTimeOffset? beforeDate = null);
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="messageContains"></param>
    /// <param name="fromDate"></param>
    /// <param name="toDate"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<IReadOnlyList<AiChatMessage>> Query(
        string? messageContains = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        int? limit = null,
        CancellationToken cancellationToken = default
    );
}