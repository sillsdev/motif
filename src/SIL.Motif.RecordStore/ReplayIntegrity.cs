using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SIL.Motif.RecordStore;

/// <summary>Order-independent digest of every history row plus the commit identities it covers.</summary>
internal sealed record ReplayState(SetDigest Digest, IReadOnlySet<Guid> Commits)
{
    public string Final() => Digest.Final("replay");
}

internal static class ReplayIntegrity
{
    public static async Task<string> DigestAsync(RecordDatabase database) => (await ScanAsync(database)).Final();

    public static async Task<ReplayState> ScanAsync(RecordDatabase database)
    {
        var digest = new SetDigest();
        var commits = new HashSet<Guid>();
        await Rows(database, "Commits", "", null, row => { digest = digest.Add(row.Bytes); commits.Add(row.Id); });
        await Rows(database, "ChangeEntities", "", null, row => digest = digest.Add(row.Bytes));
        return new(digest, commits);
    }

    /// <summary>Adds the rows of the one commit this handle just wrote; any other unseen commit is refused.</summary>
    public static async Task<ReplayState> ExtendAsync(RecordDatabase database, ReplayState prior, Guid written)
    {
        var digest = prior.Digest;
        var commits = new HashSet<Guid>(prior.Commits);
        var added = new List<Guid>();
        await Rows(database, "Commits", "", null, row => { if (!commits.Contains(row.Id)) added.Add(row.Id); }, identitiesOnly: true);
        if (added.Count != 1 || added[0] != written)
            throw new ForeignCommitException("History contains a commit this handle did not write; reopen and verify the store.");
        foreach (var id in added)
        {
            await Rows(database, "Commits", "WHERE Id = $id", id, row => digest = digest.Add(row.Bytes));
            await Rows(database, "ChangeEntities", "WHERE CommitId = $id", id, row => digest = digest.Add(row.Bytes));
            commits.Add(id);
        }
        return new(digest, commits);
    }

    private sealed record Row(Guid Id, byte[] Bytes);

    private static async Task Rows(RecordDatabase database, string table, string where, Guid? id, Action<Row> each, bool identitiesOnly = false)
    {
        await database.Database.OpenConnectionAsync();
        try
        {
            await using var command = database.Database.GetDbConnection().CreateCommand();
            command.Transaction = database.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = identitiesOnly ? $"SELECT Id FROM {table} {where}" : $"SELECT * FROM {table} {where}";
            if (id.HasValue)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "$id";
                parameter.Value = NetworkGuidConverter.Write(id.Value);
                command.Parameters.Add(parameter);
            }
            await using var reader = await command.ExecuteReaderAsync();
            var identity = table == "Commits" ? reader.GetOrdinal("Id") : -1;
            while (await reader.ReadAsync())
            {
                using var row = new BinaryOutput();
                row.Text(table);
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    switch (reader.GetValue(index))
                    {
                        case DBNull: row.Byte(0); break;
                        case long value: row.Byte(1); row.Integer(value); break;
                        case double value: row.Byte(2); row.Integer(BitConverter.DoubleToInt64Bits(value)); break;
                        case string value: row.Byte(3); row.Text(value); break;
                        case byte[] value: row.Byte(4); row.Bytes(value); break;
                        default: throw new InvalidDataException("Unsupported stored replay value.");
                    }
                }
                each(new Row(identity >= 0 ? new Guid((byte[])reader[identity], bigEndian: true) : Guid.Empty, row.Finish()));
            }
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }
}
