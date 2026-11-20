using System.Globalization;
using System.Text;
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

    /// <summary>One row to record, already checked and keyed; <see cref="Row"/> says where it came from.</summary>
    /// <param name="Row">The row number in the source, or the position of the form in the call.</param>
    /// <param name="Form">The form as it was given.</param>
    /// <param name="Key">The word type key, computed once.</param>
    /// <param name="Category">The wire name of the category.</param>
    /// <param name="Intended">The intended form of a misspelling, or null.</param>
    /// <param name="Evidence">The evidence for the disposition.</param>
    public sealed record Entry(int Row, string Form, string Key, string Category, string? Intended, string Evidence);

    /// <summary>What happened to one <see cref="Entry"/>: a <see cref="BulkStatus"/> and the disposition that stands, or stood, for its type.</summary>
    public sealed record BulkRow(Entry Entry, string Status, WordTypeDisposition? Standing, WordTypeDisposition? Result);

    /// <summary>The per-row statuses of <see cref="RecordMany"/>.</summary>
    public static class BulkStatus
    {
        /// <summary>The type had none, so a row was written.</summary>
        public const string Recorded = "recorded";
        /// <summary>Another disposition stood and was ended for this one.</summary>
        public const string Replaced = "replaced";
        /// <summary>The type already stands with this category and intended form and its evidence already says this.</summary>
        public const string AlreadyRecorded = "already-recorded";
        /// <summary>The type already stands as asked; the new evidence was appended to its evidence.</summary>
        public const string EvidenceAdded = "evidence-added";
        /// <summary>The type stands with another disposition and the call did not ask to replace it.</summary>
        public const string Contradiction = "contradiction";
        /// <summary>An earlier row of the same call gave the type another disposition.</summary>
        public const string ConflictsInCall = "conflicts-in-call";
    }

    /// <summary>What <see cref="RecordMany"/> did.</summary>
    /// <param name="Rows">One entry per input row, in order.</param>
    /// <param name="Written">Whether the changes were committed.</param>
    public sealed record BulkOutcome(IReadOnlyList<BulkRow> Rows, bool Written);

    /// <summary>
    /// Gives each form's word type the category, in one transaction. A type that already stands with the same category
    /// and intended form is left alone, except that evidence it does not yet hold is appended. A type that stands otherwise
    /// is a contradiction unless <paramref name="replace"/>, which ends the standing row as replaced. Any contradiction writes nothing.
    /// </summary>
    public RecordOutcome Record(IReadOnlyList<string> forms, DispositionCategory category, string evidence,
        string? intended, string by, bool replace)
    {
        var wire = DispositionCategories.Wire(category);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<Entry>();
        foreach (var form in forms)
        {
            var key = WordTypeKey.Of(form);
            if (seen.Add(key)) entries.Add(new(entries.Count + 1, form, key, wire, intended, evidence));
        }
        var outcome = RecordMany(entries, by, replace, priorRefusals: 0, skipRefused: false, dryRun: false);
        var contradictions = outcome.Rows.Where(row => row.Status == BulkStatus.Contradiction)
            .Select(row => new Contradiction(row.Entry.Form, row.Standing!)).ToArray();
        if (contradictions.Length > 0) return new([], contradictions);
        return new([.. outcome.Rows.Select(row => new DisposeFormResult(row.Entry.Form, row.Entry.Key,
            row.Status, row.Result!))], []);
    }

    /// <summary>
    /// Records many entries in one transaction with one key lookup per entry. A contradiction, or a refusal the caller
    /// already counted in <paramref name="priorRefusals"/>, stops the write unless <paramref name="skipRefused"/>, which
    /// writes the rest. A dry run reports the same statuses and writes nothing; a null database is an empty store.
    /// </summary>
    public BulkOutcome RecordMany(IReadOnlyList<Entry> entries, string by, bool replace, int priorRefusals,
        bool skipRefused, bool dryRun) =>
        RecordCore(_database, _clock, entries, by, replace, priorRefusals, skipRefused, dryRun);

    /// <summary>The statuses a dry run reports against a store that does not exist yet, which holds no dispositions.</summary>
    public static BulkOutcome PlanOnEmptyStore(IReadOnlyList<Entry> entries, string by, bool replace) =>
        RecordCore(null, new SystemJobClock(), entries, by, replace, 0, false, true);

    private static BulkOutcome RecordCore(MotifDatabase? database, IJobClock clock, IReadOnlyList<Entry> entries,
        string by, bool replace, int priorRefusals, bool skipRefused, bool dryRun)
    {
        var write = !dryRun && database is not null;
        using var connection = database?.OpenConnection();
        using var transaction = write ? connection!.BeginTransaction() : null;
        var now = clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        using var lookup = Prepare(connection, transaction,
            $"SELECT {Columns} FROM WordTypeDispositions WHERE FormKey = $key AND EndedUtc IS NULL;", "$key");
        var seen = new Dictionary<string, (string Category, string? Intended)>(StringComparer.Ordinal);
        var rows = new List<BulkRow>(entries.Count);
        var refused = priorRefusals;
        foreach (var raw in entries)
        {
            var entry = raw with { Intended = raw.Intended?.Normalize(NormalizationForm.FormD) };
            if (seen.TryGetValue(entry.Key, out var first))
            {
                if (first.Category == entry.Category && SameIntended(first.Intended, entry.Intended))
                    rows.Add(new(entry, BulkStatus.AlreadyRecorded, null, null));
                else
                {
                    rows.Add(new(entry, BulkStatus.ConflictsInCall, null, null));
                    refused++;
                }
                continue;
            }
            seen[entry.Key] = (entry.Category, entry.Intended);
            var standing = Read(lookup, entry.Key);
            if (standing is not null && standing.Category == entry.Category && SameIntended(standing.Intended, entry.Intended))
            {
                var text = standing.Evidence;
                if (!text.Contains(entry.Evidence, StringComparison.Ordinal))
                {
                    text += " | " + entry.Evidence;
                    if (text.Length > MaxEvidenceLength) text = text[..(MaxEvidenceLength - 3)] + "...";
                }
                var added = text != standing.Evidence;
                var merged = added ? standing with { Evidence = text } : standing;
                rows.Add(new(entry, added ? BulkStatus.EvidenceAdded : BulkStatus.AlreadyRecorded, standing, merged));
                continue;
            }
            if (standing is not null && !replace)
            {
                rows.Add(new(entry, BulkStatus.Contradiction, standing, null));
                refused++;
                continue;
            }
            var created = new WordTypeDisposition(entry.Key, entry.Form, entry.Category, entry.Evidence, entry.Intended, by, now);
            rows.Add(new(entry, standing is null ? BulkStatus.Recorded : BulkStatus.Replaced, standing, created));
        }
        var commit = write && (refused == 0 || skipRefused);
        if (commit)
            foreach (var row in rows)
            {
                if (row.Status == BulkStatus.EvidenceAdded)
                    AppendEvidence(connection!, transaction!, row.Entry.Key, row.Result!.Evidence);
                else if (row.Status is BulkStatus.Recorded or BulkStatus.Replaced)
                {
                    if (row.Status == BulkStatus.Replaced) End(connection!, transaction!, row.Entry.Key, "replaced", now, by, null);
                    Insert(connection!, transaction!, row.Result!);
                }
            }
        if (commit) transaction!.Commit();
        else transaction?.Rollback();
        return new(rows, commit);
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

    /// <summary>The longest evidence a disposition holds; appended evidence is cut to it with a trailing ellipsis.</summary>
    public const int MaxEvidenceLength = 2000;

    private static bool SameIntended(string? stored, string? incoming) =>
        string.Equals(stored?.Normalize(NormalizationForm.FormD), incoming?.Normalize(NormalizationForm.FormD), StringComparison.Ordinal);

    private static SqliteCommand? Prepare(SqliteConnection? connection, SqliteTransaction? transaction, string sql, string parameter)
    {
        if (connection is null) return null;
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.Add(parameter, SqliteType.Text);
        return command;
    }

    private static WordTypeDisposition? Read(SqliteCommand? lookup, string key)
    {
        if (lookup is null) return null;
        lookup.Parameters[0].Value = key;
        using var reader = lookup.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static void AppendEvidence(SqliteConnection connection, SqliteTransaction transaction, string key, string evidence)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE WordTypeDispositions SET Evidence = $evidence WHERE FormKey = $key AND EndedUtc IS NULL;";
        command.Parameters.AddWithValue("$evidence", evidence);
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
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
