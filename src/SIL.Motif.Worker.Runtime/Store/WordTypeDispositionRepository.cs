using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Dispositions;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>
/// The word type dispositions of one store. A type has at most one standing row; a replaced or cleared row stays as
/// history. This class owns the rows and the rule that a call writes every form or none; what may be asked of a verb
/// lives in <c>WordTypeDispositionCommands</c>. Every lookup seeks an index on the word type key.
/// </summary>
public sealed class WordTypeDispositionRepository
{
    private readonly MotifDatabase _database;
    private readonly IJobClock _clock;

    /// <summary>Opens the dispositions of one store.</summary>
    public WordTypeDispositionRepository(MotifDatabase database, IJobClock? clock = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _clock = clock ?? new SystemJobClock();
    }

    private const string Columns = "FormKey, Form, Category, Evidence, Intended, RecordedBy, RecordedUtc";

    /// <summary>A form a call asked to give another disposition than the one that stands.</summary>
    /// <param name="Form">The form as it was given.</param>
    /// <param name="Standing">The disposition that stands for the type.</param>
    public sealed record Contradiction(string Form, WordTypeDisposition Standing);

    /// <summary>What <see cref="Record"/> did: the per-type results, or the contradictions that stopped it with nothing written.</summary>
    public sealed record RecordOutcome(
        IReadOnlyList<DisposeFormResult> Results, IReadOnlyList<Contradiction> Contradictions);

