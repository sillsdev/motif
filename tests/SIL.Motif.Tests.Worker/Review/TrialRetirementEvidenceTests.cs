using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Runtime.Review;
using Xunit;

namespace SIL.Motif.Tests.Worker.Review;

public sealed class TrialRetirementEvidenceTests
{
    private const string Msa = "33333333-3333-3333-3333-333333333333";
    private const string FormA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string FormB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string FormG = "cccccccc-cccc-cccc-cccc-cccccccccccc";

    private static ParseAnalysis Reading(string form, ReadingAcceptance acceptance = ReadingAcceptance.LexiconBacked) =>
        new ReadingSignatureReader(null).Capture(new ParseAnalysis([new ParseMorph(form, Msa, null, null)]))
            with { MorphemeAcceptance = [acceptance] };

    private static ParseWordEvidence Evidence(string word, params ParseAnalysis[] readings) =>
        new("fieldworks-parse-analysis/v1", 0, word, 0, false, false, false, readings, []);

    private static ReviewCaseDelta Delta(string word, ParseWordEvidence? before, ParseWordEvidence? after,
        string caseKey = "case", IReadOnlyList<ReviewAssertion>? assertions = null)
    {
        return ReviewCaseClassifier.Compare(new ReviewCaseFacts(caseKey, "key-" + caseKey, word, "ws", before, after,
            assertions ?? [], []) { IdentityAvailable = true });
    }

    private static TrialRetirementEvidence Gather(string[] waived, params (ReviewCaseDelta Delta, ParseWordEvidence? Before, ParseWordEvidence? After)[] cases)
    {
        var evidence = new TrialRetirementEvidence(waived);
        foreach (var item in cases) evidence.Add(item.Delta, item.Before, item.After);
        return evidence;
    }

    private static (ReviewCaseDelta, ParseWordEvidence?, ParseWordEvidence?) Case(string word, ParseWordEvidence before,
        ParseWordEvidence after, string caseKey = "case", IReadOnlyList<ReviewAssertion>? assertions = null) =>
        (Delta(word, before, after, caseKey, assertions), before, after);

    [Fact]
    public void ADuplicateRetirementCarriesTheLostCopyCountAndTheLexiconBackedRemainder()
    {
        var a = Reading(FormA);
        var evidence = Gather(["cat"], Case("cat", Evidence("cat", a, a), Evidence("cat", a)));
        var row = Assert.Single(evidence.Rows);
        Assert.Equal(a.Signature, row.Signature);
        Assert.Equal(1, row.Count);
        Assert.Equal(1, row.RemainingReadingCount);
        Assert.Equal(a.Signature!.Id, row.ReadingIdentity);
        Assert.Equal("cat", row.Word);
        Assert.True(evidence.Attestation("sha256:r")!.Complete);
        Assert.Equal(1, evidence.Attestation("sha256:r")!.LostCopies);
        Assert.Equal(1, evidence.Attestation("sha256:r")!.RowCount);
    }

    [Fact]
    public void LosingADifferentReadingOfTheSameSpellingIsDistinguishableFromAnotherLoss()
    {
        var a = Reading(FormA);
        var b = Reading(FormB);
        var duplicate = Gather(["cat"], Case("cat", Evidence("cat", a, a), Evidence("cat", a))).Rows;
        var different = Gather(["cat"], Case("cat", Evidence("cat", a, b), Evidence("cat", a))).Rows;
        var kept = Gather(["cat"], Case("cat", Evidence("cat", a, b), Evidence("cat", a, b)));
        Assert.Equal(b.Signature, Assert.Single(different).Signature);
        Assert.NotEqual(Assert.Single(duplicate).Signature, Assert.Single(different).Signature);
        Assert.Empty(kept.Rows);
        Assert.True(kept.Attestation("sha256:r")!.Complete);
        Assert.Equal(0, kept.Attestation("sha256:r")!.LostCopies);
    }

    [Fact]
    public void RemainingReadingCountExcludesGuessedAndUnresolvedReadings()
    {
        var a = Reading(FormA);
        var guessed = Reading(FormG, ReadingAcceptance.Guessed);
        var unresolved = Reading(FormB, ReadingAcceptance.Unresolved);
        var row = Assert.Single(Gather(["cat"], Case("cat", Evidence("cat", a, a), Evidence("cat", a, guessed, unresolved))).Rows);
        Assert.Equal(1, row.Count);
        Assert.Equal(1, row.RemainingReadingCount);
    }

