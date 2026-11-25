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
        var corpus = Types([.. Enumerable.Range(0, 200).Select(index => ("w" + index + "ish", false))]);

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
}
