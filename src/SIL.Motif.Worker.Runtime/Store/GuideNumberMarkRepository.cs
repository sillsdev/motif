using System.Globalization;
using System.Text.RegularExpressions;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>
/// The highest Grammar Guide number this project has given out, kept in Motif's store so a deleted item's number is never
/// reused even when the list's own Description line is edited or cleared. It is a one-line file beside the store, not a
/// table, so no schema change is needed.
/// </summary>
public sealed partial class GuideNumberMarkRepository(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    [GeneratedRegex(@"id\\?""\s*:\s*\\?""GDR-(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DraftNumber();

    /// <summary>The file beside the store that holds the mark.</summary>
    public string MarkPath => _database.FullPath + ".guide-number";

    /// <summary>The larger of the stored mark and the highest number any open Draft already adds, or 0.</summary>
    public long Floor()
    {
        var largest = 0L;
        if (File.Exists(MarkPath) && long.TryParse(File.ReadAllText(MarkPath).Trim(), CultureInfo.InvariantCulture, out var mark)) largest = mark;
        using var connection = _database.OpenConnection();
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
        var current = 0L;
        if (File.Exists(MarkPath)) _ = long.TryParse(File.ReadAllText(MarkPath).Trim(), CultureInfo.InvariantCulture, out current);
        if (number <= current) return;
        var temporary = MarkPath + ".tmp";
        File.WriteAllText(temporary, number.ToString(CultureInfo.InvariantCulture));
        File.Move(temporary, MarkPath, overwrite: true);
    }
}
