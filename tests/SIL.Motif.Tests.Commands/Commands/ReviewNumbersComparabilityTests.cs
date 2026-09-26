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

    private static AssessmentRecord Record(string id, IReadOnlyList<AssessedWord> words, string assessor = "pangloss")
    {
        var selection = Selection.Create("review", words.Select(word => word.Word));
        var scope = ScopeCodec.Write(new StoredScope.Trial("words", selection.Words,
            [AssessmentKind.Correctness], TimeSpan.FromSeconds(1), StepCap.Default));
        return new AssessmentRecord(id, null, null, assessor, AssessmentKind.Correctness.ToStoredKind(),
            scope, "scope", "whitespace", "1", "baseline", selection, null, null, "grammar", null,
            null, null, "2026-09-24T12:00:00Z", Words: words);
    }
}
