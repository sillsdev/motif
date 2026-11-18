using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SIL.Motif.RecordStore;

public sealed partial class RecordStore
{
    private async Task<T> ReadPublishedAsync<T>(Func<StoreState, Task<T>> read)
    {
        if (database.Database.CurrentTransaction is not null) return await read(await PublishedStateAsync());
        await database.Database.OpenConnectionAsync();
        try
        {
            var connection = (SqliteConnection)database.Database.GetDbConnection();
            // Deferred readers keep one WAL point without reserving the writer while their queries run.
            await using var sqliteTransaction = connection.BeginTransaction(deferred: true);
            await using var transaction = (await database.Database.UseTransactionAsync(sqliteTransaction))!;
            var result = await read(await PublishedStateAsync());
            await transaction.CommitAsync();
            return result;
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }
}
