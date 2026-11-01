using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>A saved named Selection and its revision-checked inputs and parsing policy.</summary>
public sealed record NamedSelectionRecord(
    string Name,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> AddedWords,
    string CreatedUtc,
    string UpdatedUtc,
    SelectionParsingLimits Limits,
    long Revision)
{
    /// <summary>The word-list file the added words came from and each word's count; null for typed words.</summary>
    public SelectionWordList? WordList { get; init; }
}

/// <summary>Raised when a Selection write was based on a revision that is no longer current.</summary>
public sealed class SelectionRevisionConflictException(string message) : InvalidOperationException(message);

/// <summary>Stores named Selections and the one that is resolved by default for an Assessment.</summary>
public sealed class NamedSelectionRepository(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Reads the project's default Selection, or returns null when none has been saved.</summary>
    public NamedSelectionRecord? GetDefault()
    {
        using var connection = _database.OpenConnection();
        return GetDefault(connection, null);
    }

    /// <summary>Reads the Selection chosen for Parsimony's Text group, or null when none has been chosen.</summary>
    public NamedSelectionRecord? GetParsimony()
    {
        using var connection = _database.OpenConnection();
        return ReadPointer(connection, null, "ParsimonySelection");
    }

    /// <summary>Forgets the Parsimony Selection without touching the Selection it named.</summary>
    public void ClearParsimony()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ParsimonySelection WHERE Id = 1;";
        command.ExecuteNonQuery();
    }

    /// <summary>Makes an already saved Selection the Parsimony Selection; false when no such Selection exists.</summary>
    public bool PointParsimonyAt(string name)
    {
        RequireName(name);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        if (ReadRevision(connection, transaction, name.Trim()) is null) return false;
        SetPointer(connection, transaction, "ParsimonySelection", name.Trim());
        transaction.Commit();
        return true;
    }

    /// <summary>
    /// Saves Selection inputs and makes that Selection the Parsimony Selection atomically. The Default
    /// Selection pointer is left alone, so each keeps its own job.
    /// </summary>
    public NamedSelectionRecord SetParsimony(
        string name, IReadOnlyList<Guid> textIds, IReadOnlyList<string> addedWords,
        SelectionParsingLimits limits, string? expectedRevision) =>
        Save(name, textIds, addedWords, limits, expectedRevision, "ParsimonySelection", null);

    internal NamedSelectionRecord? GetDefault(SqliteConnection connection, SqliteTransaction? transaction) =>
        ReadPointer(connection, transaction, "DefaultSelection");

    private const string DefaultWordListSelect = """
        SELECT {0} FROM DefaultSelection d JOIN NamedSelections s ON s.SelectionName = d.SelectionName
        WHERE d.Id = 1 AND s.TextIdsJson = '[]' AND s.WordListJson IS NOT NULL{1};
        """;

    // The words as a save stores them: trimmed, in NFD, distinct and in ordinal order.
    private static string[] CanonicalWords(IEnumerable<string> words) =>
        SortedOrdinal(words.Select(word => word.Trim()).Where(word => word.Length > 0)
            .Select(word => word.Normalize(System.Text.NormalizationForm.FormD))
            .Distinct(StringComparer.Ordinal).ToArray());

    private static string[] SortedOrdinal(string[] words)
    {
        Array.Sort(words, StringComparer.Ordinal);
        return words;
    }

    /// <summary>
    /// The hash of the Default's saved word list, taken from the start of its stored text without parsing the list,
    /// when the Default has no Texts and its stored words are exactly <paramref name="openedWords"/>. The words are
    /// compared as stored text, so none is parsed, unless <paramref name="wordsAreTheDefaults"/> says the caller
    /// has already found them equal. It is <see langword="null"/> otherwise, or when its stored text is unreadable,
    /// so a Selection of other words is never given the list's counts.
    /// </summary>
    internal string? GetDefaultWordListSha256(SqliteConnection connection, SqliteTransaction? transaction,
        IEnumerable<string> openedWords, bool wordsAreTheDefaults = false)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(openedWords);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = string.Format(CultureInfo.InvariantCulture, DefaultWordListSelect,
            "substr(s.WordListJson, 1, 2048)", wordsAreTheDefaults ? "" : " AND s.AddedWordsJson = $words");
        if (!wordsAreTheDefaults)
            command.Parameters.AddWithValue("$words", JsonSerializer.Serialize(CanonicalWords(openedWords)));
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0)) return null;
        var match = WordListShaPattern.Match(reader.GetString(0));
        return match.Success ? match.Groups[1].Value : null;
    }

    private static readonly System.Text.RegularExpressions.Regex WordListShaPattern = new(
        "\"Sha256\"\\s*:\\s*\"(sha256:[0-9a-f]{64})\"", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The Default's saved word list, read alone: neither its added words nor its Texts are parsed. It is
    /// <see langword="null"/> when the Default has Texts or no list, and throws when the stored list is damaged.
    /// </summary>
    public SelectionWordList? GetDefaultWordList()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = string.Format(CultureInfo.InvariantCulture, DefaultWordListSelect, "s.WordListJson", "");
        using var reader = command.ExecuteReader();
        return reader.Read() && !reader.IsDBNull(0)
            ? JsonSerializer.Deserialize<SelectionWordList>(reader.GetString(0)) : null;
    }

    private static NamedSelectionRecord? ReadPointer(SqliteConnection connection, SqliteTransaction? transaction,
        string pointerTable)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT s.SelectionName, s.TextIdsJson, s.AddedWordsJson, s.CreatedUtc, s.UpdatedUtc,
                s.TimeLimitMode, s.ExplicitPerWordLimitMs, s.PerWordStepLimit, s.Revision, s.AssessBudgetSeconds,
                s.WordListJson
            FROM {pointerTable} d JOIN NamedSelections s ON s.SelectionName = d.SelectionName
            WHERE d.Id = 1;
            """;
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        RepositoryReadCounters.RecordDeserialized();
        return Read(reader);
    }

    /// <summary>Reads a named Selection, or returns null when its name is unknown.</summary>
    public NamedSelectionRecord? Get(string name)
    {
        RequireName(name);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SelectionName, TextIdsJson, AddedWordsJson, CreatedUtc, UpdatedUtc,
                TimeLimitMode, ExplicitPerWordLimitMs, PerWordStepLimit, Revision, AssessBudgetSeconds,
                WordListJson
            FROM NamedSelections WHERE SelectionName = $name;
            """;
        command.Parameters.AddWithValue("$name", name.Trim());
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>Saves Selection inputs and makes that Selection the project's default atomically.</summary>
    public NamedSelectionRecord SetDefault(
        string name, IReadOnlyList<Guid> textIds, IReadOnlyList<string> addedWords,
        SelectionParsingLimits limits, string? expectedRevision, SelectionWordList? wordList = null) =>
        Save(name, textIds, addedWords, limits, expectedRevision, "DefaultSelection", wordList);

    private NamedSelectionRecord Save(
        string name, IReadOnlyList<Guid> textIds, IReadOnlyList<string> addedWords,
        SelectionParsingLimits limits, string? expectedRevision, string pointerTable, SelectionWordList? wordList)
    {
        RequireName(name);
        ArgumentNullException.ThrowIfNull(textIds);
        ArgumentNullException.ThrowIfNull(addedWords);
        ValidateLimits(limits);
        name = name.Trim();
        var canonicalTextIds = textIds.Distinct().OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
        var canonicalWords = CanonicalWords(addedWords);
        var wordSet = canonicalWords.ToHashSet(StringComparer.Ordinal);
        var keptList = wordList is null ? null : wordList with
        {
            Counts = wordList.Counts.Where(entry => wordSet.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
        };
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        limits = limits with { AssessBudgetSeconds = limits.AssessBudgetSeconds ?? GetDefault(connection, transaction)?.Limits.AssessBudgetSeconds ?? 3600 };
        var existing = ReadRevision(connection, transaction, name);
        if (existing is null && expectedRevision is not null || existing is not null &&
            (!TryParseRevision(expectedRevision, out var expected) || expected != existing.Value.Revision))
            throw new SelectionRevisionConflictException("The Selection changed before these inputs could be saved.");

        var revision = existing is { } current ? checked(current.Revision + 1) : 1;
        if (keptList is null && existing is not null)
            keptList = StoredListForSameInputs(connection, transaction, name, canonicalTextIds, canonicalWords);
        if (existing is null)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO NamedSelections
                    (SelectionName, TextIdsJson, AddedWordsJson, CreatedUtc, UpdatedUtc,
                     TimeLimitMode, ExplicitPerWordLimitMs, PerWordStepLimit, Revision, AssessBudgetSeconds, WordListJson)
                VALUES ($name, $texts, $words, $created, $updated, $timeMode, $timeLimit, $stepLimit, $revision, $budget,
                    $wordList);
                """;
            AddInputParameters(insert, name, canonicalTextIds, canonicalWords, limits, now, revision);
            insert.Parameters.AddWithValue("$wordList", WordListColumn(keptList));
            insert.Parameters.AddWithValue("$created", now);
            insert.ExecuteNonQuery();
        }
        else
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE NamedSelections SET TextIdsJson = $texts, AddedWordsJson = $words,
                    UpdatedUtc = $updated, TimeLimitMode = $timeMode,
                    ExplicitPerWordLimitMs = $timeLimit, PerWordStepLimit = $stepLimit, Revision = $revision, AssessBudgetSeconds = $budget,
                    WordListJson = $wordList
                WHERE SelectionName = $name AND Revision = $expected;
                """;
            AddInputParameters(update, name, canonicalTextIds, canonicalWords, limits, now, revision);
            update.Parameters.AddWithValue("$wordList", WordListColumn(keptList));
            update.Parameters.AddWithValue("$expected", existing.Value.Revision);
            if (update.ExecuteNonQuery() != 1)
                throw new SelectionRevisionConflictException("The Selection changed before these inputs could be saved.");
        }

        SetPointer(connection, transaction, pointerTable, name);
        transaction.Commit();
        return new NamedSelectionRecord(name, canonicalTextIds, canonicalWords,
            existing?.CreatedUtc ?? now, now, limits, revision) { WordList = keptList };
    }

    /// <summary>Changes only the current Default Selection's limits when its revision still matches.</summary>
    public NamedSelectionRecord SetLimits(string name, string expectedRevision, SelectionParsingLimits limits)
    {
        RequireName(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        ValidateLimits(limits);
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        if (!TryParseRevision(expectedRevision, out var expected))
            throw new SelectionRevisionConflictException("The Selection changed before its limits could be saved.");

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        var current = ReadDefault(connection, transaction);
        if (current is null || !StringComparer.Ordinal.Equals(current.Name, name.Trim()) || current.Revision != expected)
            throw new SelectionRevisionConflictException("The Selection changed before its limits could be saved.");

        limits = limits with { AssessBudgetSeconds = limits.AssessBudgetSeconds ?? current.Limits.AssessBudgetSeconds ?? 3600 };
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE NamedSelections SET UpdatedUtc = $updated, TimeLimitMode = $timeMode,
                ExplicitPerWordLimitMs = $timeLimit, PerWordStepLimit = $stepLimit, Revision = $revision, AssessBudgetSeconds = $budget
            WHERE SelectionName = $name AND Revision = $expected;
            """;
        update.Parameters.AddWithValue("$updated", now);
        AddLimitParameters(update, limits);
        update.Parameters.AddWithValue("$revision", checked(current.Revision + 1));
        update.Parameters.AddWithValue("$name", current.Name);
        update.Parameters.AddWithValue("$expected", expected);
        if (update.ExecuteNonQuery() != 1)
            throw new SelectionRevisionConflictException("The Selection changed before its limits could be saved.");

        transaction.Commit();
        return current with { UpdatedUtc = now, Limits = limits, Revision = checked(current.Revision + 1) };
    }

    private static NamedSelectionRecord? ReadDefault(SqliteConnection connection, SqliteTransaction transaction) =>
        ReadPointer(connection, transaction, "DefaultSelection");

    // A save naming no word list keeps the stored one while the Texts and words are the same; a change ends it.
    private static SelectionWordList? StoredListForSameInputs(SqliteConnection connection, SqliteTransaction transaction,
        string name, IReadOnlyList<Guid> textIds, IReadOnlyList<string> words)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT TextIdsJson, AddedWordsJson, WordListJson FROM NamedSelections WHERE SelectionName = $name;";
        command.Parameters.AddWithValue("$name", name);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(2)) return null;
        try
        {
            var storedTexts = JsonSerializer.Deserialize<Guid[]>(reader.GetString(0))!;
            var storedWords = JsonSerializer.Deserialize<string[]>(reader.GetString(1))!;
            return storedTexts.SequenceEqual(textIds) && storedWords.SequenceEqual(words, StringComparer.Ordinal)
                ? JsonSerializer.Deserialize<SelectionWordList>(reader.GetString(2)) : null;
        }
        catch (JsonException) { return null; }
    }

    private static (long Revision, string CreatedUtc)? ReadRevision(
        SqliteConnection connection, SqliteTransaction transaction, string name)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Revision, CreatedUtc FROM NamedSelections WHERE SelectionName = $name;";
        command.Parameters.AddWithValue("$name", name);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt64(0), reader.GetString(1)) : null;
    }

    private static void SetPointer(SqliteConnection connection, SqliteTransaction transaction, string pointerTable,
        string name)
    {
        using (var set = connection.CreateCommand())
        {
            set.Transaction = transaction;
            set.CommandText = $"""
                INSERT INTO {pointerTable} (Id, SelectionName) VALUES (1, $name)
                ON CONFLICT (Id) DO UPDATE SET SelectionName = excluded.SelectionName;
                """;
            set.Parameters.AddWithValue("$name", name);
            set.ExecuteNonQuery();
        }

        // Choosing Texts for Parsimony is not first-time setup, so only the Default pointer ends a skipped setup.
        if (pointerTable != "DefaultSelection") return;
        using var clearSkipped = connection.CreateCommand();
        clearSkipped.Transaction = transaction;
        clearSkipped.CommandText = "UPDATE MotifMetadata SET SetupSkippedUtc = NULL WHERE Id = 1;";
        clearSkipped.ExecuteNonQuery();
    }

    private static void AddInputParameters(SqliteCommand command, string name, IReadOnlyList<Guid> textIds,
        IReadOnlyList<string> words, SelectionParsingLimits limits, string now, long revision)
    {
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$texts", JsonSerializer.Serialize(textIds));
        command.Parameters.AddWithValue("$words", JsonSerializer.Serialize(words));
        command.Parameters.AddWithValue("$updated", now);
        AddLimitParameters(command, limits);
        command.Parameters.AddWithValue("$revision", revision);
    }

    private static void AddLimitParameters(SqliteCommand command, SelectionParsingLimits limits)
    {
        command.Parameters.AddWithValue("$budget", limits.AssessBudgetSeconds ?? 3600);
        command.Parameters.AddWithValue("$timeMode", limits.TimeMode.ToString());
        command.Parameters.AddWithValue("$timeLimit",
            limits.ExplicitPerWordLimitMs is { } timeLimit ? timeLimit : DBNull.Value);
        command.Parameters.AddWithValue("$stepLimit",
            limits.PerWordStepLimit.Steps is { } steps ? steps : DBNull.Value);
    }

    private static NamedSelectionRecord Read(SqliteDataReader reader)
    {
        try
        {
            var mode = Enum.Parse<SelectionTimeLimitMode>(reader.GetString(5), ignoreCase: false);
            var limits = new SelectionParsingLimits(
                reader.IsDBNull(7) ? StepCap.Unbounded : new StepCap(reader.GetInt64(7)),
                mode,
                reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.GetInt32(9));
            var invalid = limits.ValidationError();
            if (invalid is not null) throw new InvalidDataException(invalid);
            return new NamedSelectionRecord(
                reader.GetString(0), JsonSerializer.Deserialize<Guid[]>(reader.GetString(1))!,
                JsonSerializer.Deserialize<string[]>(reader.GetString(2))!, reader.GetString(3), reader.GetString(4),
                limits, reader.GetInt64(8))
            {
                WordList = reader.IsDBNull(10) ? null : JsonSerializer.Deserialize<SelectionWordList>(reader.GetString(10)),
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                                           ArgumentOutOfRangeException or ArgumentException)
        {
            throw new InvalidDataException("A saved Selection has invalid stored inputs.", exception);
        }
    }

    private static object WordListColumn(SelectionWordList? wordList) =>
        wordList is null ? DBNull.Value : JsonSerializer.Serialize(wordList);

    private static void ValidateLimits(SelectionParsingLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.ValidationError() is { } error) throw new ArgumentException(error, nameof(limits));
    }

    private static bool TryParseRevision(string? revision, out long value) =>
        long.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;

    private static void RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A Selection name is required.", nameof(name));
    }
}
