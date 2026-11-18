using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SIL.Motif.RecordStore;

internal static class ReferencePersistence
{
    public static void Create(RecordDatabase database) => database.Database.ExecuteSqlRaw("""
        CREATE TABLE IncomingReferences (
            Target BLOB NOT NULL, Source BLOB NOT NULL, Field INTEGER NOT NULL,
            Position INTEGER NOT NULL, Owning INTEGER NOT NULL, Occurrence TEXT NOT NULL,
            PRIMARY KEY (Target, Source, Field, Position, Occurrence)
        ) WITHOUT ROWID
        """);

    public static async Task ReplaceAsync(RecordDatabase database, IEnumerable<NativeReference> edges)
    {
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = database.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "DELETE FROM IncomingReferences";
        await command.ExecuteNonQueryAsync();
        command.CommandText = "INSERT INTO IncomingReferences(Target,Source,Field,Position,Owning,Occurrence) VALUES($target,$source,$field,$position,$owning,$occurrence)";
        foreach (var name in new[] { "$target", "$source", "$field", "$position", "$owning", "$occurrence" })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = name == "$occurrence" ? DbType.String : name is "$target" or "$source" ? DbType.Binary : DbType.Int32;
            command.Parameters.Add(parameter);
        }
        command.Prepare();
        foreach (var edge in edges)
        {
            command.Parameters[0].Value = NetworkGuidConverter.Write(edge.Target);
            command.Parameters[1].Value = NetworkGuidConverter.Write(edge.Source);
            command.Parameters[2].Value = edge.Field;
            command.Parameters[3].Value = edge.Position;
            command.Parameters[4].Value = edge.Owning ? 1 : 0;
            command.Parameters[5].Value = edge.Occurrence;
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Applies one single-reference edit to the persisted index inside the caller's transaction.</summary>
    public static async Task ApplyAsync(RecordDatabase database, NativeReference? removed, NativeReference? added)
    {
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = database.Database.CurrentTransaction!.GetDbTransaction();
        void Run(string text, NativeReference edge)
        {
            command.Parameters.Clear();
            command.CommandText = text;
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$target", NetworkGuidConverter.Write(edge.Target)));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$source", NetworkGuidConverter.Write(edge.Source)));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$field", edge.Field));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$position", edge.Position));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$owning", edge.Owning ? 1 : 0));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$occurrence", edge.Occurrence));
            if (command.ExecuteNonQuery() != 1) throw new InvalidDataException("The persisted reference index does not match the published point.");
        }
        if (removed is not null) Run("DELETE FROM IncomingReferences WHERE Target=$target AND Source=$source AND Field=$field AND Position=$position AND Occurrence=$occurrence AND Owning=$owning", removed);
        if (added is not null) Run("INSERT INTO IncomingReferences(Target,Source,Field,Position,Owning,Occurrence) VALUES($target,$source,$field,$position,$owning,$occurrence)", added);
        await Task.CompletedTask;
    }

    public static async Task<NativeReference[]> ReadAsync(RecordDatabase database)
    {
        await database.Database.OpenConnectionAsync();
        try
        {
            await using var command = database.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT Source,Field,Position,Target,Owning,Occurrence FROM IncomingReferences";
            await using var reader = await command.ExecuteReaderAsync();
            var edges = new List<NativeReference>();
            while (await reader.ReadAsync()) edges.Add(new NativeReference(
                new Guid((byte[])reader[0], bigEndian: true), reader.GetInt32(1), reader.GetInt32(2),
                new Guid((byte[])reader[3], bigEndian: true), reader.GetBoolean(4), reader.GetString(5)));
            return edges.ToArray();
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }
}
