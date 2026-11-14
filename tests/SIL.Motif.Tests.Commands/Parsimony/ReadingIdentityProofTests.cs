using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Parsimony;

/// <summary>Checks that a Trial's before and after readings of the affected words are compared as sets, losing and gaining none.</summary>
public sealed class ReadingIdentityProofTests
{
    private const string Msa = "22222222-2222-2222-2222-222222222222";
    private const string Stem = "11111111-1111-1111-1111-111111111111";
    private const string Retired = "33333333-3333-3333-3333-333333333333";
    private const string Survivor = "44444444-4444-4444-4444-444444444444";
    private const string Other = "55555555-5555-5555-5555-555555555555";

    private static readonly IReadOnlyList<RetirementExpectationMapping> Mappings =
        [new(Retired, Msa, null, "whole", Survivor)];

    private static ParseAnalysis Reading(params string[] forms) =>
        new(forms.Select(form => new ParseMorph(form, Msa, null, null)).ToArray());

    private static AssessedWord Word(string word, params ParseAnalysis[] readings) =>
        new(word, readings.Length == 0 ? "no-analysis" : "analysed", [])
        {
            Morphology = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, word, 1, false, false, false, readings, []),
        };

    private static RecipeVerificationRun Run(params AssessedWord[] words) => new(
        new ComparableAssessment("a", "pangloss", "ParseTime", "none", "1", words), new BaselineToken("project", "sha256:" + new string('a', 64), "1", "2026-11-13T00:00:00Z",
            "sha256:" + new string('b', 64)), null, [], []);

    [Fact]
    public void ReadingsThatDifferOnlyByTheRetiredFormMappedToItsSurvivorAreIdentical()
    {
        var before = Run(Word("kan", Reading(Stem, Retired)), Word("kaun", Reading(Stem)));
        var after = Run(Word("kan", Reading(Stem, Survivor)), Word("kaun", Reading(Stem)));

        var proof = ReadingIdentityProof.Prove(before, after, ["kan", "kaun"], Mappings);

        Assert.Equal(ReadingIdentityStatus.Identical, proof.Status);
        Assert.Equal(2, proof.AffectedWords);
        Assert.Equal(2, proof.IdenticalWords);
        Assert.Equal(0, proof.LostReadings);
        Assert.Equal(0, proof.NewReadings);
        Assert.Empty(proof.Differences);
    }

    [Fact]
    public void AReadingThatDisappearsIsLostAndOneThatAppearsIsNewAndBothAreListed()
    {
        var before = Run(Word("kan", Reading(Stem, Retired), Reading(Other)));
        var after = Run(Word("kan", Reading(Stem, Survivor), Reading(Stem, Other)));

        var proof = ReadingIdentityProof.Prove(before, after, ["kan"], Mappings);

        Assert.Equal(ReadingIdentityStatus.Differs, proof.Status);
        Assert.Equal(1, proof.LostReadings);
        Assert.Equal(1, proof.NewReadings);
        var difference = Assert.Single(proof.Differences);
        Assert.Equal("kan", difference.Word);
        Assert.Single(difference.Lost);
        Assert.Single(difference.New);
    }

    [Fact]
    public void WithoutAMappingTheRetiredFormIsAnUnmatchedReading()
    {
        var before = Run(Word("kan", Reading(Stem, Retired)));
        var after = Run(Word("kan", Reading(Stem, Survivor)));

        var proof = ReadingIdentityProof.Prove(before, after, ["kan"], []);

        Assert.Equal(ReadingIdentityStatus.Differs, proof.Status);
        Assert.Equal((1, 1), (proof.LostReadings, proof.NewReadings));
    }

    [Fact]
    public void AWordTheTrialDidNotParseOrThatStoppedEarlyIsIncompleteNeverIdentical()
    {
        var stopped = Word("kan", Reading(Stem));
        stopped = stopped with { Morphology = stopped.Morphology! with { Capped = true } };
        var before = Run(Word("kan", Reading(Stem)), Word("kaun", Reading(Stem)));
        var after = Run(stopped);

        var proof = ReadingIdentityProof.Prove(before, after, ["kan", "kaun"], []);

        Assert.Equal(ReadingIdentityStatus.Incomplete, proof.Status);
        Assert.Equal(new[] { "kan", "kaun" }, proof.IncompleteWords);
        Assert.Equal(0, proof.IdenticalWords);
    }

    [Fact]
    public void AnEmptyAffectedSetProvesNothing()
    {
        var proof = ReadingIdentityProof.Prove(Run(), Run(), [], Mappings);

        Assert.Equal(ReadingIdentityStatus.Incomplete, proof.Status);
        Assert.Contains(proof.Unavailable, item => item.Contains("no affected words"));
    }

    [Fact]
    public void AWordOutsideTheAffectedSetNeverChangesTheVerdict()
    {
        var before = Run(Word("kan", Reading(Stem)), Word("other", Reading(Other)));
        var after = Run(Word("kan", Reading(Stem)), Word("other", Reading(Stem)));

        Assert.Equal(ReadingIdentityStatus.Identical, ReadingIdentityProof.Prove(before, after, ["kan"], []).Status);
    }

    [Fact]
    public void AWholeCorpusIsComparedInUnderASecondBecauseItIsOnePassOverTheTrialRows()
    {
        var words = Enumerable.Range(0, 62_000).Select(i => $"w{i}").ToArray();
        var before = Run(words.Select(word => Word(word, Reading(Stem, Retired), Reading(Other))).ToArray());
        var after = Run(words.Select(word => Word(word, Reading(Stem, Survivor), Reading(Other))).ToArray());

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var proof = ReadingIdentityProof.Prove(before, after, words, Mappings);
        clock.Stop();

        Assert.Equal(ReadingIdentityStatus.Identical, proof.Status);
        Assert.True(clock.ElapsedMilliseconds < 1000, $"took {clock.ElapsedMilliseconds} ms");
    }
}
