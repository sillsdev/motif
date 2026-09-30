using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>A named Selection's saved inputs and timestamps.</summary>
public sealed record NamedSelectionRecord(
    string Name,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> AddedWords,
    string CreatedUtc,
    string UpdatedUtc,
    int? PerWordLimitMs,
    StepCap PerWordStepLimit);

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
                s.PerWordLimitMs, s.PerWordStepLimit
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
                PerWordLimitMs, PerWordStepLimit
            FROM NamedSelections WHERE SelectionName = $name;
            """;
        command.Parameters.AddWithValue("$name", name.Trim());
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>Saves or replaces one named Selection and makes it the project's default atomically.</summary>
    public NamedSelectionRecord SetDefault(
        string name, IReadOnlyList<Guid> textIds, IReadOnlyList<string> addedWords,
        int? perWordLimitMs = 1000, StepCap? perWordStepLimit = null)
    {
        RequireName(name);
        ArgumentNullException.ThrowIfNull(textIds);
        ArgumentNullException.ThrowIfNull(addedWords);
        if (perWordLimitMs is <= 0) throw new ArgumentOutOfRangeException(nameof(perWordLimitMs));
        name = name.Trim();
        var stepLimit = perWordStepLimit ?? StepCap.Default;
        var canonicalTextIds = textIds.Distinct().OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
        var canonicalWords = addedWords.Select(word => word.Trim()).Where(word => word.Length > 0)
            .Select(word => word.Normalize(System.Text.NormalizationForm.FormD))
            .Distinct(StringComparer.Ordinal).OrderBy(word => word, StringComparer.Ordinal).ToArray();
        var now = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        string createdUtc;
        using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = "SELECT CreatedUtc FROM NamedSelections WHERE SelectionName = $name;";
            find.Parameters.AddWithValue("$name", name);
            createdUtc = find.ExecuteScalar() as string ?? now;
        }
        using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO NamedSelections
                    (SelectionName, TextIdsJson, AddedWordsJson, CreatedUtc, UpdatedUtc, PerWordLimitMs, PerWordStepLimit)
                VALUES ($name, $texts, $words, $created, $updated, $timeLimit, $stepLimit)
                ON CONFLICT (SelectionName) DO UPDATE SET
                    TextIdsJson = excluded.TextIdsJson,
                    AddedWordsJson = excluded.AddedWordsJson,
                    UpdatedUtc = excluded.UpdatedUtc,
                    PerWordLimitMs = excluded.PerWordLimitMs,
                    PerWordStepLimit = excluded.PerWordStepLimit;
                """;
            upsert.Parameters.AddWithValue("$name", name);
            upsert.Parameters.AddWithValue("$texts", JsonSerializer.Serialize(canonicalTextIds));
            upsert.Parameters.AddWithValue("$words", JsonSerializer.Serialize(canonicalWords));
            upsert.Parameters.AddWithValue("$created", createdUtc);
            upsert.Parameters.AddWithValue("$updated", now);
            upsert.Parameters.AddWithValue("$timeLimit", perWordLimitMs is { } timeLimit ? timeLimit : DBNull.Value);
            upsert.Parameters.AddWithValue("$stepLimit", stepLimit.Steps is { } steps ? steps : DBNull.Value);
            upsert.ExecuteNonQuery();
        }
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
        using (var clearSkipped = connection.CreateCommand())
        {
            clearSkipped.Transaction = transaction;
            clearSkipped.CommandText = "UPDATE MotifMetadata SET SetupSkippedUtc = NULL WHERE Id = 1;";
            clearSkipped.ExecuteNonQuery();
        }
        transaction.Commit();
        return new NamedSelectionRecord(name, canonicalTextIds, canonicalWords, createdUtc, now,
            perWordLimitMs, stepLimit);
    }

    private static NamedSelectionRecord Read(SqliteDataReader reader)
    {
        try
        {
            return new NamedSelectionRecord(
                reader.GetString(0), JsonSerializer.Deserialize<Guid[]>(reader.GetString(1))!,
                JsonSerializer.Deserialize<string[]>(reader.GetString(2))!, reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? StepCap.Unbounded : new StepCap(reader.GetInt64(6)));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("A saved Selection has invalid stored inputs.", exception);
        }
    }

    private static void RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A Selection name is required.", nameof(name));
    }
}
