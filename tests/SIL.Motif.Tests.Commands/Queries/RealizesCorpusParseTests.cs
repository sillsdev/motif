using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// <c>realizes(M, attested)</c> from the corpus parse: the index is one <c>batch --analyses --morph-spans</c> recorded from the pinned
/// PanGloss (<see cref="RecordedParse"/>) and replayed, and each matching word gives its piece from the spans with nothing traced.
/// </summary>
public sealed class RealizesCorpusParseTests : IDisposable
{
    private const string Suffix = "aefb1a12-318b-4a3d-9199-97bd2c6d0867:26438485-59b3-4166-9401-f767251e5508";
    private const string RootMsa = "568c4482-a8ab-46c5-8434-beadf5fcd625";

    private readonly string _folder = Directory.CreateTempSubdirectory("realizes-parse").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    /// <summary>The recording with one line edited; only the fields a test names change.</summary>
    private static string Derived(int line, Action<JsonObject> edit, string? word = null)
    {
        var lines = RecordedParse.BatchAnalyses.Split('\n');
        var row = JsonNode.Parse(lines[line])!.AsObject();
        if (word is not null) row["word"] = word;
        edit(row);
        lines[line] = row.ToJsonString();
        return string.Join("\n", lines);
    }

    private static JsonObject Spans(JsonObject row) => row["analyses"]![0]!["morphSpans"]!.AsObject();

    private static JsonObject Morpheme(JsonObject row, int item) => Spans(row)["morphemes"]![item]!.AsObject();

    private sealed class RecordedIndex(string recording = RecordedParse.BatchAnalyses, string identity = "recorded-build-0001") : IRealizesIndexBuilder
    {
        public int Builds { get; private set; }

        public string Identity { get; } = identity.PadRight(16, (char)48);

