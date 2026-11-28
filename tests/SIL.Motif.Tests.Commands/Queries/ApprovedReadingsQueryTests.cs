using System.Text.Json.Nodes;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Query.Readings;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary><c>--approved</c>: an approved-readings file compared with the readings of the corpus parse.</summary>
public sealed class ApprovedReadingsQueryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("approved-readings-query").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private static string Id(int n) => Guid.Parse($"00000000-0000-0000-0000-0000000000a{n}").ToString("D");

    private static readonly string Nan = Id(1);
    private static readonly string Avar = Id(2);
    private static readonly string Kal = Id(3);
    private static readonly string En = Id(4);
    private static readonly string Ru = Id(5);
    private static readonly string EnruSun = Id(6);
    private static readonly string Un = Id(7);
    private static readonly string Du = Id(8);

    private static (string Msa, string Text) M(string msa, string text) => (msa, text);

    private static string Line(string word, params (string Msa, string Text)[][] readings) => LineWith(word, false, readings);

    private static string LineWith(string word, bool capped, params (string Msa, string Text)[][] readings)
    {
        var analyses = new JsonArray();
        foreach (var reading in readings)
        {
            var morphs = new JsonArray();
            var spans = new JsonArray();
            var at = 0;
            for (var item = 0; item < reading.Length; item++)
            {
                morphs.Add(new JsonObject { ["form"] = reading[item].Msa.Replace("a", "c"), ["msa"] = reading[item].Msa, ["inflType"] = null, ["guessedString"] = null });
                var end = at + System.Text.Encoding.UTF8.GetByteCount(reading[item].Text);
                spans.Add(new JsonObject
                {
                    ["item"] = item, ["reason"] = null, ["rule"] = null,
                    ["spans"] = new JsonArray(new JsonObject { ["offsets"] = new JsonArray(at, end), ["segments"] = new JsonArray(at, end), ["text"] = reading[item].Text }),
                });
                at = end;
            }
            analyses.Add(new JsonObject
            {
                ["morphs"] = morphs,
                ["morphSpans"] = new JsonObject { ["morphemes"] = spans, ["segmentCount"] = at + 1, ["surface"] = word, ["unattributed"] = new JsonArray() },
            });
        }
        return new JsonObject
        {
            ["schema"] = "fieldworks-parse-analysis/v1", ["index"] = 0, ["word"] = word, ["elapsedMs"] = 0,
            ["capped"] = capped, ["timedOut"] = false, ["invalidShape"] = false, ["analyses"] = analyses, ["unavailable"] = new JsonArray(),
        }.ToJsonString();
    }

    private sealed class Recorded(string recording) : IRealizesIndexBuilder
    {
        public string Identity { get; } = "approved-clean".PadRight(16, '0');

        public string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken)
        {
            var path = Path.Combine(Directory.CreateTempSubdirectory("recorded-approved").FullName, "analyses.jsonl");
            File.WriteAllText(path, recording);
            return path;
        }
    }

    private static readonly string Recording = string.Join("\n", new[]
    {
        Line("நான்", [M(Nan, "நான்")]),
        Line("அவர்கள்", [M(Avar, "அவர்"), M(Kal, "கள்")]),
        Line("என்று", [M(En, "என்"), M(Ru, "று")], [M(EnruSun, "என்று")]),
        Line("உண்டு", [M(Un, "உண்"), M(Du, "டு")]),
    }) + "\n";

    private const string Corpus = "நான்\t6831\nஅவர்கள்\t4932\nஎன்று\t4196\nஉண்டு\t171\n";

    private static MorphCatalog Catalog() => MorphCatalog.From(new (string, string, string, string?)[]
    {
        (Nan, Guid.Parse("00000000-0000-0000-0000-0000000000e1").ToString("D"), MorphKinds.Stem, "I"),
        (Avar, Guid.Parse("00000000-0000-0000-0000-0000000000e2").ToString("D"), MorphKinds.Stem, "he.HON"),
        (Kal, Guid.Parse("00000000-0000-0000-0000-0000000000e3").ToString("D"), MorphKinds.Inflectional, "PL"),
        (En, Guid.Parse("00000000-0000-0000-0000-0000000000e4").ToString("D"), MorphKinds.Stem, "say"),
        (Ru, Guid.Parse("00000000-0000-0000-0000-0000000000e5").ToString("D"), MorphKinds.Inflectional, "PST.VPT"),
        (EnruSun, Guid.Parse("00000000-0000-0000-0000-0000000000e6").ToString("D"), MorphKinds.Stem, "sun"),
        (Un, Guid.Parse("00000000-0000-0000-0000-0000000000e7").ToString("D"), MorphKinds.Stem, "eat"),
        (Du, Guid.Parse("00000000-0000-0000-0000-0000000000e8").ToString("D"), MorphKinds.Inflectional, "PST"),
    });

    // Lines 1, 2, 4, 10 and 284 of the Tamil lane's approved-top500.jsonl, as written.
    private const string RealLines = """
        {"rank": 1, "type": "நான்", "frequency": 6831, "status": "correct", "readings": [{"gloss": "நான்/I", "class": "correct", "note": ""}]}
        {"rank": 2, "type": "அவர்கள்", "frequency": 4932, "status": "correct", "readings": [{"gloss": "அவர்/he.HON + கள்/PL", "class": "correct", "note": ""}]}
        {"rank": 4, "type": "என்று", "frequency": 4196, "status": "mixed", "readings": [{"gloss": "என்/say + று/PST.VPT", "class": "correct", "note": ""}, {"gloss": "என்று/sun", "class": "wrong", "pattern": "sun", "note": "என்று \"sun\" is a Tamil Lexicon headword but never this corpus's sense; quotative say+VPT is the reading (Lehmann 1989 quotative என்று)"}]}
        {"rank": 10, "type": "ஆனால்", "frequency": 2580, "status": "missing:no-reading", "readings": [{"gloss": null, "class": "missing", "note": "no reading"}]}
        {"rank": 284, "type": "உண்டு", "frequency": 171, "status": "correct+missing-sense", "readings": [{"gloss": "உண்/eat + டு/PST.VPT", "class": "correct", "note": ""}, {"gloss": "<absent> existential \"there is\" (Schiffman 1999; Lehmann 1989)", "class": "missing", "note": "correct reading absent"}]}
        """;

    private static string Oracle(string word, string gloss) => "{\"type\": \"" + word + "\", \"readings\": [{\"gloss\": \"" + gloss + "\", \"class\": \"correct\"}]}";

    private CommandOutcome<CorpusFormsResponse> Run(string approved, bool grammar = true, string? recording = null, string? words = null)
    {
        var corpus = Path.Combine(_folder, "words.tsv");
        File.WriteAllText(corpus, words ?? Corpus);
        var grammarPath = Path.Combine(_folder, "grammar.json");
        File.WriteAllText(grammarPath, "grammar");
        var file = Path.Combine(_folder, "approved.jsonl");
        File.WriteAllText(file, approved);
        return CorpusQueryCommand.Forms(new CorpusFormsRequest(corpus, GrammarPath: grammar ? grammarPath : null, ProjectPath: "p.fwdata", Approved: file),
            null, default, new Recorded(recording ?? Recording), Path.Combine(_folder, "cache"), catalog: Catalog());
    }

    [Fact]
    public void RealOracleLinesAreComparedByRenderedGlossWithKeptMissingExtraAndBothShares()
    {
        var outcome = Run(RealLines + "\n" + Oracle("gone", "g/x"));
        var report = Assert.IsType<ApprovedReport>(outcome.Value!.Approved);
        var totals = report.Totals;
        Assert.Equal((5, 4, 1, 1, 5, 3, 2, 2), (totals.ApprovedWords, totals.ApprovedWordsInCorpus, totals.ApprovedWordsNotInCorpus, totals.WordsWithoutApprovedReading,
            totals.ApprovedReadings, totals.Kept, totals.Missing, totals.Extra));
        Assert.Equal((16130L, 15959L, 11763L), (totals.Tokens, totals.TokensAllKept, totals.TokensAllKeptNoExtras));
        Assert.Equal(11763d / 16130, totals.ApprovedShareTokens!.Value, 9);
        Assert.Equal(15959d / 16130, totals.ApprovedShareTokensIgnoringExtras!.Value, 9);

        var plural = report.Words.Single(word => word.Word == "அவர்கள்");
        Assert.Equal(["அவர்/he.HON + கள்/PL"], plural.Kept);
        Assert.True(plural.AllApprovedKept == true && plural.NoExtras == true);
        var quotative = report.Words.Single(word => word.Word == "என்று");
        Assert.Equal(["என்று/sun"], quotative.Extra);
        Assert.Equal(["என்/say + று/PST.VPT"], quotative.Kept);
        var eat = report.Words.Single(word => word.Word == "உண்டு");
        Assert.Equal(["உண்/eat + டு/PST.VPT"], eat.Missing);
        Assert.Equal(["உண்/eat + டு/PST"], eat.Extra);
        Assert.DoesNotContain(report.Words, word => word.Word == "ஆனால்");
    }

    [Fact]
    public void AWordAbsentFromTheCorpusHasNoTokensAndAllItsReadingsMissing()
    {
        var report = Run(Oracle("gone", "g/x")).Value!.Approved!;
        var word = Assert.Single(report.Words);
        Assert.False(word.InCorpus);
        Assert.Equal(("absent", 0L), (word.ParseState, word.Tokens));
        Assert.Equal(["g/x"], word.Missing);
        Assert.Null(report.Totals.ApprovedShareTokens);
    }

    [Fact]
    public void ACappedWordWithPartialReadingsIsIncompleteAndStaysOutOfTheNumerators()
    {
        var recording = string.Join("\n", Line("நான்", [M(Nan, "நான்")]), LineWith("அவர்கள்", true, [M(Avar, "அவர்"), M(Kal, "கள்")])) + "\n";
        var report = Run(RealLines, recording: recording, words: "நான்\t6831\nஅவர்கள்\t4932\n").Value!.Approved!;
        var capped = report.Words.Single(word => word.Word == "அவர்கள்");
        Assert.Equal(ReadingStates.Capped, capped.ParseState);
        Assert.Null(capped.AllApprovedKept);
        Assert.Null(capped.NoExtras);
        Assert.False(report.Complete);
        var totals = report.Totals;
        Assert.Equal((1, 4932L, 11763L, 6831L, 6831L), (totals.IncompleteWords, totals.IncompleteTokens, totals.Tokens, totals.TokensAllKept, totals.TokensAllKeptNoExtras));
        Assert.Equal(6831d / 11763, totals.ApprovedShareTokens!.Value, 9);
    }

    [Fact]
    public void AFailedWordIsIncompleteWithAllItsReadingsMissing()
    {
        var recording = Line("நான்", [M(Nan, "நான்")]) + "\n";
        var report = Run(RealLines, recording: recording, words: "நான்\t6831\nஅவர்கள்\t4932\n").Value!.Approved!;
        var failed = report.Words.Single(word => word.Word == "அவர்கள்");
        Assert.Equal(ReadingStates.Failed, failed.ParseState);
        Assert.Null(failed.AllApprovedKept);
        Assert.Null(failed.NoExtras);
        Assert.Equal(["அவர்/he.HON + கள்/PL"], failed.Missing);
        Assert.Equal((2, 0), (report.Totals.Missing, report.Totals.Extra));
        Assert.Equal((1, 4932L, 11763L, 6831L), (report.Totals.IncompleteWords, report.Totals.IncompleteTokens, report.Totals.Tokens, report.Totals.TokensAllKeptNoExtras));
    }

    [Fact]
    public void OnlyTheCorrectClassIsApprovedAndOtherKeysAreIgnored()
    {
        var report = Run("{\"type\": \"என்று\", \"rank\": 4, \"readings\": [{\"gloss\": \"என்று/sun\", \"class\": \"wrong\"}, {\"gloss\": \"என்/say + று/PST.VPT\", \"class\": \"correct\", \"x\": 1}]}").Value!.Approved!;
        Assert.Equal(["என்/say + று/PST.VPT"], report.Words.Single().Kept);
        Assert.Equal(["என்று/sun"], report.Words.Single().Extra);
    }

    [Theory]
    [InlineData("{\"type\": \"நான்\", \"readings\": [{\"gloss\": \"a\", \"class\": \"correct\"}]}\nnot json", 2)]
    [InlineData("{\"type\": \"நான்\"}", 1)]
    [InlineData("{\"word\": \"நான்\", \"readings\": []}", 1)]
    [InlineData("{\"type\": \"நான்\", \"readings\": [\"a\"]}", 1)]
    [InlineData("{\"type\": \"நான்\", \"readings\": [{\"class\": \"correct\"}]}", 1)]
    [InlineData("\n{\"type\": \"நான்\", \"readings\": []}\n{\"type\": \"நான்\", \"readings\": []}", 3)]
    public void AMalformedFileIsRefusedWithATypedCodeNamingTheLine(string file, int line)
    {
        var outcome = Run(file);
        Assert.Equal(RefusalCodes.QueryApprovedInvalid, outcome.Refusal!.Code);
        Assert.Contains("Line " + line, outcome.Refusal.Message);
    }

    [Fact]
    public void AFileWithNoCorrectReadingIsRefused()
    {
        Assert.Equal(RefusalCodes.QueryApprovedInvalid, Run("{\"type\": \"ஆனால்\", \"readings\": [{\"gloss\": null, \"class\": \"missing\"}]}").Refusal!.Code);
    }

    [Fact]
    public void ApprovedNeedsAGrammar()
    {
        Assert.Equal(RefusalCodes.QueryReadingsNeedsGrammar, Run(Oracle("நான்", "நான்/I"), grammar: false).Refusal!.Code);
    }
}
