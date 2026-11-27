using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SIL.Motif.RecordStore;

public sealed partial class RecordStore
{
    private async Task<T> ReadPublishedAsync<T>(Func<StoreState, Task<T>> read, CancellationToken ct = default,
        bool lazyContent = false)
    {
        ct.ThrowIfCancellationRequested();
        if (database.Database.CurrentTransaction is not null) return await read(await PublishedStateAsync(ct, lazyContent));
        await database.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqliteConnection)database.Database.GetDbConnection();
            // Deferred readers keep one WAL point without reserving the writer while their queries run.
            await using var sqliteTransaction = connection.BeginTransaction(deferred: true);
            await using var transaction = (await database.Database.UseTransactionAsync(sqliteTransaction, ct))!;
            var result = await read(await PublishedStateAsync(ct, lazyContent));
            await transaction.CommitAsync(ct);
            return result;
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }
}
