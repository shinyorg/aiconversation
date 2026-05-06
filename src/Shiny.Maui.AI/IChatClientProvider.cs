using Microsoft.Extensions.AI;

namespace Shiny.Maui.AI;

public interface IChatClientProvider
{
    Task<IChatClient> GetChatClient(CancellationToken cancelToken = default);
}