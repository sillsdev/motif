using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Corpus;

public class AssessmentComparisonNormalizationTests
{
    private const string Composed = "\u0BAA\u0BCA\u0BA9\u0BCD";
    private const string Decomposed = "\u0BAA\u0BC6\u0BBE\u0BA9\u0BCD";

    [Fact]
    public void AWordStoredComposedOnOneSideAndDecomposedOnTheOtherIsOneSharedWord()
    {
        var from = new ComparableAssessment("assessment/from", "pangloss", "Correctness", "ws", "1",
            [new AssessedWord(Decomposed, "notAnalysed", [])]);
        var to = new ComparableAssessment("assessment/to", "pangloss", "Correctness", "ws", "1",
            [new AssessedWord(Composed, "analysed", [new ParsedAnalysis("cat", ["root"], 0, "sha256:x")])]);

        var comparison = AssessmentComparer.Compare(from, to);

        Assert.Single(comparison.SharedWords);
        var change = Assert.Single(comparison.Changes);
        Assert.Equal(WordChangeKind.GainedAnalysis, change.Kind);
        Assert.Equal(Decomposed, change.Word);
    }

    [Fact]
    public void TheSharedWordIsTheSameWhicheverSideStoredItComposed()
    {
        var composedFirst = new ComparableAssessment("assessment/from", "pangloss", "Correctness", "ws", "1",
            [new AssessedWord(Composed, "notAnalysed", [])]);
        var decomposedSecond = new ComparableAssessment("assessment/to", "pangloss", "Correctness", "ws", "1",
            [new AssessedWord(Decomposed, "notAnalysed", [])]);

        var comparison = AssessmentComparer.Compare(composedFirst, decomposedSecond);

        Assert.Equal([Decomposed], comparison.SharedWords);
        Assert.Empty(comparison.Changes);
    }

    [Fact]
    public void AnAssessmentHoldingBothSpellingsOfOneWordIsRefusedAsAmbiguous()
    {
        var both = new ComparableAssessment("assessment/from", "pangloss", "Correctness", "ws", "1",
            [new AssessedWord(Composed, "notAnalysed", []), new AssessedWord(Decomposed, "notAnalysed", [])]);
        var other = new ComparableAssessment("assessment/to", "pangloss", "Correctness", "ws", "1",
            [new AssessedWord(Decomposed, "notAnalysed", [])]);

        var refusal = Assert.Throws<AmbiguousWordCasesException>(() => AssessmentComparer.Compare(both, other));
        Assert.Contains("Run the Trial again", refusal.Message);
    }
}