    /// <summary>
    /// Gives each form's word type the category, in one transaction. A type that already stands with the same category
    /// and intended form is left alone. A type that stands otherwise is a contradiction unless <paramref name="replace"/>,
    /// which ends the standing row as replaced. Any contradiction writes nothing.
    /// </summary>
    public RecordOutcome Record(IReadOnlyList<string> forms, DispositionCategory category, string evidence,
        string? intended, string by, bool replace)
    {
        var wire = DispositionCategories.Wire(category);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = Now();
        var results = new List<DisposeFormResult>();
        var contradictions = new List<Contradiction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var form in forms)
        {
            var key = WordTypeKey.Of(form);
            if (!seen.Add(key)) continue;
            var standing = Current(connection, transaction, key);
            if (standing is not null && standing.Category == wire && standing.Intended == intended)
            {
                results.Add(new(form, key, "already-recorded", standing));
                continue;
            }
            if (standing is not null && !replace)
            {
                contradictions.Add(new(form, standing));
                continue;
            }
            if (standing is not null) End(connection, transaction, key, "replaced", now, by, null);
            var created = new WordTypeDisposition(key, form, wire, evidence, intended, by, now);
            Insert(connection, transaction, created);
            results.Add(new(form, key, standing is null ? "recorded" : "replaced", created));
        }
        if (contradictions.Count > 0)
        {
            transaction.Rollback();
            return new([], contradictions);
        }
        transaction.Commit();
        return new(results, []);
    }

    /// <summary>Ends the standing disposition of each form's type as cleared, in one transaction; a type with none is reported and left alone.</summary>
    public IReadOnlyList<UndisposeFormResult> Clear(IReadOnlyList<string> forms, string by, string? note)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = Now();
        var results = new List<UndisposeFormResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var form in forms)
        {
            var key = WordTypeKey.Of(form);
            if (!seen.Add(key)) continue;
            var standing = Current(connection, transaction, key);
            if (standing is not null) End(connection, transaction, key, "cleared", now, by, note);
            results.Add(new(form, key, standing is null ? "not-recorded" : "cleared", standing));
        }
        transaction.Commit();
        return results;
    }

    /// <summary>The disposition that stands for the type of <paramref name="form"/>, or null. A seek on the current-row index.</summary>
    public WordTypeDisposition? Get(string form)
    {
        using var connection = _database.OpenConnection();
        return Current(connection, null, WordTypeKey.Of(form));
    }

    /// <summary>Every disposition that once stood for the type of <paramref name="form"/> and no longer does, newest first.</summary>
    public IReadOnlyList<EndedWordTypeDisposition> History(string form)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns}, EndedKind, EndedUtc, EndedBy, EndedNote FROM WordTypeDispositions " +
            "WHERE FormKey = $key AND EndedUtc IS NOT NULL ORDER BY DispositionId DESC;";
        command.Parameters.AddWithValue("$key", WordTypeKey.Of(form));
        using var reader = command.ExecuteReader();
        var ended = new List<EndedWordTypeDisposition>();
        while (reader.Read())
            ended.Add(new(Read(reader), reader.GetString(7), reader.GetString(8), reader.GetString(9), Str(reader, 10)));
        return ended;
    }

    /// <summary>One page of standing dispositions in key order, and how many match; <paramref name="category"/> null means all.</summary>
    public (IReadOnlyList<WordTypeDisposition> Page, int Total) List(DispositionCategory? category, int offset, int limit)
    {
        using var connection = _database.OpenConnection();
        var filter = category is null ? "" : " AND Category = $category";
        var wire = category is { } chosen ? DispositionCategories.Wire(chosen) : null;
        using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM WordTypeDispositions WHERE EndedUtc IS NULL{filter};";
        if (wire is not null) count.Parameters.AddWithValue("$category", wire);
        var total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM WordTypeDispositions WHERE EndedUtc IS NULL{filter} " +
            "ORDER BY " + (wire is null ? "FormKey" : "Category, FormKey") + " LIMIT $limit OFFSET $offset;";
        if (wire is not null) command.Parameters.AddWithValue("$category", wire);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var page = new List<WordTypeDisposition>();
        while (reader.Read()) page.Add(Read(reader));
        return (page, total);
    }

    /// <summary>How many types stand in each category, every category listed, in the order <see cref="DispositionCategories.All"/> gives.</summary>
    public IReadOnlyList<DispositionCategoryCount> CountsByCategory()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Category, COUNT(*) FROM WordTypeDispositions WHERE EndedUtc IS NULL GROUP BY Category;";
        using var reader = command.ExecuteReader();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        while (reader.Read()) counts[reader.GetString(0)] = reader.GetInt32(1);
        return [.. DispositionCategories.All.Select(category => DispositionCategories.Wire(category))
            .Select(name => new DispositionCategoryCount(name, counts.GetValueOrDefault(name)))];
    }

    private static WordTypeDisposition? Current(SqliteConnection connection, SqliteTransaction? transaction, string key)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {Columns} FROM WordTypeDispositions WHERE FormKey = $key AND EndedUtc IS NULL;";
        command.Parameters.AddWithValue("$key", key);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, WordTypeDisposition row)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO WordTypeDispositions (FormKey, Form, Category, Evidence, Intended, RecordedBy, RecordedUtc) " +
            "VALUES ($key, $form, $category, $evidence, $intended, $by, $at);";
        command.Parameters.AddWithValue("$key", row.Key);
        command.Parameters.AddWithValue("$form", row.Form);
        command.Parameters.AddWithValue("$category", row.Category);
        command.Parameters.AddWithValue("$evidence", row.Evidence);
        command.Parameters.AddWithValue("$intended", (object?)row.Intended ?? DBNull.Value);
        command.Parameters.AddWithValue("$by", row.RecordedBy);
        command.Parameters.AddWithValue("$at", row.RecordedUtc);
        command.ExecuteNonQuery();
    }

    private static void End(SqliteConnection connection, SqliteTransaction transaction, string key, string kind,
        string at, string by, string? note)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE WordTypeDispositions SET EndedUtc = $at, EndedKind = $kind, EndedBy = $by, EndedNote = $note " +
            "WHERE FormKey = $key AND EndedUtc IS NULL;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$at", at);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$by", by);
        command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static WordTypeDisposition Read(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), Str(reader, 4),
        reader.GetString(5), reader.GetString(6));

    private static string? Str(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);

    private string Now() => _clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
