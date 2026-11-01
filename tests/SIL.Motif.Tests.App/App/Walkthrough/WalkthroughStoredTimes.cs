using System.Globalization;
using SIL.Motif.Commands;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>Gives the project's stored Assessments the fixed capture time the replay's clock reads.</summary>
internal static class WalkthroughStoredTimes
{
    /// <summary>
    /// The Assessor stamps each Assessment from the machine's clock, so a seeded project would show the minute it was
    /// seeded in the project chip and in the AI Handoff's stale-files check. The latest run of the Assessor takes
    /// <paramref name="latest"/> and each earlier one a second less, so their order stays what it was.
    /// </summary>
    internal static void Pin(WalkthroughProjectContext project, DateTimeOffset latest)
    {
        var locator = ProjectStoreCommand.Locate(project.FwDataPath);
        var path = ProjectDatabaseCatalog.DatabasePathFor(locator);
        if (!File.Exists(path)) return;
        using var database = MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0));
        using var connection = database.OpenConnection();
        // One Assess command stamps all its records with one time, so they keep sharing a fixed one.
        var stamps = new List<string>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT DISTINCT SavedUtc FROM Assessments ORDER BY SavedUtc;";
            using var reader = read.ExecuteReader();
            while (reader.Read()) stamps.Add(reader.GetString(0));
        }
        for (var index = 0; index < stamps.Count; index++)
        {
            using var write = connection.CreateCommand();
            write.CommandText = "UPDATE Assessments SET SavedUtc = $fixed WHERE SavedUtc = $stamp;";
            write.Parameters.AddWithValue("$fixed",
                latest.AddSeconds(index - (stamps.Count - 1)).ToString("O", CultureInfo.InvariantCulture));
            write.Parameters.AddWithValue("$stamp", stamps[index]);
            write.ExecuteNonQuery();
        }
    }
}
