using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>A named Selection's saved inputs and timestamps.</summary>
public sealed record NamedSelectionRecord(
    string Name,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> AddedWords,
    string CreatedUtc,
    string UpdatedUtc);

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
            SELECT s.SelectionName, s.TextIdsJson, s.AddedWordsJson, s.CreatedUtc, s.UpdatedUtc
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
            SELECT SelectionName, TextIdsJson, AddedWordsJson, CreatedUtc, UpdatedUtc
            FROM NamedSelections WHERE SelectionName = $name;
            """;
        command.Parameters.AddWithValue("$name", name.Trim());
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>Saves or replaces one named Selection and makes it the project's default atomically.</summary>
    public NamedSelectionRecord SetDefault(string name, IReadOnlyList<Guid> textIds, IReadOnlyList<string> addedWords)
    {
        RequireName(name);
        ArgumentNullException.ThrowIfNull(textIds);
        ArgumentNullException.ThrowIfNull(addedWords);
        name = name.Trim();
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
                INSERT INTO NamedSelections (SelectionName, TextIdsJson, AddedWordsJson, CreatedUtc, UpdatedUtc)
                VALUES ($name, $texts, $words, $created, $updated)
                ON CONFLICT (SelectionName) DO UPDATE SET
                    TextIdsJson = excluded.TextIdsJson,
                    AddedWordsJson = excluded.AddedWordsJson,
                    UpdatedUtc = excluded.UpdatedUtc;
                """;
            upsert.Parameters.AddWithValue("$name", name);
            upsert.Parameters.AddWithValue("$texts", JsonSerializer.Serialize(canonicalTextIds));
            upsert.Parameters.AddWithValue("$words", JsonSerializer.Serialize(canonicalWords));
            upsert.Parameters.AddWithValue("$created", createdUtc);
            upsert.Parameters.AddWithValue("$updated", now);
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
        transaction.Commit();
        return new NamedSelectionRecord(name, canonicalTextIds, canonicalWords, createdUtc, now);
    }

    private static NamedSelectionRecord Read(SqliteDataReader reader)
    {
        try
        {
            return new NamedSelectionRecord(
                reader.GetString(0), JsonSerializer.Deserialize<Guid[]>(reader.GetString(1))!,
                JsonSerializer.Deserialize<string[]>(reader.GetString(2))!, reader.GetString(3), reader.GetString(4));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new InvalidDataException("A saved Selection has invalid stored inputs.", exception);
        }
    }

    private static void RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A Selection name is required.", nameof(name));
    }
}
