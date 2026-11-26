using System.Globalization;
using System.Text.RegularExpressions;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>
/// The highest Grammar Guide number this project has given out, kept in Motif's store so a deleted item's number is never
/// reused even when the list's own Description line is edited or cleared. The table is made on first use, so a store
/// from an older schema needs no upgrade.
/// </summary>
public sealed partial class GuideNumberMarkRepository(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    [GeneratedRegex(@"id\\?""\s*:\s*\\?""GDR-(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DraftNumber();

    /// <summary>The larger of the stored mark and the highest number any open Draft already adds, or 0.</summary>
    public long Floor()
    {
        using var connection = _database.OpenConnection();
        Ensure(connection);
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT Mark FROM GuideNumberMark WHERE Id = 1;";
        var largest = read.ExecuteScalar() is { } mark and not DBNull ? Convert.ToInt64(mark, CultureInfo.InvariantCulture) : 0;
        using var drafts = connection.CreateCommand();
        drafts.CommandText = "SELECT DraftJson FROM Proposals WHERE DraftJson IS NOT NULL AND DraftJson LIKE '%GDR-%';";
        using var reader = drafts.ExecuteReader();
        while (reader.Read())
            foreach (Match match in DraftNumber().Matches(reader.GetString(0)))
                if (long.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var value) && value > largest) largest = value;
        return largest;
    }

    /// <summary>Raises the stored mark to <paramref name="number"/>; it never goes down.</summary>
    public void Raise(long number)
    {
        using var connection = _database.OpenConnection();
        Ensure(connection);
        using var write = connection.CreateCommand();
        write.CommandText = """
            INSERT INTO GuideNumberMark (Id, Mark) VALUES (1, $mark)
            ON CONFLICT(Id) DO UPDATE SET Mark = MAX(Mark, excluded.Mark);
            """;
        write.Parameters.AddWithValue("$mark", number);
        write.ExecuteNonQuery();
    }

    private static void Ensure(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        using var create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE IF NOT EXISTS GuideNumberMark (Id INTEGER PRIMARY KEY CHECK (Id = 1), Mark INTEGER NOT NULL);";
        create.ExecuteNonQuery();
    }
}
