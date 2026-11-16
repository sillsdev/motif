using System.Diagnostics;
using SIL.LCModel;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Sources;
using SIL.Motif.Host.Store;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.QueryScale;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Sources;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker;

/// <summary>Measures the FieldWorks Texts index build on a copy of a large project named by MOTIF_CEB_PROJECT_DIR.</summary>
public sealed class FieldWorksTextsIndexScaleTests(ITestOutputHelper output)
{
    [SlowScaleFact]
    [Trait("Category", "SlowScale")]
    public void LargeProjectBuildReportsMemoryAndTimeForFullAndUnchangedBuilds()
    {
        var source = Environment.GetEnvironmentVariable("MOTIF_CEB_PROJECT_DIR");
        if (string.IsNullOrEmpty(source) || !Directory.Exists(source))
        {
            output.WriteLine("MOTIF_CEB_PROJECT_DIR is not set to a project folder; nothing measured.");
            return;
        }
        var root = Path.Combine(Path.GetTempPath(), "motif-fw-texts-scale-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "proj");
        try
        {
            CopyProject(source, project);
            var path = Directory.GetFiles(project, "*.fwdata").Single();
            using var _ = AmbientEnvironment.Push(new Dictionary<string, string?>
            {
                [ScriptureLocalStore.PathEnvironmentVariable] = Path.Combine(root, "scripture-local.json"),
            });
            using var cache = new FwDataProjectLoader().LoadScratchCache(path);
            using var database = MotifDatabase.OpenOwned(Path.Combine(root, "project.motif.db"),
                new ProjectLocator(path, Path.GetFileNameWithoutExtension(path)), MotifSchema.CurrentSchema, new Version(1, 0));
            var identity = ProgressSourceIdentity.Capture(database.Project.FullFwDataPath)!;
            var ws = cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id;
            foreach (var label in new[] { "full build", "second build, nothing changed" })
            {
                GC.Collect();
                var baseline = GC.GetTotalMemory(true);
                long peak = baseline;
                using var stop = new CancellationTokenSource();
                var sampler = Task.Run(() =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        peak = Math.Max(peak, GC.GetTotalMemory(false));
                        Thread.Sleep(10);
                    }
                });
                var read = 0;
                long live = 0;
                var reused = 0;
                FieldWorksTextsIndex.TextReused.Value = _ => reused++;
                var watch = new Stopwatch();
                FieldWorksTextsIndex.TextRead.Value = _ =>
                {
                    read++;
                    watch.Stop();
                    live = Math.Max(live, GC.GetTotalMemory(true));
                    watch.Start();
                };
                watch.Start();
                bool published;
                try { published = FieldWorksTextsIndex.Build(database, cache, ws, identity); }
                finally { FieldWorksTextsIndex.TextRead.Value = null; FieldWorksTextsIndex.TextReused.Value = null; }
                watch.Stop();
                stop.Cancel();
                sampler.Wait();
                var line = $"{label}: published={published}, {watch.Elapsed.TotalSeconds:F1} s, Texts read={read}, Texts copied={reused}, " +
                    $"live heap above baseline at the largest Text boundary={(live - baseline) / 1048576.0:F0} MB, sampled peak above baseline={(peak - baseline) / 1048576.0:F0} MB, baseline={baseline / 1048576.0:F0} MB, " +
                    $"rows={RowsDigest(database)}";
                output.WriteLine(line);
                var report = Environment.GetEnvironmentVariable("MOTIF_CEB_REPORT");
                if (!string.IsNullOrEmpty(report)) File.AppendAllText(report, line + "\n");
                Assert.True(published);
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string RowsDigest(MotifDatabase database)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT p.PassageId,p.TotalPlaces,p.EligiblePlaces,x.Ordinal,x.Anchor,x.Verse,x.Surface,x.WordformKey," +
            "COALESCE(x.ReadingKey,''),COALESCE(x.Signature,''),x.State,w.WritingSystem,w.Normalized,w.Display,COALESCE(w.FieldWorksId,'') " +
            "FROM SourcePassages p JOIN SourceWordPlaces x ON x.SourceId=p.SourceId AND x.PassageId=p.PassageId " +
            "JOIN SourceWordforms w ON w.SourceId=x.SourceId AND w.WordformKey=x.WordformKey ORDER BY p.PassageId,x.Ordinal;";
        using var reader = command.ExecuteReader();
        var rows = 0;
        while (reader.Read())
        {
            rows++;
            for (var i = 0; i < reader.FieldCount; i++)
                hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) + "\u001f"));
            hash.AppendData([10]);
        }
        return rows + " rows " + Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
    }

    private static void CopyProject(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*.fwdata")) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var folder in new[] { "WritingSystemStore", "SharedSettings" })
        {
            var from = Path.Combine(source, folder);
            if (!Directory.Exists(from)) continue;
            Directory.CreateDirectory(Path.Combine(destination, folder));
            foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(destination, folder, Path.GetFileName(file)));
        }
    }
}
