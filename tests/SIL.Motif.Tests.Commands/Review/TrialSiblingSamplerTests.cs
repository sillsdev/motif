using System.Text;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class TrialSiblingSamplerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "siblings-" + Guid.NewGuid().ToString("N"));

    public TrialSiblingSamplerTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, true);

    private string Write(string text)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private static CorpusType[] Types(params (string Word, bool Parsed)[] items) => [.. items.Select(item => new CorpusType(item.Word, item.Parsed))];

    private static IReadOnlySet<string> None => new HashSet<string>();

    [Fact]
    public void AnAssessJsonReportReadsOutcomeAnalysedAsParsed()
    {
        var path = Write(System.Text.Json.JsonSerializer.Serialize(new
        {
            words = new[] { new { word = "Pika", outcome = "analysed" }, new { word = "pua", outcome = "unanalysed" } },
        }));

        var corpus = TrialSiblingSampler.ReadCorpus(path);

        Assert.True(corpus.Succeeded, corpus.Refusal?.Message);
        Assert.Equal([new CorpusType("pika", true), new CorpusType("pua", false)], corpus.Value!.Types);
        Assert.Equal(64, corpus.Value!.Sha256.Length);
    }

    [Fact]
    public void ALedgerReadsOpenAndDispositionAsUnparsedAndEveryOtherStateAsParsed()
    {
        var path = Write("word\tstate\n# comment\nabc\tOpen\nabd\tDisposition\nabe\tApproved\nabf\t\nabg\tdone\n");

        var types = TrialSiblingSampler.ReadCorpus(path).Value!.Types;

        Assert.Equal(["abe", "abg"], types.Where(type => type.Parsed).Select(type => type.Word));
        Assert.Equal(5, types.Count);
    }

    [Fact]
    public void APlainWordListParsesNothingAndAnEmptyOrBinaryFileIsRefused()
    {
        Assert.All(TrialSiblingSampler.ReadCorpus(Write("one\ntwo\r\n\nthree")).Value!.Types, type => Assert.False(type.Parsed));
        Assert.Equal(RefusalCodes.TrialSiblingsInvalid, TrialSiblingSampler.ReadCorpus(Write("")).Refusal!.Code);
        Assert.Equal(RefusalCodes.TrialSiblingsInvalid, TrialSiblingSampler.ReadCorpus(Write("word\tstate\n")).Refusal!.Code);
        Assert.Equal(RefusalCodes.TrialSiblingsInvalid, TrialSiblingSampler.ReadCorpus(Write("{\"nope\":1}")).Refusal!.Code);
        Assert.Equal(RefusalCodes.TrialSiblingsInvalid, TrialSiblingSampler.ReadCorpus(Path.Combine(_directory, "missing")).Refusal!.Code);
    }

    [Fact]
    public void ParsedTypesFillTheSampleFirstAndEachFormGetsItsTurn()
    {
        var corpus = Types(("aishx", true), ("bishx", true), ("cishx", false), ("dishx", false), ("xopa", true), ("yopa", false));

        var sample = TrialSiblingSampler.Sample(corpus, "sha", ["ish", "opa"], null, 3, "rev", None).Value!;

        Assert.Equal(PerFormLabel, sample.Pattern);
        Assert.Equal(3, sample.Words.Count);
        // Round-robin: an ish type, an opa type, then another ish type; parsed types come before unparsed ones.
        Assert.Contains(sample.Words[0], new[] { "aishx", "bishx" });
        Assert.Equal("xopa", sample.Words[1]);
        Assert.Contains(sample.Words[2], new[] { "aishx", "bishx" });
        Assert.NotEqual(sample.Words[0], sample.Words[2]);
        Assert.Equal(6, sample.Candidates);
        Assert.Equal(3, sample.ParsedCandidates);
    }

    private const string PerFormLabel = TrialSiblingSampler.PerForm;

    [Fact]
    public void TheSampleIsTheSameForTheSameSeedAndDiffersWithTheRevision()
    {
        var corpus = Types([.. Enumerable.Range(0, 200).Select(index => ("w" + index + "ish", false)), .. Filler(4000)]);

        var first = TrialSiblingSampler.Sample(corpus, "sha", ["ish"], null, 20, "rev-a", None).Value!;
        var again = TrialSiblingSampler.Sample(corpus, "sha", ["ish"], null, 20, "rev-a", None).Value!;
        var other = TrialSiblingSampler.Sample(corpus, "sha", ["ish"], null, 20, "rev-b", None).Value!;

        Assert.Equal(first.Words, again.Words);
        Assert.Equal(first.Seed, again.Seed);
        Assert.NotEqual(first.Words, other.Words);
        Assert.Equal(20, first.Words.Distinct().Count());
    }

    [Fact]
    public void TheSeededSampleMatchesAFixedVectorSoARuntimeChangeFails()
    {
        var corpus = Types([.. Enumerable.Range(0, 30).Select(index => ("w" + index + (index % 2 == 0 ? "ish" : "opa"), index % 3 == 0))]);

        var sample = TrialSiblingSampler.Sample(corpus, "sha", ["ish", "opa"], null, 8, "intent-digest", None).Value!;

        Assert.Equal(3319226552105666192L, sample.Seed);
        Assert.Equal("w24ish,w9opa,w18ish,w3opa,w6ish,w21opa,w12ish,w27opa", string.Join(",", sample.Words));
    }

    [Fact]
    public void AJsonArrayRootAndAnOversizedFileAreRefused()
    {
        Assert.Equal(RefusalCodes.TrialSiblingsInvalid, TrialSiblingSampler.ReadCorpus(Write("[\"a\",\"b\"]")).Refusal!.Code);
        var path = Write("word\n");
        using (var stream = new FileStream(path, FileMode.Append)) stream.SetLength(TrialSiblingSampler.MaxCorpusBytes + 1);

        var refused = TrialSiblingSampler.ReadCorpus(path).Refusal!;

        Assert.Equal(RefusalCodes.TrialSiblingsInvalid, refused.Code);
        Assert.Contains("larger than", refused.Message);
    }

    [Fact]
    public void WordsAlreadyInTheListAreNotSampledAndMarksAreStrippedFromFormsAndCaseIsFolded()
    {
        var corpus = Types(("Kuisha", false), ("kuishe", false), ("other", false));

        var sample = TrialSiblingSampler.Sample(corpus, "sha", ["-ISH-"], null, 10, "rev", new HashSet<string> { "kuisha" }).Value!;

        Assert.Equal(["ish"], sample.Forms);
        Assert.Equal(["kuishe"], sample.Words);
        Assert.Equal(1, sample.Candidates);
    }

    [Fact]
    public void APatternReplacesTheFormGroupsAndFormStandsForTheTouchedForms()
    {
        var corpus = Types(("kuishia", true), ("kuishe", false), ("anishia", true), ("zzz", true));

        var withForm = TrialSiblingSampler.Sample(corpus, "sha", ["ish"], "ku{form}.*", 10, "rev", None).Value!;
        var literal = TrialSiblingSampler.Sample(corpus, "sha", [], "z+", 10, "rev", None).Value!;

        Assert.Equal(["kuishe", "kuishia"], withForm.Words.Order());
        Assert.Equal("ku{form}.*", withForm.Pattern);
        Assert.Equal(["zzz"], literal.Words);
        Assert.Equal(RefusalCodes.TrialSiblingsInvalid,
            TrialSiblingSampler.Sample(corpus, "sha", ["ish"], "(", 10, "rev", None).Refusal!.Code);
    }

    [Fact]
    public void NoCandidateIsARefusalNotAnEmptySample()
    {
        var corpus = Types(("abc", true));

        Assert.Equal(RefusalCodes.TrialSiblingsNone, TrialSiblingSampler.Sample(corpus, "sha", ["zzz"], null, 5, "rev", None).Refusal!.Code);
        Assert.Equal(RefusalCodes.TrialSiblingsNone, TrialSiblingSampler.Sample(corpus, "sha", [], null, 5, "rev", None).Refusal!.Code);
        Assert.Equal(RefusalCodes.TrialSiblingsNone,
            TrialSiblingSampler.Sample(corpus, "sha", ["abc"], null, 5, "rev", new HashSet<string> { "abc" }).Refusal!.Code);
    }

    [Fact]
    public void AQuotedLedgerCellMayHoldTabsAndLineBreaksWithoutSplittingTheRow()
    {
        var path = Write("word\tstate\tnote\r\n\"abc\"\tApproved\t\"a\tb\r\nc \"\"q\"\"\"\r\nabd\tOpen\tplain\r\n" +
            "\"a\tb\"\tApproved\tx\r\nabe\tApproved\t\"two\nlines\"\n");

        var types = TrialSiblingSampler.ReadCorpus(path).Value!.Types;

        Assert.Equal([new CorpusType("abc", true), new CorpusType("abd", false), new CorpusType("abe", true)], types);
    }

    [Fact]
    public void AQuoteInAPlainWordListIsPartOfTheWordAndAnUnclosedQuoteInALedgerIsRefused()
    {
        Assert.Equal(["\"ab", "cd"], TrialSiblingSampler.ReadCorpus(Write("\"ab\ncd\n")).Value!.Types.Select(type => type.Word));
        Assert.Equal(RefusalCodes.TrialSiblingsInvalid,
            TrialSiblingSampler.ReadCorpus(Write(string.Join('\n', "word\tstate", "\"abc\tOpen", "abd\tOpen"))).Refusal!.Code);
    }

    // Filler types that contain none of the forms the tests use, so a form's share of the corpus is easy to set.
    private static (string, bool)[] Filler(int count) => [.. Enumerable.Range(0, count).Select(index => ("x" + index.ToString("D4"), true))];

    [Fact]
    public void ReferrerFormsDrawOneGroupBesideTheTouchedFormsAndNameAWordTheTouchedFormsMiss()
    {
        var corpus = Types([("kuishax", true), ("kwaa", true), ("kwab", true), .. Filler(100)]);

        var sample = TrialSiblingSampler.Sample(corpus, "sha", ["ish"], null, 8, "rev", None, ["kwa", "-KWB-", ""]).Value!;

        Assert.Equal(2, sample.ReferrerForms);
        Assert.Contains("kuishax", sample.Words);
        Assert.Equal(2, sample.Words.Count(word => word.StartsWith("kwa")));
        Assert.Equal(["ish"], sample.Forms);
        Assert.Null(sample.SkippedReferrerForms);
    }

    [Fact]
    public void AReferrerFormKeepsItsAffixMarkASuffixEndsTheTypeAPrefixStartsItAndABareFormMayBeAnywhere()
    {
        var corpus = Types([("kuish", true), ("ishku", true), ("prekuu", true), ("kuupre", true), ("amidb", true), .. Filler(100)]);

        Assert.Equal(["kuish"], Words(corpus, "-ish"));
        Assert.Equal(["kuish"], Words(corpus, "=ish"));
        Assert.Equal(["kuupre"], Words(corpus, "kuu-"));
        Assert.Equal(["prekuu"], Words(corpus, "-kuu"));
        Assert.Equal(["amidb"], Words(corpus, "mid"));
    }

    private static string[] Words(CorpusType[] corpus, string form) =>
        [.. TrialSiblingSampler.Sample(corpus, "sha", [], null, 50, "rev", None, [form]).Value!.Words.Order(StringComparer.Ordinal)];

    [Fact]
    public void AShortOrNonSelectiveReferrerFormIsSkippedAndReported()
    {
        var common = Enumerable.Range(0, 20).Select(index => ("kwaz" + index, true));
        var corpus = Types([("uverb", true), .. common, .. Filler(100)]);

        var sample = TrialSiblingSampler.Sample(corpus, "sha", [], null, 50, "rev", None, ["a", "kwa", "-verb"]).Value!;

        Assert.Equal(["uverb"], sample.Words);
        Assert.Equal(1, sample.ReferrerForms);
        Assert.Equal([("a", "shorter than 2 characters"), ("kwa", "matches 20 of 121 corpus types (over 5%)")],
            sample.SkippedReferrerForms!.Select(form => (form.Form, form.Reason)));
    }

    [Fact]
    public void TheReferrerGroupTakesAtMostAQuarterOfTheCap()
    {
        var referring = Enumerable.Range(0, 30).Select(index => ("kwa" + (char)('a' + index % 26) + index, true));
        var touched = Enumerable.Range(0, 30).Select(index => ("ish" + (char)('a' + index % 26) + index, true));
        var corpus = Types([.. referring, .. touched, .. Filler(1000)]);

        var sample = TrialSiblingSampler.Sample(corpus, "sha", ["ish"], null, 20, "rev", None, ["kwa"]).Value!;

        Assert.Equal(20, sample.Words.Count);
        Assert.Equal(5, sample.Words.Count(word => word.StartsWith("kwa")));
        Assert.Equal(15, sample.Words.Count(word => word.StartsWith("ish")));
    }

    [Fact]
    public void ALoneTrailingCarriageReturnKeepsTheLastWord()
    {
        Assert.Equal(["one", "two"], TrialSiblingSampler.ReadCorpus(Write("one\r\ntwo\r")).Value!.Types.Select(type => type.Word));
    }

    [Fact]
    public void ReferrerFormsAloneAreEnoughAndAPatternIgnoresThem()
    {
        var corpus = Types([("akwab", true), ("zzz", true), .. Filler(100)]);

        Assert.Equal(["akwab"], TrialSiblingSampler.Sample(corpus, "sha", [], null, 5, "rev", None, ["kwa"]).Value!.Words);
        Assert.Equal(["zzz"], TrialSiblingSampler.Sample(corpus, "sha", [], "z+", 5, "rev", None, ["kwa"]).Value!.Words);
        Assert.Null(TrialSiblingSampler.Sample(corpus, "sha", [], "z+", 5, "rev", None, ["kwa"]).Value!.ReferrerForms);
    }

    [Fact]
    public void ATouchedFormKeepsItsAffixMarkAndIsSkippedWhenShortOrCommonAndTheSkipSaysTouched()
    {
        var common = Enumerable.Range(0, 20).Select(index => ("kwaz" + index, true));
        var corpus = Types([("kuish", true), ("ishku", true), ("uverb", true), .. common, .. Filler(100)]);

        var sample = TrialSiblingSampler.Sample(corpus, "sha", ["-ish", "a", "kwa", "verb-"], null, 50, "rev", None).Value!;

        Assert.Equal(["kuish"], sample.Words);
        Assert.Equal([("a", "shorter than 2 characters", "touched"), ("kwa", "matches 20 of 123 corpus types (over 5%)", "touched")],
            sample.SkippedReferrerForms!.Select(form => (form.Form, form.Reason, form.Kind)));
    }

    [Fact]
    public void ACommonTouchedFormThatIsTheOnlyFormOfAnObjectIsKeptOnAQuarterOfTheCap()
    {
        var common = Enumerable.Range(0, 120).Select(index => ("kwa" + (char)('a' + index % 26) + index, true));
        var other = Enumerable.Range(0, 40).Select(index => ("ish" + (char)('a' + index % 26) + index, true));
        var corpus = Types([.. common, .. other, .. Filler(1000)]);

        var sample = TrialSiblingSampler.Sample(corpus, "sha", ["kwa", "ish"], null, 20, "rev", None, null,
            new HashSet<string> { "kwa" }).Value!;

        Assert.Equal(5, sample.Words.Count(word => word.StartsWith("kwa")));
        Assert.Equal(15, sample.Words.Count(word => word.StartsWith("ish")));
        Assert.Equal([("kwa", "kept: only form", "touched")], sample.SkippedReferrerForms!.Select(form => (form.Form, form.Reason, form.Kind)));
    }

    [Fact]
    public void AShortTouchedFormThatIsTheOnlyFormOfAnObjectIsKept()
    {
        var corpus = Types([("qx", true), ("aq", true), .. Filler(100)]);

        var sample = TrialSiblingSampler.Sample(corpus, "sha", ["q"], null, 10, "rev", None, null, new HashSet<string> { "q" }).Value!;

        Assert.Equal(["aq", "qx"], sample.Words.Order(StringComparer.Ordinal));
        Assert.Equal([("q", "kept: only form", "touched")], sample.SkippedReferrerForms!.Select(form => (form.Form, form.Reason, form.Kind)));
    }
}
