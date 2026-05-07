# IMessageStore

## Interface

**Namespace**: `Shiny.AiConversation`

```csharp
public interface IMessageStore
{
    Task Store(ChatMessage chatMessage, CancellationToken cancellationToken);
    Task Store(string? userTriggeringMessage, ChatResponseUpdate? update, UsageDetails? usage, CancellationToken cancellationToken);
    Task Clear(DateTimeOffset? beforeDate = null);
    Task<IReadOnlyList<AiChatMessage>> Query(
        string? messageContains = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        int? limit = null,
        CancellationToken cancellationToken = default
    );
}
```

## Methods

### Store (ChatMessage)
Persists a user or assistant `ChatMessage`. Called automatically by AiConversationService for the user message.

### Store (ChatResponseUpdate)
Persists streaming response metadata including the `ChatResponseUpdate` chunk, the user's triggering message, and optional `UsageDetails`. Called per streaming chunk during AI response.

### Clear
Removes messages from the store:
- `beforeDate = null` — clears all messages
- `beforeDate` specified — removes only messages older than the given date

### Query
Searches the message store with optional filtering:
- `messageContains` — text search against message content
- `fromDate` / `toDate` — inclusive date range filter
- `limit` — maximum number of results to return
- Returns messages ordered by timestamp

## Implementation Example

Using Shiny.DocumentDb:

```csharp
using Shiny.DocumentDb;
using Shiny.AiConversation;

public class DocumentDbMessageStore(IDocumentStore store) : IMessageStore
{
    public Task Store(ChatMessage chatMessage, CancellationToken cancellationToken)
    {
        var direction = chatMessage.Role == ChatRole.User
            ? ChatMessageDirection.User
            : ChatMessageDirection.AI;

        return store.Insert(
            new AiChatMessage(Guid.NewGuid().ToString(), chatMessage.Text ?? "", DateTimeOffset.UtcNow, direction),
            cancellationToken: cancellationToken
        );
    }

    public Task Store(string? userTriggeringMessage, ChatResponseUpdate? update, UsageDetails? usage, CancellationToken cancellationToken)
    {
        if (update?.Text is not { } text)
            return Task.CompletedTask;

        return store.Insert(
            new AiChatMessage(Guid.NewGuid().ToString(), text, DateTimeOffset.UtcNow, ChatMessageDirection.AI),
            cancellationToken: cancellationToken
        );
    }

    public async Task Clear(DateTimeOffset? beforeDate = null)
    {
        var query = store.Query<AiChatMessage>();
        if (beforeDate.HasValue)
            query = query.Where(x => x.Timestamp <= beforeDate.Value);
        await query.ExecuteDelete();
    }

    public async Task<IReadOnlyList<AiChatMessage>> Query(
        string? messageContains = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var query = store.Query<AiChatMessage>().OrderBy(x => x.Timestamp);

        if (!String.IsNullOrWhiteSpace(messageContains))
            query = query.Where(x => x.Message.Contains(messageContains));

        if (fromDate != null)
            query = query.Where(x => x.Timestamp >= fromDate.Value);

        if (toDate != null)
            query = query.Where(x => x.Timestamp <= toDate.Value);

        if (limit != null)
            query = query.Paginate(0, limit.Value);

        return await query.ToList(cancellationToken);
    }
}
```

## Registration

```csharp
builder.Services.AddShinyAiConversation(opts =>
{
    opts.SetChatClientProvider<MyChatClientProvider>();
    opts.SetMessageStore<DocumentDbMessageStore>();
});
```
