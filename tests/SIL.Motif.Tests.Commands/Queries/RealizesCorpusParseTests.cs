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

    private CommandOutcome<CorpusFormsResponse> Run(Replay parser, string where, string[] words, int? sample = null)
    {
        var path = Path.Combine(_folder, "words.tsv");
        File.WriteAllText(path, string.Concat(words.Select((word, index) => word + "\t" + (10 - index) + "\n")));
        return CorpusQueryCommand.Forms(new CorpusFormsRequest(path, Where: where, GrammarPath: path, Sample: sample), null, default, parser);
    }

    [Fact]
    public void TheMorphemesPieceIsReadFromTheParseAndTheSearchRunsOverTheWholeCorpus()
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
        var parse = set.Parse!;
        Assert.Equal((4, 4, 2, 0, 1), (parse.Types, parse.Parsed, parse.WithMorpheme, parse.NoAnalysis, parse.Unsegmentable));
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
    public void WhenEveryWordParsedTheSetIsCompleteAndNegationKeepsTheOtherWords()
    {
        var outcome = Run(Recorded(), $"not realizes({Suffix}, attested)", ["tikas", "tika", "itkas"], sample: 0);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(["tika"], outcome.Value!.Rows.Select(row => row.Form));
    }

    [Fact]
    public void ASampleParsesOnlyThatManyWordsAndSaysSoAndIsTheSameEachTime()
    {
        var first = Recorded();
        var second = Recorded();
        var one = Run(first, $"realizes({Suffix}, attested)", ["tikas", "tika", "itkas"], sample: 2);
        Run(second, $"realizes({Suffix}, attested)", ["tikas", "tika", "itkas"], sample: 2);
        Assert.Equal(2, first.Asked.Count);
        Assert.Equal(first.Asked, second.Asked);
        var set = one.Value!.Realizes!.Single();
        Assert.Contains(set.Reasons, reason => reason.Code == "sampled");
        Assert.False(set.Complete);
        Assert.Equal((3, 2, 2), (set.Parse!.Types, set.Parse.Parsed, set.Parse.Sample));
    }

    [Fact]
    public void AParseStoppedAtTheWorkLimitIsCountedIncompleteAndRefusesWhenNothingElseCarriesIt()
    {
        var outcome = Run(Recorded(RecordedParse.Capped), $"realizes({Suffix}, attested)", ["tikas"], sample: 0);
        Assert.Equal(RefusalCodes.QueryRealizesNoParses, outcome.Refusal!.Code);
        var withOther = Run(Recorded(RecordedParse.Capped), $"realizes({Suffix}, attested)", ["tikas", "itkas"], sample: 0).Value!.Realizes!.Single();
        Assert.Equal(1, withOther.Parse!.Capped);
        Assert.Contains(withOther.Reasons, reason => reason.Code == "parse_stopped");
        Assert.False(withOther.Complete);
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
