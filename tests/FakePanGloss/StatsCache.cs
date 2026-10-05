using Microsoft.Data.Sqlite;

namespace SIL.Motif.FakePanGloss;

/// <summary>
/// Writes a batch statistics cache in the tables Motif's statistics reader queries — <c>word</c>,
/// <c>object</c> and <c>fact</c> — with one row per requested word and no object facts, so an Assessment
/// run through the fake completes rather than failing to read its statistics.
/// </summary>
internal static class StatsCache
{
    internal static void Write(string path, IReadOnlyList<string> words)
    {
        if (File.Exists(path)) File.Delete(path);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE word (word_id INTEGER PRIMARY KEY, form TEXT NOT NULL, elapsed_ns INTEGER NOT NULL,
                                   attempts INTEGER NOT NULL, passes INTEGER NOT NULL);
                CREATE TABLE object (object_id INTEGER PRIMARY KEY, key TEXT NOT NULL, identity_quality TEXT NOT NULL,
                                     kind TEXT NOT NULL, label TEXT NOT NULL);
                CREATE TABLE fact (word_id INTEGER NOT NULL, object_id INTEGER NOT NULL,
                                   direction TEXT NOT NULL, attempts INTEGER NOT NULL, self_time_ns INTEGER);
                """;
            create.ExecuteNonQuery();
        }
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO word (form, elapsed_ns, attempts, passes) VALUES ($form, 0, 1, 1);";
        var form = insert.Parameters.Add("$form", SqliteType.Text);
        insert.Prepare();
        foreach (var word in words.Distinct(StringComparer.Ordinal))
        {
            form.Value = word;
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
