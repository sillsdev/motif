using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// <c>realizes(M, attested)</c> from parsing a corpus, driven by <c>parse</c> replies recorded from a real PanGloss
/// (<see cref="RecordedParse"/>) and replayed word by word.
/// </summary>
public sealed class RealizesCorpusParseTests : IDisposable
{
    private const string Suffix = "aefb1a12-318b-4a3d-9199-97bd2c6d0867:26438485-59b3-4166-9401-f767251e5508";

    private readonly string _folder = Directory.CreateTempSubdirectory("realizes-parse").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private sealed class Replay(Dictionary<string, string> replies) : IRealizesParser
    {
        public List<string> Asked { get; } = [];

        public WordParse Parse(string word, CancellationToken cancellationToken)
        {
            Asked.Add(word);
            using var document = JsonDocument.Parse(replies[word]);
            var status = document.RootElement.GetProperty("status").GetString();
            return ServeParseReply.Read(status, document.RootElement.GetProperty("result").GetRawText());
        }
    }

    private static Replay Recorded(string tikas = RecordedParse.StemAndSuffix) => new(new Dictionary<string, string>
    {
        ["tikas"] = tikas, ["tika"] = RecordedParse.StemOnly, ["itkas"] = RecordedParse.OtherStemAndSuffix, ["zzz"] = RecordedParse.Unsegmentable,
    });

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

    private CommandOutcome<CorpusFormsResponse> Run(Replay parser, string where, string[] words, int? sample = null,
        RecordedIndex? index = null, List<string>? notices = null)
    {
        var path = Path.Combine(_folder, "words.tsv");
        File.WriteAllText(path, string.Concat(words.Select((word, position) => word + "\t" + (10 - position) + "\n")));
        if (!File.Exists(Grammar)) File.WriteAllText(Grammar, "grammar one");
        return CorpusQueryCommand.Forms(new CorpusFormsRequest(path, Where: where, GrammarPath: Grammar, Sample: sample), null, default, parser,
            index ?? new RecordedIndex(), Path.Combine(_folder, "cache"), (notices ?? []).Add);
    }

    [Fact]
    public void TheMorphemesPieceIsReadFromTheTraceOfTheWordsTheIndexSaysCarryItAndTheSearchRunsOverTheWholeCorpus()
    {
        var parser = Recorded();
        var outcome = Run(parser, $"realizes({Suffix}, attested)", ["tikas", "tika", "itkas", "zzz"], sample: 0);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var set = Assert.Single(outcome.Value!.Realizes!);
        var form = Assert.Single(set.Forms);
        Assert.Equal(("s", "corpus-parse", 2), (form.Form, form.Source, form.Words));
        Assert.Equal("(?:s)\\z", set.Pattern.Text);
        Assert.Equal("end", set.Pattern.Anchor);
        Assert.Equal(["tikas", "itkas"], outcome.Value.Rows.Select(row => row.Form));
        Assert.Equal(["itkas", "tikas"], parser.Asked.Order());
        var parse = set.Parse!;
        Assert.Equal((4, 4, 2, 0, 1, 2), (parse.Types, parse.Parsed, parse.WithMorpheme, parse.NoAnalysis, parse.Unsegmentable, parse.Traced));
        Assert.True(parse.IndexBuilt);
        Assert.Null(parse.Sample);
    }

    [Fact]
    public void AWordTheGrammarCannotSegmentMakesTheSetIncompleteAndNegationIsRefused()
    {
        var all = Run(Recorded(), $"realizes({Suffix}, attested)", ["tikas", "zzz"], sample: 0).Value!.Realizes!.Single();
        Assert.False(all.Complete);
        Assert.Equal("unsegmentable", Assert.Single(all.Reasons).Code);
        var negated = Run(Recorded(), $"not realizes({Suffix}, attested)", ["tikas", "zzz"], sample: 0);
        Assert.Equal(RefusalCodes.QueryRealizesNegationIncomplete, negated.Refusal!.Code);
    }

