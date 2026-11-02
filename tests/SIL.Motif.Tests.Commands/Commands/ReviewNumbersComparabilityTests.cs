using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ReviewNumbersComparabilityTests
{
    [Fact]
    public void WithoutAnEarlierAssessmentOnlyTheTrialsOwnCountsAreReturned()
    {
        var trial = Record("trial", [CorrectnessFixture.Word("cat", true), CorrectnessFixture.Word("dog", false)]);

        var numbers = ReviewNumbersCommand.Summarize(null, trial, 2);

        Assert.Equal(new ReviewNumbersResponse(ReviewComparability.NoEarlierAssessment, 0, 0, 0, 2, 1, true),
            numbers);
    }

    [Fact]
    public void DifferentAssessorsAreNotCompared()
    {
        var before = Record("before", [CorrectnessFixture.Word("cat", false)], assessor: "another-parser");
        var trial = Record("trial", [CorrectnessFixture.Word("cat", true)]);

        var numbers = ReviewNumbersCommand.Summarize(before, trial, 1);

        Assert.Equal(new ReviewNumbersResponse(ReviewComparability.DifferentAssessor, 0, 0, 0, 1, 1, true),
            numbers);
    }

    [Fact]
    public void AssessmentsWithNoWordInCommonAreNotCompared()
    {
        var before = Record("before", [CorrectnessFixture.Word("dog", true)]);
        var trial = Record("trial", [CorrectnessFixture.Word("cat", false)]);

        var numbers = ReviewNumbersCommand.Summarize(before, trial, 1);

        Assert.Equal(new ReviewNumbersResponse(ReviewComparability.NoSharedWords, 0, 0, 0, 1, 0, true), numbers);
    }

    [Fact]
    public void ComparedAssessmentsCountApprovedAnalysesKeptOverSharedWordsOnly()
    {
        var before = Record("before", [CorrectnessFixture.Word("cat", false), CorrectnessFixture.Word("fish", true),
            CorrectnessFixture.Word("dog", true)]);
        var trial = Record("trial", [CorrectnessFixture.Word("cat", true), CorrectnessFixture.Word("fish", true),
            CorrectnessFixture.Word("bird", false)]);

        var numbers = ReviewNumbersCommand.Summarize(before, trial, 3);

        Assert.Equal(new ReviewNumbersResponse(ReviewComparability.Compared, 2, 1, 2, 3, 2, true), numbers);
    }

    [Fact]
    public void AMissingTouchedWordLeavesTheEvidenceIncomplete()
    {
        var trial = Record("trial", [CorrectnessFixture.Word("cat", true)]);

        var numbers = ReviewNumbersCommand.Summarize(null, trial, 2);

        Assert.False(numbers.EvidenceComplete);
        Assert.Equal(1, numbers.TouchedWordCount);
    }

    [Fact]
    public void AWordStoredComposedInAnOldSelectionIsStillSharedWithItsDecomposedSpelling()
    {
        const string composed = "\u0BAA\u0BCA\u0BA9\u0BCD";
        var before = Record("before", [CorrectnessFixture.Word(composed, false)], keepWordsAsStored: true);
        var trial = Record("trial", [CorrectnessFixture.Word(composed.Normalize(System.Text.NormalizationForm.FormD), true)]);

        var numbers = ReviewNumbersCommand.Summarize(before, trial, 1);

        Assert.Equal(ReviewComparability.Compared, numbers.Comparability);
        Assert.Equal(1, numbers.SharedWordCount);
    }

    [Fact]
    public void AnAssessmentHoldingBothSpellingsOfOneWordCannotBeSummarisedAgainstAnother()
    {
        const string composed = "\u0BAA\u0BCA\u0BA9\u0BCD";
        var decomposed = composed.Normalize(System.Text.NormalizationForm.FormD);
        var trial = Record("trial", [CorrectnessFixture.Word(composed, true), CorrectnessFixture.Word(decomposed, true)],
            keepWordsAsStored: true);
        var before = Record("before", [CorrectnessFixture.Word(decomposed, false)]);

        var refusal = Assert.Throws<AmbiguousWordCasesException>(() => ReviewNumbersCommand.Summarize(before, trial, 2));

        Assert.Contains("Run the Trial again", refusal.Message);
    }

    internal static AssessmentRecord Record(string id, IReadOnlyList<AssessedWord> words, string assessor = "pangloss",
        bool keepWordsAsStored = false)
    {
        var selection = keepWordsAsStored
            ? new Selection("review", words.Select(word => word.Word).Distinct().Order(StringComparer.Ordinal).ToArray(), "stored")
            : Selection.Create("review", words.Select(word => word.Word));
        var scope = ScopeCodec.Write(new StoredScope.Trial("words", selection.Words,
            [AssessmentKind.Correctness], TimeSpan.FromSeconds(1), ParserWorkLimit.Default));
        return new AssessmentRecord(
            AssessmentId: id,
            ProposalId: null,
            ProposalIntentDigest: null,
            Assessor: assessor,
            Kind: AssessmentKind.Correctness.ToStoredKind(),
            ScopeJson: scope,
            ScopeDigest: "scope",
            Subject: new AssessmentSubject.Words(selection, "whitespace", "1"),
            BaselineToken: "baseline",
            OutcomeDigest: null,
            SemanticDigest: null,
            GrammarSourceSha256: "grammar",
            ModelFingerprint: null,
            Pipeline: null,
            DiagnosticCount: null,
            SavedUtc: "2026-09-24T12:00:00Z",
            Details: new AssessmentRecordDetails.Words(words, null, null, [])
        );
    }
}
