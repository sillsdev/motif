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
    long Revision);

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
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.SelectionName, s.TextIdsJson, s.AddedWordsJson, s.CreatedUtc, s.UpdatedUtc,
                s.TimeLimitMode, s.ExplicitPerWordLimitMs, s.PerWordStepLimit, s.Revision
            FROM DefaultSelection d JOIN NamedSelections s ON s.SelectionName = d.SelectionName
            WHERE d.Id = 1;
            """;
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>Reads a named Selection, or returns null when its name is unknown.</summary>
    public NamedSelectionRecord? Get(string name)
    {
        RequireName(name);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SelectionName, TextIdsJson, AddedWordsJson, CreatedUtc, UpdatedUtc,
                TimeLimitMode, ExplicitPerWordLimitMs, PerWordStepLimit, Revision
            FROM NamedSelections WHERE SelectionName = $name;
            """;
        command.Parameters.AddWithValue("$name", name.Trim());
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>Saves Selection inputs and makes that Selection the project's default atomically.</summary>
    public NamedSelectionRecord SetDefault(
        string name, IReadOnlyList<Guid> textIds, IReadOnlyList<string> addedWords,
        SelectionParsingLimits limits, string? expectedRevision)
    {
        RequireName(name);
        ArgumentNullException.ThrowIfNull(textIds);
        ArgumentNullException.ThrowIfNull(addedWords);
        ValidateLimits(limits);
        name = name.Trim();
        var canonicalTextIds = textIds.Distinct().OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
        var canonicalWords = addedWords.Select(word => word.Trim()).Where(word => word.Length > 0)
            .Select(word => word.Normalize(System.Text.NormalizationForm.FormD))
            .Distinct(StringComparer.Ordinal).OrderBy(word => word, StringComparer.Ordinal).ToArray();
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        var existing = ReadRevision(connection, transaction, name);
        if (existing is null && expectedRevision is not null || existing is not null &&
            (!TryParseRevision(expectedRevision, out var expected) || expected != existing.Value.Revision))
            throw new SelectionRevisionConflictException("The Selection changed before these inputs could be saved.");

        var revision = existing is { } current ? checked(current.Revision + 1) : 1;
        if (existing is null)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO NamedSelections
                    (SelectionName, TextIdsJson, AddedWordsJson, CreatedUtc, UpdatedUtc,
                     TimeLimitMode, ExplicitPerWordLimitMs, PerWordStepLimit, Revision)
                VALUES ($name, $texts, $words, $created, $updated, $timeMode, $timeLimit, $stepLimit, $revision);
                """;
            AddInputParameters(insert, name, canonicalTextIds, canonicalWords, limits, now, revision);
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
                    ExplicitPerWordLimitMs = $timeLimit, PerWordStepLimit = $stepLimit, Revision = $revision
                WHERE SelectionName = $name AND Revision = $expected;
                """;
            AddInputParameters(update, name, canonicalTextIds, canonicalWords, limits, now, revision);
            update.Parameters.AddWithValue("$expected", existing.Value.Revision);
            if (update.ExecuteNonQuery() != 1)
                throw new SelectionRevisionConflictException("The Selection changed before these inputs could be saved.");
        }

        SetDefaultReference(connection, transaction, name);
        transaction.Commit();
        return new NamedSelectionRecord(name, canonicalTextIds, canonicalWords,
            existing?.CreatedUtc ?? now, now, limits, revision);
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

        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE NamedSelections SET UpdatedUtc = $updated, TimeLimitMode = $timeMode,
                ExplicitPerWordLimitMs = $timeLimit, PerWordStepLimit = $stepLimit, Revision = $revision
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

    private static NamedSelectionRecord? ReadDefault(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT s.SelectionName, s.TextIdsJson, s.AddedWordsJson, s.CreatedUtc, s.UpdatedUtc,
                s.TimeLimitMode, s.ExplicitPerWordLimitMs, s.PerWordStepLimit, s.Revision
            FROM DefaultSelection d JOIN NamedSelections s ON s.SelectionName = d.SelectionName
            WHERE d.Id = 1;
            """;
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
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

    private static void SetDefaultReference(SqliteConnection connection, SqliteTransaction transaction, string name)
    {
        using (var set = connection.CreateCommand())
        {
            set.Transaction = transaction;
            set.CommandText = """
                INSERT INTO DefaultSelection (Id, SelectionName) VALUES (1, $name)
                ON CONFLICT (Id) DO UPDATE SET SelectionName = excluded.SelectionName;
                """;
            set.Parameters.AddWithValue("$name", name);
            set.ExecuteNonQuery();
        }

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
                reader.IsDBNull(6) ? null : reader.GetInt32(6));
            var invalid = limits.ValidationError();
            if (invalid is not null) throw new InvalidDataException(invalid);
            return new NamedSelectionRecord(
                reader.GetString(0), JsonSerializer.Deserialize<Guid[]>(reader.GetString(1))!,
                JsonSerializer.Deserialize<string[]>(reader.GetString(2))!, reader.GetString(3), reader.GetString(4),
                limits, reader.GetInt64(8));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                                           ArgumentOutOfRangeException or ArgumentException)
        {
            throw new InvalidDataException("A saved Selection has invalid stored inputs.", exception);
        }
    }

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
