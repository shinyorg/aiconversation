using Microsoft.Extensions.AI;
using Shiny.DocumentDb;
using Shiny.AiConversation;

namespace Sample.Services;

public class DocumentDbMessageStore(IDocumentStore store) : IMessageStore
{
    public Task Store(ChatMessage chatMessage, CancellationToken cancellationToken)
    {
        var direction = chatMessage.Role == ChatRole.User
            ? ChatMessageDirection.User
            : ChatMessageDirection.AI;

        return store.Insert(
            new AiChatMessage(
                Guid.NewGuid().ToString(),
                chatMessage.Text ?? "",
                DateTimeOffset.UtcNow,
                direction
            ),
            cancellationToken: cancellationToken
        );
    }

    public Task Store(string? userTriggeringMessage, ChatResponseUpdate? update, UsageDetails? usage, CancellationToken cancellationToken)
    {
        if (update?.Text is not { } text)
            return Task.CompletedTask;

        return store.Insert(
            new AiChatMessage(
                Guid.NewGuid().ToString(),
                text,
                DateTimeOffset.UtcNow,
                ChatMessageDirection.AI
            ),
            cancellationToken: cancellationToken
        );
    }

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