    [Fact]
    public void ACompleteWordLossNamesEveryLostReadingWithNothingRemaining()
    {
        var a = Reading(FormA);
        var b = Reading(FormB);
        var evidence = Gather(["cat"], Case("cat", Evidence("cat", a, b), Evidence("cat")));
        Assert.Equal([0L, 0L], evidence.Rows.Select(row => row.RemainingReadingCount!.Value).ToArray());
        Assert.Equal(new[] { a.Signature!.Id, b.Signature!.Id }.Order(StringComparer.Ordinal),
            evidence.Rows.Select(row => row.ReadingIdentity));
        Assert.True(evidence.Attestation("sha256:r")!.Complete);
    }

    [Fact]
    public void OnlyWaivedWordsGetSignatureRowsAndWordsMatchByNormalizedKey()
    {
        var a = Reading(FormA);
        var evidence = Gather(["e\u0301"], Case("\u00e9", Evidence("\u00e9", a), Evidence("\u00e9")),
            Case("dog", Evidence("dog", a), Evidence("dog"), "dog-case"));
        Assert.Equal("\u00e9", Assert.Single(evidence.Rows).Word);
        Assert.Equal(["\u00e9"], evidence.Attestation("d")!.WaivedWords);
    }

    [Fact]
    public void ALostReadingMatchingAFrozenApprovalKeepsItsAssertionLinks()
    {
        var a = Reading(FormA);
        var assertion = new ReviewAssertion("assertion-1", "identity-1", new ApprovedMorphology(
            [new ApprovedMorph(FormA, Msa, null, [])]), ReferenceAuthority.Human, "actor", "rev", true, false, true, ["src"]);
        var row = Assert.Single(Gather(["cat"], Case("cat", Evidence("cat", a), Evidence("cat"), "case", [assertion])).Rows);
        Assert.True(row.Approved);
        Assert.Equal(["assertion-1"], row.AssertionIds);
    }

    [Fact]
    public void EveryMeasuredContextOfAWaivedWordContributesItsOwnRows()
    {
        var a = Reading(FormA);
        var evidence = Gather(["cat"], Case("cat", Evidence("cat", a, a), Evidence("cat", a), "one"),
            Case("cat", Evidence("cat", a), Evidence("cat"), "two"));
        Assert.Equal(["one", "two"], evidence.Rows.Select(row => row.CaseKey));
        var attestation = evidence.Attestation("sha256:r")!;
        Assert.Equal(2, attestation.CaseCount);
        Assert.Equal(2, attestation.LostCopies);
    }

    [Fact]
    public void AnUnfinishedSideOrAMissingWaivedWordLeavesTheAttestationIncomplete()
    {
        var a = Reading(FormA);
        var stopped = Evidence("cat", a) with { Capped = true };
        var unfinished = Gather(["cat"], Case("cat", Evidence("cat", a, a), stopped)).Attestation("sha256:r")!;
        Assert.False(unfinished.Complete);
        Assert.Contains("case-incomplete", unfinished.Limitations);
        var missing = Gather(["cat", "ghost"], Case("cat", Evidence("cat", a), Evidence("cat", a))).Attestation("sha256:r")!;
        Assert.False(missing.Complete);
        Assert.Contains("waived-word-not-measured", missing.Limitations);
    }

    [Fact]
    public void AReadingWithoutACapturedSignatureLeavesTheAttestationIncomplete()
    {
        var bare = new ParseAnalysis([new ParseMorph(FormA, Msa, null, null)]);
        var evidence = Gather(["cat"], Case("cat", Evidence("cat", bare), Evidence("cat")));
        Assert.Empty(evidence.Rows);
        var attestation = evidence.Attestation("sha256:r")!;
        Assert.False(attestation.Complete);
        Assert.Contains("reading-signature-unavailable", attestation.Limitations);
    }

    [Fact]
    public void NoWaivedWordsMeansNoAttestation()
    {
        var a = Reading(FormA);
        Assert.Null(Gather([], Case("cat", Evidence("cat", a), Evidence("cat"))).Attestation("sha256:r"));
    }

    [Fact]
    public void TheAttestationIsBoundToTheReportDigest()
    {
        var a = Reading(FormA);
        var evidence = Gather(["cat"], Case("cat", Evidence("cat", a), Evidence("cat")));
        Assert.Equal("sha256:one", evidence.Attestation("sha256:one")!.ReportDigest);
        Assert.Equal("sha256:two", evidence.Attestation("sha256:two")!.ReportDigest);
    }
}
