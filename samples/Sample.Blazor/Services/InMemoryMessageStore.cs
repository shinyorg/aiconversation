using Shiny.AiConversation;

namespace Sample.Blazor.Services;

public class InMemoryMessageStore : IMessageStore
{
    readonly List<AiChatMessage> messages = [];
    readonly object sync = new();

    public Task Store(AiChatMessage chatMessage, CancellationToken cancellationToken)
    {
        lock (sync)
            messages.Add(chatMessage);

        return Task.CompletedTask;
    }

    public Task Clear(DateTimeOffset? beforeDate = null)
    {
        lock (sync)
        {
            if (beforeDate.HasValue)
                messages.RemoveAll(m => m.Timestamp <= beforeDate.Value);
            else
                messages.Clear();
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AiChatMessage>> Query(
        string? messageContains = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            IEnumerable<AiChatMessage> query = messages.OrderBy(m => m.Timestamp);

            if (!String.IsNullOrWhiteSpace(messageContains))
                query = query.Where(m => m.Message.Contains(messageContains, StringComparison.OrdinalIgnoreCase));

            if (fromDate.HasValue)
                query = query.Where(m => m.Timestamp >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(m => m.Timestamp <= toDate.Value);

            if (limit.HasValue)
                query = query.Take(limit.Value);

            return Task.FromResult<IReadOnlyList<AiChatMessage>>(query.ToList().AsReadOnly());
        }
    }
}
