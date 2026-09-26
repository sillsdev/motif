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

public sealed class ReviewNumbersCommandTests
{
    [Fact]
    public void ComparingReviewNumbersUsesOnlySharedWords()
    {
        var before = Record("before", [CorrectnessFixture.Word("cat", false),
            CorrectnessFixture.Word("dog", true)]);
        var after = Record("after", [CorrectnessFixture.Word("cat", true)]);

        var numbers = ReviewNumbersCommand.Summarize(before, after, 1);

        Assert.True(numbers.EvidenceComplete);
        Assert.Equal(ReviewComparability.Compared, numbers.Comparability);
        Assert.Equal(1, numbers.SharedWordCount);
        Assert.Equal((0, 1), (numbers.ApprovedKeptBefore, numbers.ApprovedKeptAfter));
    }

    [Fact]
    public void ReviewNumbersNameEveryWordThatLostAnApprovedAnalysis()
    {
        var before = Record("before", [CorrectnessFixture.Word("cat", true), CorrectnessFixture.Word("dog", true),
            CorrectnessFixture.Word("fish", false)]);
        var after = Record("after", [CorrectnessFixture.Word("cat", false), CorrectnessFixture.Word("dog", true),
            CorrectnessFixture.Word("fish", false)]);

        var numbers = ReviewNumbersCommand.Summarize(before, after, 3);

        Assert.Equal(["cat"], numbers.WordsLosingApprovedAnalysis);
    }

    [Fact]
    public void WithoutAnEarlierAssessmentNoWordIsSaidToLoseAnApprovedAnalysis()
    {
        var after = Record("after", [CorrectnessFixture.Word("cat", false)]);

        var numbers = ReviewNumbersCommand.Summarize(null, after, 1);

        Assert.Empty(numbers.WordsLosingApprovedAnalysis);
    }

    [Fact]
    public void DisjointReviewNumbersReportThatTheyCannotBeCompared()
    {
        var before = Record("before", [CorrectnessFixture.Word("dog", true)]);
        var after = Record("after", [CorrectnessFixture.Word("cat", true)]);

        var numbers = ReviewNumbersCommand.Summarize(before, after, 1);

        Assert.Equal(ReviewComparability.NoSharedWords, numbers.Comparability);
    }

    private static AssessmentRecord Record(string id, IReadOnlyList<AssessedWord> words)
    {
        var selection = Selection.Create("review", words.Select(word => word.Word));
        var scope = ScopeCodec.Write(new StoredScope.Trial("words", selection.Words,
            [AssessmentKind.Correctness], TimeSpan.FromSeconds(1), StepCap.Default));
        return new AssessmentRecord(id, null, null, "pangloss", AssessmentKind.Correctness.ToStoredKind(),
            scope, "scope", "whitespace", "1", "baseline", selection, null, null, "grammar", null,
            null, null, "2026-09-24T12:00:00Z", Words: words);
    }
}
