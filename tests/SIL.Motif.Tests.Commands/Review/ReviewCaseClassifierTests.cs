using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Runtime.Review;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class ReviewCaseClassifierTests
{
    private const string Form1 = "11111111-1111-1111-1111-111111111111";
    private const string Form2 = "22222222-2222-2222-2222-222222222222";
    private const string Msa = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public void MatchedCountUnchangedStillDetectsReplacedApprovedReading()
    {
        var result = ReviewCaseClassifier.Compare(Facts([Form1], [Form2], [Assertion(Form1, true), Assertion(Form2, true)]));
        Assert.True(result.NewApprovedLoss);
        Assert.Contains(result.ReadingLosses, item => item.ReadingIdentity == Form1);
    }

    [Fact]
    public void KeptReadingDoesNotHideSecondApprovedLoss()
    {
        var result = ReviewCaseClassifier.Compare(Facts([Form1, Form2], [Form1], [Assertion(Form1, true), Assertion(Form2, true)]));
        Assert.Single(result.ReadingLosses);
        Assert.Equal(Form2, result.ReadingLosses[0].ReadingIdentity);
    }

    [Fact]
    public void ApprovedLossLabelsBothFlagsWithoutDoubleCounting()
    {
        var result = ReviewCaseClassifier.Compare(Facts([Form1], [], [Assertion(Form1, true)]));
        Assert.True(result.NewNoParse);
        Assert.True(result.NewApprovedLoss);
        Assert.Equal([ReviewFlagId.NewWordFailures, ReviewFlagId.ConfirmedReadingsLost], result.Flags);
        Assert.Single(result.ReadingLosses);
    }

    [Fact]
    public void ReferenceMembershipWithoutExactAssertionIsNotConfirmed()
    {
        var assertion = Assertion(Form1, false) with { Morphology = null };
        var result = ReviewCaseClassifier.Compare(Facts([Form1], [], [assertion]));
        Assert.DoesNotContain(ReviewFlagId.ConfirmedReadingsLost, result.Flags);
        Assert.NotEmpty(result.Limitations);
    }

    [Fact]
    public void PreexistingLossIsNotNew()
    {
        var result = ReviewCaseClassifier.Compare(Facts([], [], [Assertion(Form1, true)]));
        Assert.Empty(result.Flags);
    }

    [Fact]
    public void IncompleteAbsenceIsUnknownButObservedNegativeParseIsEvidence()
    {
        var stopped = Facts([Form1], [], [Assertion(Form1, true)]) with { After = Evidence([], true) };
        Assert.Empty(ReviewCaseClassifier.Compare(stopped).Flags);
        var negative = Facts([], [Form1], [Negative()]) with { After = Evidence([Form1], true) };
        Assert.True(ReviewCaseClassifier.Compare(negative).NewlyAcceptedNegative);
    }

    [Fact]
    public void DisapprovedReadingDoesNotBanWholeWord()
    {
        var disapproved = Assertion(Form1, false) with { Eligible = false };
        Assert.False(ReviewCaseClassifier.Compare(Facts([], [Form1], [disapproved])).NewlyAcceptedNegative);
    }

    [Fact]
    public void CandidateCannotConfirmItsOwnReading()
    {
        var candidate = Assertion(Form1, false) with { Eligible = false };
        Assert.DoesNotContain(ReviewFlagId.ConfirmedReadingsLost,
            ReviewCaseClassifier.Compare(Facts([Form1], [], [candidate])).Flags);
    }

    [Fact]
    public void MissingCaseIdentityCannotCreateAnEvent()
    {
        var result = ReviewCaseClassifier.Compare(Facts([Form1], [], [Assertion(Form1, true)]) with { IdentityAvailable = false });
        Assert.Empty(result.Flags);
        Assert.Contains("case-identity-unavailable", result.Limitations);
    }

    [Fact]
    public void OneReadingConfirmedByTwoAuthoritiesIsOneLoss()
    {
        var result = ReviewCaseClassifier.Compare(Facts([Form1], [], [Assertion(Form1, true), Assertion(Form1, false)]));
        var loss = Assert.Single(result.ReadingLosses);
        Assert.True(loss.Approved);
        Assert.True(loss.ReferenceConfirmed);
    }

    private static ReviewCaseFacts Facts(string[] before, string[] after, ReviewAssertion[] assertions) =>
        new("case", "word", "word", "ws", Evidence(before), Evidence(after), assertions,
            assertions.Where(a => !a.Approved && !a.WholeWordNegative && a.Eligible)
                .Select(a => new ReviewMembership("references", "case", "reference", 1, null, null, [a.Id]))
                .Concat(assertions.Any(a => a.WholeWordNegative)
                    ? [new ReviewMembership("negative-set", "case", "negative", 1, null, null, ["negative"])] : [])
                .ToArray()) { IdentityAvailable = true };

    private static ParseWordEvidence Evidence(string[] forms, bool capped = false) =>
        new("fieldworks-parse-analysis/v1", 0, "word", 0, capped, false, false,
            forms.Select(form => new ParseAnalysis([new ParseMorph(form, Msa, null, null)])).ToArray(), []);

    private static ReviewAssertion Assertion(string form, bool approved) => new(form, form,
        new ApprovedMorphology([new ApprovedMorph(form, Msa, null, [])]),
        approved ? ReferenceAuthority.Human : ReferenceAuthority.SourceValidated,
        "actor", "revision", approved, false, true, []);

    private static ReviewAssertion Negative() => new("negative", "", null, ReferenceAuthority.Human,
        "actor", "revision", false, true, true, []);
}
