using Shiny.DocumentDb;
using Shiny.AiConversation;

namespace Sample.Services;

public class DocumentDbMessageStore(IDocumentStore store) : IMessageStore
{
    public Task Store(AiChatMessage chatMessage, CancellationToken cancellationToken)
        => store.Insert(chatMessage, cancellationToken: cancellationToken);

    public Task Clear(DateTimeOffset? beforeDate = null)
    {
        var query = store.Query<AiChatMessage>();

        if (beforeDate.HasValue)
            query = query.Where(x => x.Timestamp <= beforeDate.Value);

        return query.ExecuteDelete();
    }

    public Task<IReadOnlyList<AiChatMessage>> Query(
        string? messageContains = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        int? limit = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = store.Query<AiChatMessage>()
            .OrderBy(x => x.Timestamp);

        if (!String.IsNullOrWhiteSpace(messageContains))
            query = query.Where(x => x.Message.Contains(messageContains));

        if (fromDate.HasValue)
            query = query.Where(x => x.Timestamp >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(x => x.Timestamp <= toDate.Value);

        if (limit.HasValue)
            query = query.Paginate(0, limit.Value);

        return query.ToList(cancellationToken);
    }
}
