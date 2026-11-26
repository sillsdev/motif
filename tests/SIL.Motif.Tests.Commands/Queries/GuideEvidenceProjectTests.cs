using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Contract.Query;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A block whose line names a <c>--project</c> opens it read-only: the project folder is byte-for-byte as it was afterwards.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class GuideEvidenceProjectTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _cache = Directory.CreateTempSubdirectory("guide-evidence-cache").FullName;

    public void Dispose() => Directory.Delete(_cache, true);

    private sealed class Recorded(string recording) : IRealizesIndexBuilder
    {
        public string Identity { get; } = "evidence-project".PadRight(16, '0');

        public string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken)
        {
            var path = Path.Combine(Directory.CreateTempSubdirectory("recorded-evidence-project").FullName, "analyses.jsonl");
            File.WriteAllText(path, recording);
            return path;
        }
    }

    private static string Line(string word, string msa, string text) => new JsonObject
    {
        ["schema"] = "fieldworks-parse-analysis/v1", ["index"] = 0, ["word"] = word, ["elapsedMs"] = 0, ["capped"] = false, ["timedOut"] = false,
        ["invalidShape"] = false,
        ["analyses"] = new JsonArray(new JsonObject
        {
            ["morphs"] = new JsonArray(new JsonObject { ["form"] = "x", ["msa"] = msa, ["inflType"] = null, ["guessedString"] = null }),
            ["morphSpans"] = new JsonObject
            {
                ["morphemes"] = new JsonArray(new JsonObject
                {
                    ["item"] = 0, ["reason"] = null, ["rule"] = null,
                    ["spans"] = new JsonArray(new JsonObject { ["offsets"] = new JsonArray(0, text.Length), ["segments"] = new JsonArray(0, text.Length), ["text"] = text }),
                }),
                ["segmentCount"] = text.Length + 1, ["surface"] = word, ["unattributed"] = new JsonArray(),
            },
        }),
        ["unavailable"] = new JsonArray(),
    }.ToJsonString();

    [Fact]
    public void AProjectLineLeavesTheProjectFolderUntouchedAndReleasesTheProject()
    {
        var project = RealizesCorpusFixture.Create(pristine);
        var folder = Path.GetDirectoryName(project)!;
        string entry, msa;
        using (var read = BaselineReadCache.Open(project))
        {
            var found = read.Cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().First(item => item.HeadWord?.Text == "kaon");
            entry = found.Guid.ToString("D");
            msa = found.MorphoSyntaxAnalysesOC.Single().Guid.ToString("D");
        }
        File.WriteAllText(Path.Combine(folder, "words.tsv"), "kaon\t5\n");
        File.WriteAllText(Path.Combine(folder, "g.json"), "g");
        var line = $"motif query corpus forms --corpus words.tsv --grammar g.json --project {Path.GetFileName(project)} forms where 'only-via(entry:{entry})'";
        var builder = new Recorded(Line("kaon", msa, "kaon") + "\n");
        Assert.True(QueryRunLine.TryParseRun(line, out var parsed, out _));
        Assert.True(CorpusQueryCommand.Forms(SIL.Motif.Contract.Query.Corpus.CorpusRunLine.Resolve(parsed.Corpus!, folder), null, default, builder, _cache).Succeeded);

        var before = Snapshot(folder);
        var result = GuideEvidenceRunner.Evaluate(line, folder, null, default, builder, _cache);
        Assert.Equal(GuideEvidenceStatus.Ready, result.Status);
        Assert.Contains("project", result.Phases.Keys);
        Assert.Equal(before, Snapshot(folder));
        using var reopened = File.Open(project, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static string Snapshot(string folder) => string.Join("\n", Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => path + "|" + (File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks + "|" + new FileInfo(path).Length : "dir")));
}
