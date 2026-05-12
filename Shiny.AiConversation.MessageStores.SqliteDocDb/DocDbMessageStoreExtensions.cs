using System.Text.Json.Serialization;
using Shiny.AiConversation;
using Shiny.AiConversation.MessageStores.SqliteDocDb;
using Shiny.DocumentDb;
using Shiny.DocumentDb.Sqlite;

namespace Shiny;

public static class DocDbMessageStoreExtensions
{
    public const string SqliteDiRegKey = "ShinyAiConvMessageStoreDocDb";
    
    public static AiConversationOptions SetSqliteDocDbMessageStore(this AiConversationOptions options, string databasePath)
    {
        options.SetMessageStore<DocumentDbMessageStore>();
        options.Services.AddDocumentStore(SqliteDiRegKey, opts =>
        {
            opts.UseReflectionFallback = false;
            opts.JsonSerializerOptions = AppJsonContext.Default.Options;
            opts.DatabaseProvider = new SqliteDatabaseProvider(databasePath);
        });
        return options;
    }
}

[JsonSerializable(typeof(AiChatMessage))]
public partial class AppJsonContext : JsonSerializerContext;