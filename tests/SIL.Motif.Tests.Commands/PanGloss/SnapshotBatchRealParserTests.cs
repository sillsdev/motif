using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

[Collection(LcmCacheParallelCollections.Group1)]
[Trait("MotifTestLevel", "System")]
public sealed class SnapshotBatchRealParserTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task RetainedSnapshotParsesTheSameWordsAndProducesTheSameFactsAsTheImportedProject()
    {
        string project;
        using (var cache = pristine.NewScratch())
        {
            project = cache.ProjectId.Path;
            RealParserProject.PrepareForParsing(cache,
                (SeededProject.FirstForm + SeededProject.SecondForm).Select(letter => letter.ToString()).ToArray());
        }
        var root = Path.Combine(Path.GetDirectoryName(project)!, "snapshot-batch");
        Directory.CreateDirectory(root);
        var snapshot = Path.Combine(root, "grammar.json");
        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(Wait.Scaled(TimeSpan.FromMinutes(10)));
        async Task<PanGlossOutcome.Completed> Run(PanGlossRequest request)
        {
            var outcome = await invoker.RunAsync(request, "test:snapshot-batch", cancellation.Token,
                wallClockCap: Wait.Scaled(TimeSpan.FromMinutes(10)));
            Assert.True(outcome is PanGlossOutcome.Completed, outcome.ToString());
            return (PanGlossOutcome.Completed)outcome;
        }

        await Run(new PanGlossRequest.Import(project, snapshot));
        string[] words = [SeededProject.FirstForm, SeededProject.SecondForm, "zzzz"];
        var sourceBatch = await Run(new PanGlossRequest.Batch(project, words, null,
            StatsCachePath: Path.Combine(root, "source-stats.sqlite"))
            { CollectAnalyses = true, ThreadCount = 1 });
        var artifacts = Path.Combine(root, "batch");
        var statistics = Path.Combine(artifacts, "stats.sqlite");
        var manifest = Path.Combine(artifacts, "stats-manifest.json");
        var snapshotBatch = await Run(new PanGlossRequest.Batch(snapshot, words, null,
            StatsCachePath: statistics,
            ArtifactDirectory: artifacts)
            { CollectAnalyses = true, ThreadCount = 1, StatsManifestPath = manifest });
        using var batchLease = snapshotBatch.ArtifactLease;
        var evidence = Assert.IsType<BatchInvocationEvidence>(snapshotBatch.BatchEvidence);

        Assert.Equal(".json", Path.GetExtension(evidence.SourcePath));
        Assert.Equal(File.ReadAllBytes(snapshot), File.ReadAllBytes(evidence.SourcePath));
        Assert.Equal(BatchInvocationEvidence.DigestFile(snapshot), evidence.SourceBytesSha256);
        Assert.Equal(ParseResults(sourceBatch.Output), ParseResults(snapshotBatch.Output));
        Assert.Equal(AnalysisResults(sourceBatch.MorphologyOutput!), AnalysisResults(snapshotBatch.MorphologyOutput!));
        Assert.Contains(pristine.Seed.FirstLexemeFormId.ToString("D"), snapshotBatch.MorphologyOutput!);
        Assert.Contains(pristine.Seed.SecondLexemeFormId.ToString("D"), snapshotBatch.MorphologyOutput!);
        Assert.True(File.Exists(manifest));

        var context = JsonSerializer.Serialize(new
        {
            format = "pangloss-facts-context", version = 1, baselineToken = new { token = "snapshot-batch" },
            inputKind = "baseline", dryRunDigest = (string?)null,
        });
        foreach (var includeStatistics in new[] { false, true })
        {
            var sourceFacts = await Run(new PanGlossRequest.Facts(snapshot, context,
                StatsCachePath: includeStatistics ? statistics : null,
                StatsManifestPath: includeStatistics ? manifest : null, Light: true));
            using var sourceLease = sourceFacts.FactsArtifactLease;
            var snapshotFacts = await Run(new PanGlossRequest.Facts(evidence.SourcePath, context,
                StatsCachePath: includeStatistics ? statistics : null,
                StatsManifestPath: includeStatistics ? manifest : null, Light: true));
            using var snapshotLease = snapshotFacts.FactsArtifactLease;
            Assert.Equal(sourceFacts.Facts!.ModelFingerprint, snapshotFacts.Facts!.ModelFingerprint);
            var rows = ReadFacts(snapshotFacts.Facts.Path);
            Assert.Equal(ReadFacts(sourceFacts.Facts.Path), rows);
            if (includeStatistics) Assert.Contains(rows, row => row.StartsWith("stats_word:", StringComparison.Ordinal));
        }
    }

    private static string[] ParseResults(string tsv) => tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split('\t'))
        .Select(columns => string.Join('\t', columns.Where((_, index) => index != 2))).ToArray();

    private static string[] AnalysisResults(string jsonl) => jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line =>
        {
            var row = JsonNode.Parse(line)!.AsObject();
            row.Remove("elapsedMs");
            return row.ToJsonString();
        }).ToArray();

    private static string[] ReadFacts(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var tables = connection.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        using var names = tables.ExecuteReader();
        var rows = new List<string>();
        while (names.Read())
        {
            var table = names.GetString(0);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\"";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(table + ":" + JsonSerializer.Serialize(values));
            }
        }
        return rows.Order(StringComparer.Ordinal).ToArray();
    }
}
