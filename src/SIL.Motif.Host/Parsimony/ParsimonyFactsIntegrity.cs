using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.Parsimony;

/// <summary>The SQLite checks a grammar-facts file must pass before it is published as a Parsimony bundle.</summary>
public static class ParsimonyFactsIntegrity
{
    /// <summary>
    /// Runs SQLite integrity and foreign-key checks over the whole file, which is too costly to repeat on every open.
    /// Throws <see cref="InvalidDataException"/> when either check fails.
    /// </summary>
    /// <param name="factsPath">The closed grammar-facts file to check.</param>
    public static void Verify(string factsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factsPath);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ParsimonySqlitePath.DataSource(factsPath),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            if (!StringComparer.Ordinal.Equals(integrity.ExecuteScalar() as string, "ok"))
                throw new InvalidDataException("The grammar-facts artifact failed SQLite integrity validation.");
        }
        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_key_check;";
        using var violations = foreignKeys.ExecuteReader();
        if (violations.Read())
            throw new InvalidDataException("The grammar-facts artifact has a foreign-key violation.");
    }
}