    [Fact]
    public void WhenEveryWordParsedAndEveryMatchWasTracedTheSetIsCompleteAndNegationKeepsTheOtherWords()
    {
        var outcome = Run(Recorded(), $"not realizes({Suffix}, attested)", ["tikas", "tika", "itkas"], sample: 0);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(["tika"], outcome.Value!.Rows.Select(row => row.Form));
    }

    [Fact]
    public void TheTraceStageIsCappedAtTheSampleAndTheMatchesItSkippedStandInWhole()
    {
        var first = Recorded();
        var second = Recorded();
        var capped = Run(first, $"realizes({Suffix}, attested)", ["tikas", "tika", "itkas"], sample: 1);
        Run(second, $"realizes({Suffix}, attested)", ["tikas", "tika", "itkas"], sample: 1);
        Assert.Single(first.Asked);
        Assert.Equal(first.Asked, second.Asked);
        var set = capped.Value!.Realizes!.Single();
        Assert.Contains(set.Reasons, reason => reason.Code == "trace_capped");
        Assert.False(set.Complete);
        Assert.Equal((2, 1, 1), (set.Parse!.WithMorpheme, set.Parse.Traced, set.Parse.Sample));
        Assert.Contains(set.Forms, form => form.Source == "corpus-parse-whole-word");
        Assert.Equal(["itkas", "tikas"], capped.Value.Rows.Select(row => row.Form).Order());
        var negated = Run(Recorded(), $"not realizes({Suffix}, attested)", ["tikas", "tika", "itkas"], sample: 1);
        Assert.Equal(RefusalCodes.QueryRealizesNegationIncomplete, negated.Refusal!.Code);
    }

    [Fact]
    public void ATraceWithNoAnalysisLeavesTheWholeWordStandingInAndSaysSo()
    {
        var set = Run(Recorded(RecordedParse.Capped), $"realizes({Suffix}, attested)", ["tikas"], sample: 0).Value!.Realizes!.Single();
        var form = Assert.Single(set.Forms);
        Assert.Equal(("tikas", "corpus-parse-whole-word"), (form.Form, form.Source));
        Assert.Contains(set.Reasons, reason => reason.Code == "piece_unaligned");
        Assert.False(set.Complete);
    }

    [Fact]
    public void AnIndexParseStoppedAtALimitIsCountedAndMakesTheSetIncomplete()
    {
        var index = new RecordedIndex(RecordedParse.BatchAnalyses.Replace("\"capped\":false", "\"capped\":true"));
        var set = Run(Recorded(), $"realizes({Suffix}, attested)", ["tikas", "itkas"], sample: 0, index: index).Value!.Realizes!.Single();
        Assert.Equal(2, set.Parse!.Capped);
        Assert.Contains(set.Reasons, reason => reason.Code == "parse_stopped");
        Assert.False(set.Complete);
    }

    [Fact]
    public void AWordTheBatchWroteNoLineForCountsAsFailed()
    {
        var index = new RecordedIndex(string.Join("\n", RecordedParse.BatchAnalyses.Split('\n').Take(1)));
        var set = Run(Recorded(), $"realizes({Suffix}, attested)", ["tikas", "itkas"], sample: 0, index: index).Value!.Realizes!.Single();
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
        Assert.True(Run(Recorded(), where, words, 0, index, notices).Value!.Realizes!.Single().Parse!.IndexBuilt);
        var warm = Run(Recorded(), where, words, 0, index, notices).Value!.Realizes!.Single();
        Assert.False(warm.Parse!.IndexBuilt);
        Assert.Equal(1, index.Builds);
        Assert.Single(notices);
        Assert.Contains("building parse index", notices[0]);

        File.WriteAllText(Grammar, "grammar two");
        Run(Recorded(), where, words, 0, index, notices);
        Assert.Equal(2, index.Builds);
        Run(Recorded(), where, [.. words, "extra"], 0, index, notices);
        Assert.Equal(3, index.Builds);
        var other = new RecordedIndex(identity: "another-build");
        Run(Recorded(), where, [.. words, "extra"], 0, other, notices);
        Assert.Equal(1, other.Builds);
        Run(Recorded(), where, [.. words, "extra"], 0, other, notices);
        Assert.Equal(1, other.Builds);
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
        var tikas = Assert.Single(back.Words["tikas"].Analyses);
        Assert.Equal([new IndexedMorph("573d0f58-2271-409c-ba66-dac1d356373a", "568c4482-a8ab-46c5-8434-beadf5fcd625"),
            new IndexedMorph("99bbd021-3a3c-4a01-85ce-d419bd3fb849", "26438485-59b3-4166-9401-f767251e5508")], tikas);
    }

