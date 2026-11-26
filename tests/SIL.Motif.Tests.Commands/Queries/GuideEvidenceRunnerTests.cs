using System.Text.Json.Nodes;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Contract.Query;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// The evaluator behind a Guide block's v2 <c>run</c> line, over a recorded parse index: a warm run answers, a cold index says "not built"
/// and builds nothing, an incomplete parse says "at least", a refusal is a refusal, and the run writes nothing but the shared parse index.
/// </summary>
public sealed class GuideEvidenceRunnerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("guide-evidence").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private const string Root = "00000000-0000-0000-0000-0000000000a1";

    private static string Line(string word, bool capped, params string[] texts)
    {
        var analyses = new JsonArray();
        var morphs = new JsonArray();
        var spans = new JsonArray();
        var at = 0;
        for (var item = 0; item < texts.Length; item++)
        {
            morphs.Add(new JsonObject { ["form"] = "x", ["msa"] = Root, ["inflType"] = null, ["guessedString"] = null });
            var end = at + texts[item].Length;
            spans.Add(new JsonObject
            {
                ["item"] = item, ["reason"] = null, ["rule"] = null,
                ["spans"] = new JsonArray(new JsonObject { ["offsets"] = new JsonArray(at, end), ["segments"] = new JsonArray(at, end), ["text"] = texts[item] }),
            });
            at = end;
        }
        if (texts.Length > 0)
            analyses.Add(new JsonObject
            {
                ["morphs"] = morphs,
                ["morphSpans"] = new JsonObject { ["morphemes"] = spans, ["segmentCount"] = at + 1, ["surface"] = word, ["unattributed"] = new JsonArray() },
            });
        return new JsonObject
        {
            ["schema"] = "fieldworks-parse-analysis/v1", ["index"] = 0, ["word"] = word, ["elapsedMs"] = 0, ["capped"] = capped, ["timedOut"] = false,
            ["invalidShape"] = false, ["analyses"] = analyses, ["unavailable"] = new JsonArray(),
        }.ToJsonString();
    }

    private sealed class Recorded(string recording) : IRealizesIndexBuilder
    {
        public int Builds { get; private set; }

        public string Identity { get; } = "evidence-recorded".PadRight(16, '0');

        public string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken)
        {
            Builds++;
            var path = Path.Combine(Directory.CreateTempSubdirectory("recorded-evidence").FullName, "analyses.jsonl");
            File.WriteAllText(path, recording);
            return path;
        }
    }

    private static readonly string Complete = string.Join("\n", Line("solo", false, "so", "lo"), Line("pair", false, "pa", "ir"), Line("none", false)) + "\n";
    private static readonly string Capped = string.Join("\n", Line("solo", false, "so", "lo"), Line("pair", true, "pa", "ir"), Line("none", false)) + "\n";

    private const string RunLine = "motif query corpus forms --corpus words.tsv --grammar grammar.json forms where 'readings > 0'";

    private string CacheRoot => Path.Combine(_folder, "cache");

    private void Seed()
    {
        File.WriteAllText(Path.Combine(_folder, "words.tsv"), "solo\t10\npair\t5\nnone\t2\n");
        File.WriteAllText(Path.Combine(_folder, "grammar.json"), "grammar");
    }

    private GuideEvidenceResult Evaluate(string line, Recorded builder) =>
        GuideEvidenceRunner.Evaluate(line, _folder, null, default, builder, CacheRoot);

    // The CLI's own run, with a builder, is what warms the shared index a block then reads.
    private void Warm(string line, Recorded builder)
    {
        Assert.True(QueryRunLine.TryParseRun(line, out var parsed, out _));
        var request = SIL.Motif.Contract.Query.Corpus.CorpusRunLine.Resolve(parsed.Corpus!, _folder);
        Assert.True(CorpusQueryCommand.Forms(request, null, default, builder, CacheRoot).Succeeded);
    }

    [Fact]
    public void ARunOverAWarmIndexAnswersWithRowsTheTotalAndItsPhases()
    {
        Seed();
        var builder = new Recorded(Complete);
        Warm(RunLine, builder);
        var built = builder.Builds;
        var result = Evaluate(RunLine, builder);
        Assert.Equal(GuideEvidenceStatus.Ready, result.Status);
        Assert.Equal(built, builder.Builds);
        var table = result.Table!;
        Assert.Equal(["form", "tokens", "parse.state", "readings"], table.Columns);
        Assert.Equal(["solo", "pair"], table.Rows.Select(row => row[0]));
        Assert.Equal((2, 15L), (table.Types, table.Tokens));
        Assert.True(table.Complete);
        Assert.Equal("2 word types (15 tokens)", table.Headline);
        Assert.Contains("query", result.Phases.Keys);
        Assert.Contains("index-load", result.Phases.Keys);
        Assert.Null(result.BaselineId);
    }

    [Fact]
    public void AColdIndexSaysNotBuiltNamesTheCommandAndBuildsNothing()
    {
        Seed();
        var builder = new Recorded(Complete);
        var result = Evaluate(RunLine, builder);
        Assert.Equal(GuideEvidenceStatus.ColdIndex, result.Status);
        Assert.Equal(0, builder.Builds);
        Assert.Null(result.Table);
        Assert.StartsWith("parse index not built: run cd '", result.Message);
        Assert.EndsWith("'--corpus' 'words.tsv' '--grammar' 'grammar.json' 'forms' 'where' 'readings > 0'", result.BuildCommand);
        Assert.StartsWith("cd '" + _folder + "' && motif 'query'", result.BuildCommand);
        Assert.Equal(RefusalCodes.QueryParseIndexCold, result.RefusalCode);
        Assert.Equal("query.parse-index-cold", result.RefusalCode);
        Assert.False(Directory.Exists(CacheRoot) && Directory.EnumerateFileSystemEntries(CacheRoot).Any());
    }

    [Fact]
    public void AnIncompleteParseSaysAtLeast()
    {
        Seed();
        var builder = new Recorded(Capped);
        Warm(RunLine, builder);
        var result = Evaluate(RunLine, builder);
        Assert.Equal(GuideEvidenceStatus.Ready, result.Status);
        Assert.False(result.Table!.Complete);
        Assert.StartsWith("at least 2 word types", result.Table.Headline);
        Assert.Equal("at least 2", result.Table.CountText);
        Assert.Contains("stopped at the parser's work limit", result.Message);
    }

    [Theory]
    [InlineData("motif query corpus forms --corpus words.tsv --grammar grammar.json forms where 'readings > 0' --write-words out.tsv", RefusalCodes.GuideQueryRunNotReadOnly)]
    [InlineData("motif query corpus forms --corpus missing.tsv --grammar grammar.json forms where 'readings > 0'", null)]
    [InlineData("motif query corpus forms --corpus words.tsv --grammar grammar.json forms where 'nonsense >>'", null)]
    public void ARefusalIsShownAsARefusalWithItsCodeNeverAsAnEmptyTable(string line, string? code)
    {
        Seed();
        var result = Evaluate(line, new Recorded(Complete));
        Assert.Equal(GuideEvidenceStatus.Refused, result.Status);
        Assert.Null(result.Table);
        Assert.NotNull(result.RefusalCode);
        if (code is not null) Assert.Equal(code, result.RefusalCode);
        Assert.StartsWith(result.RefusalCode + ":", result.Message);
        Assert.False(File.Exists(Path.Combine(_folder, "out.tsv")));
    }

    [Fact]
    public void AnUnknownShowColumnIsARefusalNotACrash()
    {
        Seed();
        var builder = new Recorded(Complete);
        Warm(RunLine, builder);
        var shaped = Evaluate(RunLine, builder).Shaped(["form", "wombat"], null, null);
        Assert.Equal(GuideEvidenceStatus.Refused, shaped.Status);
        Assert.Contains("wombat", shaped.Message);
    }

    [Fact]
    public void ARunWritesNothingBesideTheSharedIndexAndLeavesTheProjectFolderAsItWas()
    {
        Seed();
        var builder = new Recorded(Complete);
        Warm(RunLine, builder);
        var before = Snapshot();
        Assert.Equal(GuideEvidenceStatus.Ready, Evaluate(RunLine, builder).Status);
        Assert.Equal(before, Snapshot());
        File.WriteAllText(Path.Combine(_folder, "other.tsv"), "solo\t1\n");
        before = Snapshot();
        Assert.Equal(GuideEvidenceStatus.ColdIndex, Evaluate(RunLine.Replace("words.tsv", "other.tsv", StringComparison.Ordinal), builder).Status);
        Assert.Equal(before, Snapshot());
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(_folder), path => Path.GetFileName(path) is "Assessments" or "Jobs");
    }

    // Reading the shared index touches it: its files count by name, all others by name and time.
    private string Snapshot() =>
        string.Join("\n", Directory.EnumerateFileSystemEntries(_folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => path + "|" + (File.Exists(path) && !path.StartsWith(CacheRoot, StringComparison.Ordinal) ? File.GetLastWriteTimeUtc(path).Ticks : 0)));

    [Fact]
    public void ABlocksDefinitionShapesTheRunWithTheDirectivesColumnsSortAndLimit()
    {
        Seed();
        var builder = new Recorded(Complete);
        Warm(RunLine, builder);
        var blocks = GuideQueryBlocks.Parse("```motif-query\nid: q\ntitle: T\nrun: " + RunLine + "\ncolumns: form, tokens\nsort: tokens desc\n```\n```motif-show\nquery: q\nshow: table\nlimit: 1\n```");
        Assert.Empty(blocks.Diagnostics);
        var shaped = Evaluate(blocks.Queries[0].Run, builder).Shaped(blocks.Queries[0].Columns, blocks.Queries[0].Sort, blocks.Shows[0].Limit);
        Assert.Equal(["solo"], shaped.Table!.Rows.Select(row => row[0]));
        Assert.Equal((1, 2), (shaped.Table.Truncated, shaped.Table.Types));
    }

    [Theory]
    [InlineData("motif query corpus forms --corpus words.tsv forms where \"form ~ $(curl evil)\"")]
    [InlineData("motif query corpus forms --corpus words.tsv forms where \"form ~ `id`\"")]
    [InlineData("motif query corpus forms --corpus words.tsv forms where \"a\nb\"")]
    public void HostileTextInsideDoubleQuotesIsRefusedNotShownAsACommand(string line)
    {
        Seed();
        var result = Evaluate(line, new Recorded(Complete));
        Assert.Equal(GuideEvidenceStatus.Refused, result.Status);
        Assert.Equal(RefusalCodes.GuideQueryRunNotReadOnly, result.RefusalCode);
        Assert.Null(result.BuildCommand);
    }

    [Fact]
    public void TheBuildCommandSingleQuotesEveryTokenAndEscapesQuotes()
    {
        var command = GuideEvidenceRunner.BuildCommand("motif query corpus forms --corpus words.tsv forms where 'it'\"'\"'s $(x)'", "/p'q");
        Assert.Equal("cd '/p'\\''q' && motif 'query' 'corpus' 'forms' '--corpus' 'words.tsv' 'forms' 'where' 'it'\\''s $(x)'", command);
    }

    [Theory]
    [InlineData("--corpus /etc/passwd", "corpus")]
    [InlineData("--corpus ../words.tsv", "corpus")]
    [InlineData("--corpus words.tsv --grammar /tmp/g.json", "grammar")]
    [InlineData("--corpus words.tsv --grammar sub/../../g.json", "grammar")]
    [InlineData("--corpus words.tsv --project /home/x/p.fwdata", "project")]
    public void APathOutsideTheProjectFolderIsRefusedBeforeAnythingIsRead(string flags, string flag)
    {
        Seed();
        var builder = new Recorded(Complete);
        var result = Evaluate("motif query corpus forms " + flags + " forms where 'readings > 0'", builder);
        Assert.Equal(GuideEvidenceStatus.Refused, result.Status);
        Assert.Equal(RefusalCodes.GuideQueryRunPathOutsideProject, result.RefusalCode);
        Assert.Equal(0, builder.Builds);
        Assert.Contains("--" + flag, result.Message);
    }

    [Fact]
    public void AnUnexpectedExceptionBecomesARefusalNamingItsType()
    {
        Seed();
        var result = GuideEvidenceRunner.Evaluate(RunLine, _folder, null, default, new Throwing(), CacheRoot);
        Assert.Equal(GuideEvidenceStatus.Refused, result.Status);
        Assert.Equal(RefusalCodes.GuideQueryRunInvalid, result.RefusalCode);
        Assert.Contains("FormatException", result.Message);
    }

    private sealed class Throwing : IRealizesIndexBuilder
    {
        public string Identity => throw new FormatException("boom");

        public string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken) => throw new FormatException("boom");
    }
}
