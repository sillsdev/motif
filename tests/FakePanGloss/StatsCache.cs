using Microsoft.Data.Sqlite;

namespace SIL.Motif.FakePanGloss;

/// <summary>
/// Writes a batch statistics cache in the tables Motif's statistics reader queries — <c>word</c>,
/// <c>object</c> and <c>fact</c> — with one row per requested word and no object facts, so an Assessment
/// run through the fake completes rather than failing to read its statistics.
/// </summary>
internal static class StatsCache
{
    internal sealed record WordRow(string Form, int Attempts, int Passes, long? WorkSteps);

    internal static void Write(string path, IReadOnlyList<WordRow> words)
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
                CREATE TABLE cache_identity (cache_id INTEGER PRIMARY KEY, schema_version INTEGER NOT NULL);
                CREATE TABLE run (counter_semantics INTEGER NOT NULL);
                INSERT INTO run VALUES (8);
                CREATE TABLE word (word_id INTEGER PRIMARY KEY, form TEXT NOT NULL, elapsed_ns INTEGER NOT NULL,
                                   attempts INTEGER NOT NULL, passes INTEGER NOT NULL, work INTEGER NULL);
                CREATE TABLE object (object_id INTEGER PRIMARY KEY, key TEXT NOT NULL, identity_quality TEXT NOT NULL,
                                     kind TEXT NOT NULL, label TEXT NOT NULL);
                CREATE TABLE fact (word_id INTEGER NOT NULL, object_id INTEGER NOT NULL,
                                   direction TEXT NOT NULL, attempts INTEGER NOT NULL, self_time_ns INTEGER, work_steps INTEGER);
                INSERT INTO cache_identity VALUES (1, 10);
                """;
            create.ExecuteNonQuery();
        }
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO word (form, elapsed_ns, attempts, passes, work) " +
            "VALUES ($form, 0, $attempts, $passes, $work);";
        var form = insert.Parameters.Add("$form", SqliteType.Text);
        var attempts = insert.Parameters.Add("$attempts", SqliteType.Integer);
        var passes = insert.Parameters.Add("$passes", SqliteType.Integer);
        var work = insert.Parameters.Add("$work", SqliteType.Integer);
        insert.Prepare();
        foreach (var word in words.GroupBy(word => word.Form, StringComparer.Ordinal).Select(group => group.First()))
        {
            form.Value = word.Form;
            attempts.Value = word.Attempts;
            passes.Value = word.Passes;
            work.Value = word.WorkSteps is { } value ? value : DBNull.Value;
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