    [Fact]
    public void ANameWithoutAProjectIsRefusedBecauseOnlyALexiconCanResolveIt()
    {
        var outcome = Run(Recorded(), "realizes(sfx:-s, attested)", ["tikas"], sample: 0);
        Assert.Equal(RefusalCodes.QueryRealizesNeedsProject, outcome.Refusal!.Code);
        Assert.Equal("--project", outcome.Refusal.Facts!["flag"]);
    }

    [Fact]
    public void AShapeThatDoesNotLineUpWithTheWordGivesNoPiece()
    {
        Assert.Null(SurfaceShape.Pieces("tikas", "tika+?z", 2));
        Assert.Null(SurfaceShape.Pieces("tikas", "tikas", 2));
        Assert.Equal([("tika", 0), ("s", 4)], SurfaceShape.Pieces("tikas", "tika+?s", 2));
    }

    // Shapes copied from real parses of Cebuano words: a class lists the spellings a character stands for.
    [Theory]
    [InlineData("gipangita", "[gG()/][ieEI()/]+?[pP()/][aA()/]+?[(ng)(NG)(Ng)ŋŊ()/][ieEI()/][tT()/][aA()/]", new[] { "gi", "pa", "ngita" })]
    [InlineData("Pagkaon", "[pP()/][aA()/][gG()/]+?[kK()/][aA()/][uoOU()/][nN()/]", new[] { "Pag", "kaon" })]
    [InlineData("kan-on", "[kK()/][aA()/][nN()/][-()/]+?[uoOU()/][nN()/]", new[] { "kan-", "on" })]
    public void ClassesInAShapeAreMatchedAgainstTheWordsOwnSpelling(string word, string shape, string[] expected) =>
        Assert.Equal(expected, SurfaceShape.Pieces(word, shape, expected.Length)!.Select(piece => piece.Text));

    // Cebuano tinuang is tinu + an + ng; the grammar drops the n of -an before ng, so the suffix surfaces as a.
    [Fact]
    public void AMorphsPieceCanBeShorterThanItsFormWhenThePiecesStillAddUpToTheWord() =>
        Assert.Equal(["tinu", "a", "ng"], SurfaceShape.Pieces("tinuang",
            "[tT()/][ieEI()/][nN()/][uoOU()/]+?[aA()/]+?[(ng)(NG)(Ng)ŋŊ()/]", 3)!.Select(piece => piece.Text));

    [Fact]
    public void TheSampleIsWeightedByFrequencyAndStable()
    {
        var words = Enumerable.Range(0, 200).Select(i => ("w" + i, i == 0 ? 100000L : 1L)).ToList();
        var chosen = RealizesSample.Choose(words, 20).Select(word => word.Form).ToList();
        Assert.Contains("w0", chosen);
        Assert.Equal(20, chosen.Count);
        Assert.Equal(chosen, RealizesSample.Choose(words, 20).Select(word => word.Form));
        Assert.Equal(200, RealizesSample.Choose(words, 0).Count);
    }
}