        public string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken)
        {
            Builds++;
            var path = Path.Combine(Directory.CreateTempSubdirectory("recorded-index").FullName, "analyses.jsonl");
            File.WriteAllText(path, recording);
            return path;
        }
    }

    private string Grammar => Path.Combine(_folder, "grammar.fwdata");

    private CommandOutcome<CorpusFormsResponse> Run(string where, string[] words,
        RecordedIndex? index = null, List<string>? notices = null, IRealizesIndexBuilder? builder = null)
    {
        var path = Path.Combine(_folder, "words.tsv");
        File.WriteAllText(path, string.Concat(words.Select((word, position) => word + "\t" + Math.Max(1, 10 - position) + "\n")));
        if (!File.Exists(Grammar)) File.WriteAllText(Grammar, "grammar one");
        return CorpusQueryCommand.Forms(new CorpusFormsRequest(path, Where: where, GrammarPath: Grammar), null, default,
            builder ?? index ?? new RecordedIndex(), Path.Combine(_folder, "cache"), (notices ?? []).Add);
    }

    [Fact]
    public void TheMorphemesPieceIsReadFromTheIndexSpansAndTheSearchRunsOverTheWholeCorpus()
    {
        var outcome = Run($"realizes({Suffix}, attested)", ["tikas", "tika", "itkas", "zzz"]);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var set = Assert.Single(outcome.Value!.Realizes!);
        var form = Assert.Single(set.Forms);
        Assert.Equal(("s", "corpus-parse", 2), (form.Form, form.Source, form.Words));
        Assert.Equal("(?:s\\z)", set.Pattern.Text);
        Assert.Equal("context", set.Pattern.Anchor);
        Assert.True(set.Pattern.OverBroad);
        Assert.StartsWith("s$", set.Pattern.Broadest);
        Assert.Equal(["tikas", "itkas"], outcome.Value.Rows.Select(row => row.Form));
        var parse = set.Parse!;
        Assert.Equal((4, 4, 2, 0, 1, 2, 0), (parse.Types, parse.Parsed, parse.WithMorpheme, parse.NoAnalysis, parse.Unsegmentable, parse.Placed, parse.WholeWord));
        Assert.True(parse.IndexBuilt);
        Assert.DoesNotContain(set.Reasons, reason => reason.Code is "piece_unplaced" or "piece_inconsistent");
    }

    [Fact]
    public void ASearchTermIsNotFlaggedBroadWhenItMatchesFewTypes()
    {
        string[] words = ["tikas", "itkas", .. Enumerable.Range(0, 18).Select(i => "w" + i)];
        var outcome = Run($"realizes({Suffix}, attested)", words);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var set = outcome.Value!.Realizes!.Single();
        Assert.False(set.Pattern.OverBroad);
        Assert.Null(set.Pattern.Broadest);
    }

    [Fact]
    public void EachPieceIsSearchedWithTheContextOfItsOpenSide()
    {
        var target = new RealizesTarget(null, "m", null);
        var root = new RealizesTarget(null, "m", null, IsRoot: true);
        Assert.Equal("ang$", RealizesPatternCompiler.Show(RealizesCorpusSource.Term("tinuang", "a", 4, target)));
        Assert.Equal("an$", RealizesPatternCompiler.Show(RealizesCorpusSource.Term("kaonan", "an", 4, target)));
        Assert.Equal("^gi", RealizesPatternCompiler.Show(RealizesCorpusSource.Term("gipangita", "gi", 0, target)));
        Assert.Equal("^gi$", RealizesPatternCompiler.Show(RealizesCorpusSource.Term("gi", "gi", -1, target)));
        Assert.Equal("kaon", RealizesPatternCompiler.Show(RealizesCorpusSource.Term("pagkaon", "kaon", 3, root)));
        Assert.Equal("on$", RealizesPatternCompiler.Show(RealizesCorpusSource.Term("pagkaon", "on", 5, root)));
    }

    [Fact]
    public void AWordTheGrammarCannotSegmentMakesTheSetIncompleteAndNegationIsRefused()
    {
        var all = Run($"realizes({Suffix}, attested)", ["tikas", "zzz"]).Value!.Realizes!.Single();
        Assert.False(all.Complete);
        Assert.Equal("unsegmentable", Assert.Single(all.Reasons).Code);
        var negated = Run($"not realizes({Suffix}, attested)", ["tikas", "zzz"]);
        Assert.Equal(RefusalCodes.QueryRealizesNegationIncomplete, negated.Refusal!.Code);
    }

    [Fact]
    public void WhenEveryWordParsedAndEveryMatchWasTracedTheSetIsCompleteAndNegationKeepsTheOtherWords()
    {
        var outcome = Run($"not realizes({Suffix}, attested)", ["tikas", "tika", "itkas"]);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(["tika"], outcome.Value!.Rows.Select(row => row.Form));
    }

    // The fixture grammar yields no typed null, so these derive one from the recording.
    private static void Unattribute(JsonObject row, string reason)
    {
        var run = Morpheme(row, 1)["spans"]![0]!.AsObject();
        Spans(row)["unattributed"] = new JsonArray(new JsonObject
        {
            ["segments"] = run["segments"]!.DeepClone(), ["offsets"] = run["offsets"]!.DeepClone(), ["text"] = "s", ["reason"] = reason, ["rule"] = null,
        });
    }

    private static void LoseSpan(JsonObject row)
    {
        Unattribute(row, "span_lost_by_rule");
        var morpheme = Morpheme(row, 1);
        morpheme["spans"] = null;
        morpheme["reason"] = "span_lost_by_rule";
        morpheme["rule"] = "Metathesis";
    }

    [Fact]
    public void ATypedNullSpanLeavesTheWholeWordStandingInAndSaysWhy()
    {
        var index = new RecordedIndex(Derived(0, LoseSpan));
        var set = Run($"realizes({Suffix}, attested)", ["tikas", "tika", "itkas"], index: index).Value!.Realizes!.Single();
        Assert.Contains(set.Forms, form => form is { Form: "tikas", Source: "corpus-parse-whole-word" });
        Assert.Contains(set.Forms, form => form is { Form: "s", Source: "corpus-parse", Words: 1 });
        var reason = Assert.Single(set.Reasons, reason => reason.Code == "piece_unplaced");
        Assert.Contains("span_lost_by_rule 1", reason.Detail);
        Assert.False(set.Complete);
        Assert.Equal((1, 1), (set.Parse!.Placed, set.Parse.WholeWord));
        var negated = Run($"not realizes({Suffix}, attested)", ["tikas", "tika", "itkas"], index: new RecordedIndex(Derived(0, LoseSpan)));
        Assert.Equal(RefusalCodes.QueryRealizesNegationIncomplete, negated.Refusal!.Code);
    }

    [Fact]
    public void AReadingWithNoSpanAnswerStandsInWholeWithItsTypedReason()
    {
        var index = new RecordedIndex(Derived(0, row => row["analyses"]![0]!["morphSpans"] = null));
        var set = Run($"realizes({Suffix}, attested)", ["tikas", "itkas"], index: index).Value!.Realizes!.Single();
        Assert.Contains(set.Forms, form => form is { Form: "tikas", Source: "corpus-parse-whole-word" });
        Assert.Contains("morph_spans_null 1", Assert.Single(set.Reasons, reason => reason.Code == "piece_unplaced").Detail);
        Assert.False(set.Complete);
    }

    [Fact]
    public void AMorphWithNoSegmentLeftStandsInWhole()
    {
        var index = new RecordedIndex(Derived(0, row =>
        {
            Unattribute(row, "no_morph_owner");
            Morpheme(row, 1)["spans"] = new JsonArray();
        }));
        var set = Run($"realizes({Suffix}, attested)", ["tikas", "itkas"], index: index).Value!.Realizes!.Single();
        Assert.Contains("no_segment_left 1", Assert.Single(set.Reasons, reason => reason.Code == "piece_unplaced").Detail);
        Assert.False(set.Complete);
    }

    [Fact]
    public void SpansThatDoNotTileTheSurfaceAreAnInconsistencyNotAPiece()
    {
        // The suffix claims two bytes that are not there: the runs overlap, so they cannot spell the surface.
        var index = new RecordedIndex(Derived(0, row =>
        {
            var run = Morpheme(row, 1)["spans"]![0]!.AsObject();
            run["offsets"] = new JsonArray(3, 5);
            run["text"] = "as";
        }));
        var set = Run($"realizes({Suffix}, attested)", ["tikas", "itkas"], index: index).Value!.Realizes!.Single();
        Assert.Contains(set.Forms, form => form is { Form: "tikas", Source: "corpus-parse-whole-word" });
        Assert.Contains(set.Reasons, reason => reason.Code == "piece_inconsistent");
        Assert.False(set.Complete);
    }

    [Fact]
    public void ASurfaceThatIsNotTheWordIsAnInconsistencyEvenWhenItsSpansTile()
    {
        var index = new RecordedIndex(Derived(0, row =>
        {
            Spans(row)["surface"] = "tikaz";
            Morpheme(row, 1)["spans"]![0]!["text"] = "z";
        }));
        var set = Run($"realizes({Suffix}, attested)", ["tikas", "itkas"], index: index).Value!.Realizes!.Single();
        Assert.Contains(set.Reasons, reason => reason.Code == "piece_inconsistent");
        Assert.Contains(set.Forms, form => form is { Form: "tikas", Source: "corpus-parse-whole-word" });
    }

    [Fact]
    public void ThePieceKeepsTheWordsCaseWhileThespanTextIsLowerCase()
    {
        var index = new RecordedIndex(Derived(0, _ => { }, word: "Tikas"));
        var set = Run($"realizes({RootMsa}, attested)", ["Tikas", "tika", "itkas"], index: index).Value!.Realizes!.Single();
        Assert.Contains(set.Forms, form => form is { Form: "Tika", Source: "corpus-parse" });
        Assert.Contains(set.Forms, form => form is { Form: "tika", Source: "corpus-parse" });
    }

    [Fact]
    public void AnIndexFromAnOlderFormatIsRebuiltNotMisread()
    {
        var cache = Directory.CreateDirectory(Path.Combine(_folder, "cache")).FullName;
        using (var stream = File.Create(Path.Combine(cache, "old.gz")))
        using (var zip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionLevel.Fastest))
        using (var writer = new StreamWriter(zip))
            writer.Write("motif-realizes-parse-index/v1\ntikas\t0\ta:b,c:d\n");
        using var old = File.OpenRead(Path.Combine(cache, "old.gz"));
        Assert.Null(RealizesParseIndex.Read(old));
        var index = new RecordedIndex();
        var notices = new List<string>();
        Assert.True(Run($"realizes({Suffix}, attested)", ["tikas", "itkas"], index, notices).Succeeded);
        Assert.Equal(1, index.Builds);
        Assert.DoesNotContain(notices, notice => notice.Contains("discarding"));
    }

    [Fact]
    public void AParserWithoutMorphSpansIsRefusedWithATypedNote()
    {
        var refusal = Assert.Throws<RealizesIndexException>(() => PanGlossIndexBuilder.RequireMorphSpans(RecordedParse.DescribeBatchV033));
        Assert.StartsWith("needs-pangloss-v0.34.0", refusal.Message);
        PanGlossIndexBuilder.RequireMorphSpans(RecordedParse.DescribeBatchV034);
        Assert.Throws<RealizesIndexException>(() => PanGlossIndexBuilder.RequireMorphSpans(null));
        Assert.Throws<RealizesIndexException>(() => PanGlossIndexBuilder.RequireMorphSpans("not json"));
    }

    [Fact]
    public void AnIndexParseStoppedAtALimitIsCountedAndMakesTheSetIncomplete()
    {
        var index = new RecordedIndex(RecordedParse.BatchAnalyses.Replace("\"capped\":false", "\"capped\":true"));
        var set = Run($"realizes({Suffix}, attested)", ["tikas", "itkas"], index: index).Value!.Realizes!.Single();
        Assert.Equal(2, set.Parse!.Capped);
        Assert.Contains(set.Reasons, reason => reason.Code == "parse_stopped");
        Assert.False(set.Complete);
    }

    [Fact]
    public void AWordTheBatchWroteNoLineForCountsAsFailed()
    {
        var index = new RecordedIndex(string.Join("\n", RecordedParse.BatchAnalyses.Split('\n').Take(1)));
        var set = Run($"realizes({Suffix}, attested)", ["tikas", "itkas"], index: index).Value!.Realizes!.Single();
        Assert.Equal((1, 1), (set.Parse!.Failed, set.Parse.WithMorpheme));
        Assert.Contains(set.Reasons, reason => reason.Code == "parse_failed");
    }

    [Fact]
    public void TheIndexIsBuiltOnceAndReusedUntilTheGrammarTheCorpusOrThePinnedParserChanges()
    {
        var notices = new List<string>();
        var index = new RecordedIndex();
        string[] words = ["tikas", "tika", "itkas"];
        var where = $"realizes({Suffix}, attested)";
        Assert.True(Run(where, words, index, notices).Value!.Realizes!.Single().Parse!.IndexBuilt);
        var warm = Run(where, words, index, notices).Value!.Realizes!.Single();
        Assert.False(warm.Parse!.IndexBuilt);
        Assert.Equal(1, index.Builds);
        Assert.Single(notices);
        Assert.Contains("building parse index", notices[0]);

        File.WriteAllText(Grammar, "grammar two");
        Run(where, words, index, notices);
        Assert.Equal(2, index.Builds);
        Run(where, [.. words, "extra"], index, notices);
        Assert.Equal(3, index.Builds);
        var other = new RecordedIndex(identity: "another-build");
        Run(where, [.. words, "extra"], other, notices);
        Assert.Equal(1, other.Builds);
        Run(where, [.. words, "extra"], other, notices);
        Assert.Equal(1, other.Builds);
    }

    private sealed class FailingIndex(string message) : IRealizesIndexBuilder
    {
        public string Identity => "failing-build-000001";

        public string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken) =>
            throw new RealizesIndexException(message);
    }

    [Fact]
    public void ABatchThatFailsIsRefusedWithItsErrorOutput()
    {
        var outcome = Run($"realizes({Suffix}, attested)", ["tikas"],
            index: null, builder: new FailingIndex("pangloss batch exited 3 and wrote no analyses. grammar load failed"));
        Assert.Equal(RefusalCodes.QueryRealizesIndexFailed, outcome.Refusal!.Code);
        Assert.Contains("exited 3", outcome.Refusal.Message);
        Assert.Contains("grammar load failed", outcome.Refusal.Facts!["detail"]);
    }

    [Fact]
    public void AMalformedAnalysesLineIsRefusedWithItsLineNumber()
    {
        var lines = RecordedParse.BatchAnalyses.Split('\n');
        var broken = new RecordedIndex(string.Join("\n", lines[0], "{\"word\":", lines[2]));
        var outcome = Run($"realizes({Suffix}, attested)", ["tikas", "itkas"], index: broken);
        Assert.Equal(RefusalCodes.QueryRealizesIndexFailed, outcome.Refusal!.Code);
        Assert.Contains("line 2", outcome.Refusal.Message);
        Assert.False(Directory.Exists(Path.Combine(_folder, "cache")));
    }

    private string TheOnlyCacheFile() => Assert.Single(Directory.EnumerateFiles(Path.Combine(_folder, "cache"), "*.gz"));

    [Theory]
    [InlineData("truncated")]
    [InlineData("garbage")]
    [InlineData("badrow")]
    public void AnUnreadableCachedIndexIsDeletedAndRebuiltOnceWithANote(string damage)
    {
        var notices = new List<string>();
        var index = new RecordedIndex();
        var where = $"realizes({Suffix}, attested)";
        Run(where, ["tikas", "itkas"], index, notices);
        var file = TheOnlyCacheFile();
        switch (damage)
        {
            case "truncated": File.WriteAllBytes(file, File.ReadAllBytes(file)[..20]); break;
            case "garbage": File.WriteAllText(file, "not a gzip file"); break;
            default:
                using (var stream = File.Create(file))
                using (var zip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionLevel.Fastest))
                using (var writer = new StreamWriter(zip))
                    writer.Write("motif-realizes-parse-index/v3\n{not json\n");
                break;
        }
        var outcome = Run(where, ["tikas", "itkas"], index, notices);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(2, index.Builds);
        Assert.Single(notices, notice => notice.Contains("discarding the cached parse index"));
        Run(where, ["tikas", "itkas"], index, notices);
        Assert.Equal(2, index.Builds);
    }

    [Fact]
    public void OnlyTheNewestEightIndexesAreKept()
    {
        var cache = Directory.CreateDirectory(Path.Combine(_folder, "cache")).FullName;
        for (var i = 0; i < 10; i++)
        {
            var old = Path.Combine(cache, $"old-{i:D2}.gz");
            File.WriteAllText(old, "x");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-20 + i));
        }
        Run($"realizes({Suffix}, attested)", ["tikas", "itkas"]);
        var kept = Directory.EnumerateFiles(cache, "*.gz").Select(Path.GetFileName).Order().ToList();
        Assert.Equal(RealizesParseIndexCache.KeepIndexes, kept.Count);
        Assert.DoesNotContain("old-00.gz", kept);
        Assert.Contains("old-09.gz", kept);
    }

    [Fact]
    public void AnIndexReadsBackAsItWasWritten()
    {
        var path = Path.Combine(_folder, "analyses.jsonl");
        File.WriteAllText(path, RecordedParse.BatchAnalyses);
        var index = RealizesParseIndex.FromBatch(path, ["tikas", "tika", "itkas", "zzz", "gone"]);
        using var stream = new MemoryStream();
        index.Write(stream);
        var back = RealizesParseIndex.Read(new MemoryStream(stream.ToArray()))!;
        Assert.Equal(index.Words.Keys.Order(), back.Words.Keys.Order());
        Assert.Equal(IndexedStatus.Unsegmentable, back.Words["zzz"].Status);
        Assert.Equal(IndexedStatus.Failed, back.Words["gone"].Status);
        Assert.Same(back.Words["tikas"], back.Get("tikas"));
        Assert.Null(back.Get("never-indexed"));
        var tikas = Assert.Single(back.Words["tikas"].Analyses);
        Assert.Equal("tikas", tikas.Surface);
        Assert.Null(tikas.Problem);
        Assert.Equal([new IndexedMorph("573d0f58-2271-409c-ba66-dac1d356373a", "568c4482-a8ab-46c5-8434-beadf5fcd625", [new IndexedRun(0, "tika")]),
            new IndexedMorph("99bbd021-3a3c-4a01-85ce-d419bd3fb849", "26438485-59b3-4166-9401-f767251e5508", [new IndexedRun(4, "s")])], tikas.Morphs, MorphComparer.Instance);
    }

    private sealed class MorphComparer : IEqualityComparer<IndexedMorph>
    {
        public static readonly MorphComparer Instance = new();

        public bool Equals(IndexedMorph? x, IndexedMorph? y) =>
            x!.Form == y!.Form && x.Msa == y.Msa && x.Reason == y.Reason && (x.Runs ?? []).SequenceEqual(y.Runs ?? []);

        public int GetHashCode(IndexedMorph morph) => morph.Form.GetHashCode();
    }

    [Fact]
    public void ANameWithoutAProjectIsRefusedBecauseOnlyALexiconCanResolveIt()
    {
        var outcome = Run("realizes(sfx:-s, attested)", ["tikas"]);
        Assert.Equal(RefusalCodes.QueryRealizesNeedsProject, outcome.Refusal!.Code);
        Assert.Equal("--project", outcome.Refusal.Facts!["flag"]);
    }
}
