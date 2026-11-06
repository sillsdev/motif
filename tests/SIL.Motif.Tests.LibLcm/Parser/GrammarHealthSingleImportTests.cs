using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parser;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class GrammarHealthSingleImportTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task OneImportProducesIdenticalSnapshotFactsAndAnalysesToSeparateCommands()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");
        new FwDataProjectLoader().Save(cache);
        var root = Path.Combine(Path.GetTempPath(), "motif-one-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var invoker = new PanGlossInvoker();
            var oldSnapshot = Path.Combine(root, "separate.json");
            var newSnapshot = Path.Combine(root, "combined.json");
            var context = JsonSerializer.Serialize(new
            {
                format = "pangloss-facts-context", version = 1,
                baselineToken = new { bundleDigest = "sha256:" + new string('a', 64) },
                inputKind = "baseline", dryRunDigest = (string?)null,
            });
            var separateHealth = Assert.IsType<PanGlossOutcome.Completed>(await invoker.RunAsync(
                new PanGlossRequest.GrammarHealth(cache.ProjectId.Path, "Single import", StoredAnalysisComparison.Off),
                "separate-health", CancellationToken.None));
            Assert.IsType<PanGlossOutcome.Completed>(await invoker.RunAsync(
                new PanGlossRequest.Import(cache.ProjectId.Path, oldSnapshot), "separate-import", CancellationToken.None));
            var separate = Assert.IsType<PanGlossOutcome.Completed>(await invoker.RunAsync(
                new PanGlossRequest.Facts(oldSnapshot, context, Light: true), "separate-facts", CancellationToken.None));
            using var separateLease = separate.FactsArtifactLease;
            var combined = Assert.IsType<PanGlossOutcome.Completed>(await invoker.RunAsync(
                new PanGlossRequest.GrammarHealth(cache.ProjectId.Path, "Single import", StoredAnalysisComparison.Off)
                { SnapshotPath = newSnapshot, FactsContextJson = context, FactsLight = true },
                "combined-health", CancellationToken.None));
            using var combinedLease = combined.FactsArtifactLease;

            Assert.Equal(separateHealth.Output, combined.Output);
            Assert.Equal(File.ReadAllBytes(oldSnapshot), File.ReadAllBytes(newSnapshot));
            Assert.Equal(separate.Facts!.SourceSha256, combined.Facts!.SourceSha256);
            Assert.Equal(separate.Facts.ModelFingerprint, combined.Facts.ModelFingerprint);
            Assert.Equal(ReadEveryFact(separate.Facts.Path), ReadEveryFact(combined.Facts.Path));
            var snapshot = PanGlossImportedSnapshot.Capture(cache.ProjectId.Path, newSnapshot);
            var words = new[] { SeededProject.FirstForm, "mofita" };
            var oldBatch = Assert.IsType<PanGlossOutcome.Completed>(await invoker.RunAsync(
                new PanGlossRequest.Batch(cache.ProjectId.Path, words, null) { CollectAnalyses = true },
                "separate-batch", CancellationToken.None));
            var newBatch = Assert.IsType<PanGlossOutcome.Completed>(await invoker.RunAsync(
                new PanGlossRequest.Batch(cache.ProjectId.Path, words, null)
                { CollectAnalyses = true, ImportedSnapshot = snapshot, ArtifactDirectory = Path.Combine(root, "assessment") },
                "combined-batch", CancellationToken.None));
            using var batchLease = newBatch.ArtifactLease;
            var evidence = Assert.IsType<BatchInvocationEvidence>(newBatch.BatchEvidence);
            Assert.Equal(BatchInvocationEvidence.DigestFile(cache.ProjectId.Path), evidence.SourceBytesSha256);
            Assert.Equal(File.ReadAllBytes(cache.ProjectId.Path), File.ReadAllBytes(evidence.SourcePath));
            Assert.Equal(File.ReadAllBytes(newSnapshot),
                File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(evidence.SourcePath)!, "grammar-snapshot.json")));
            Assert.Equal(Results(oldBatch), Results(newBatch));
            Assert.Equal(Morphologies(oldBatch), Morphologies(newBatch));
            Assert.Contains(Results(newBatch), row => row.Contains(SeededProject.FirstForm, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string[] Morphologies(PanGlossOutcome.Completed outcome) =>
        outcome.MorphologyOutput!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            var word = JsonNode.Parse(line)!.AsObject();
            word.Remove("elapsedMs");
            return word.ToJsonString();
        }).ToArray();

    private static string[] Results(PanGlossOutcome.Completed outcome) =>
        BatchTsvParser.Parse(outcome.Output).Select(row => JsonSerializer.Serialize(new
        { row.Word, row.Outcome, row.Signature })).ToArray();

    private static string[] ReadEveryFact(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var tables = connection.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        var names = new List<string>();
        using (var reader = tables.ExecuteReader())
            while (reader.Read()) names.Add(reader.GetString(0));
        var rows = new List<string>();
        foreach (var name in names)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM \"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(name + ":" + JsonSerializer.Serialize(values.Select(value => value is DBNull ? null : value)));
            }
        }
        return rows.Order(StringComparer.Ordinal).ToArray();
    }
}

[Trait("MotifTestLevel", "Unit")]
public sealed class PanGlossImportedSnapshotTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ABatchRefusesAnImportedSnapshotWhenEitherBoundFileChanges(bool changeProject)
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-import-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "source.fwdata");
            var snapshotPath = Path.Combine(root, "snapshot.json");
            File.WriteAllBytes(project, Guid.NewGuid().ToByteArray());
            File.WriteAllBytes(snapshotPath, Guid.NewGuid().ToByteArray());
            var snapshot = PanGlossImportedSnapshot.Capture(project, snapshotPath);
            var request = new PanGlossRequest.Batch(project, [], null) { ImportedSnapshot = snapshot };
            Assert.True(snapshot.Matches(project));

            File.WriteAllBytes(changeProject ? project : snapshotPath, Guid.NewGuid().ToByteArray());

            Assert.False(snapshot.Matches(project));
            using var invoker = new PanGlossInvoker(executablePath: null);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                invoker.RunAsync(request, "changed-import", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

}
