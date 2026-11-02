using System.Linq;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Assess;

/// <summary>
/// Covers CONTEXT.md's Readiness rule directly, with hand-built <see cref="CorrectnessAssessment"/> values
/// and no project or database: no candidate; a stale baseline; a regression with the gate on and off; and
/// the all-clear case.
/// </summary>
public sealed class ReadinessTests
{
    private const string GrammarSha = "sha256:" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void AllApprovedMatchesDoNotMakeAnInterruptedWordReady()
    {
        var candidate = Build("candidate", ("alpha", true));
        var word = candidate.Words[0];
        candidate = candidate with { Words = [word with { Morphology = word.Morphology! with { Capped = true } }] };
        var reasons = Readiness.Assess(candidate, null, "old", "new", false);
        Assert.Contains(reasons, reason => reason.Contains("incomplete or unavailable for \"alpha\"") &&
            reason.Contains("work or time limit") && !reason.Contains("spaces"));
        Assert.Contains(reasons, reason => reason.Contains("different project state"));
    }

    [Fact]
    public void ACapitalisedWordThatCouldNotBeReadAsWrittenIsCoveredByItsCompleteLowercaseRow()
    {
        var candidate = Build("candidate", ("Alpha", true), ("alpha", true));
        var capital = candidate.Words[0] with { Morphology = candidate.Words[0].Morphology! with { InvalidShape = true, Analyses = [] } };
        var lower = candidate.Words[1];
        System.Func<string, string?> lowercase = word => word.ToLowerInvariant() == word ? null : word.ToLowerInvariant();
        Assert.Empty(Readiness.Assess(candidate with { Words = [capital, lower] }, null, null, "same", false, null, lowercase));

        var brokenLower = lower with { Morphology = lower.Morphology! with { Capped = true } };
        var reasons = Readiness.Assess(candidate with { Words = [capital, brokenLower] }, null, null, "same", false, null, lowercase);
        Assert.Contains(reasons, reason => reason.Contains("\"Alpha\""));
    }

    [Fact]
    public void AWordWithAnInvalidShapeIsToldToUseOneWordPerLine()
    {
        var candidate = Build("candidate", ("alpha beta", true));
        var word = candidate.Words[0];
        candidate = candidate with
        {
            Words = [word with { Morphology = word.Morphology! with { InvalidShape = true, Analyses = [] } }],
        };
        var reasons = Readiness.Assess(candidate, null, null, "same", false);
        Assert.Contains(reasons, reason => reason.Contains("\"alpha beta\"") && reason.Contains("one word per line"));
    }

    [Fact]
    public void AnUnprojectableExtraReadingDoesNotNegateEveryApprovedMatch()
    {
        var candidate = Build("candidate", ("alpha", true));
        var word = candidate.Words[0];
        candidate = candidate with
        {
            Words = [word with { Morphology = word.Morphology! with { Unavailable = ["runtime root identity missing"] } }],
        };
        Assert.Empty(Readiness.Assess(candidate, null, null, "same", false));
    }

    [Fact]
    public void NoCandidate_GivesTheSingleReasonAndStops()
    {
        var reasons = Readiness.Assess(
            candidate: null, current: null, currentBaselineToken: null,
            candidateBaselineToken: "baseline-1", gateOnRegression: true);

        var reason = Assert.Single(reasons);
        Assert.Contains("no Assessment covers its current content", reason);
    }

    [Fact]
    public void DifferentBaselineTokens_AreStale()
    {
        var current = Build("current", ("alpha", true));
        var candidate = Build("candidate", ("alpha", true));

        var reasons = Readiness.Assess(
            candidate, current, currentBaselineToken: "baseline-old",
            candidateBaselineToken: "baseline-new", gateOnRegression: true);

        Assert.Contains(reasons, reason => reason.Contains("not been re-run since the project moved"));
    }

    [Fact]
    public void RegressionWithGateOn_IsAReason()
    {
        var current = Build("current", ("alpha", true), ("beta", true));
        var candidate = Build("candidate", ("alpha", false), ("beta", true));

        var reasons = Readiness.Assess(
            candidate, current, currentBaselineToken: "baseline-1",
            candidateBaselineToken: "baseline-1", gateOnRegression: true);

        Assert.Contains(reasons, reason => reason.Contains("would be a regression"));
    }

    [Fact]
    public void RegressionWithGateOff_IsNotAReason()
    {
        var current = Build("current", ("alpha", true), ("beta", true));
        var candidate = Build("candidate", ("alpha", false), ("beta", true));

        var reasons = Readiness.Assess(
            candidate, current, currentBaselineToken: "baseline-1",
            candidateBaselineToken: "baseline-1", gateOnRegression: false);

        Assert.Empty(reasons);
    }

    [Fact]
    public void SameBaseline_NoRegression_IsAllClear()
    {
        var current = Build("current", ("alpha", true), ("beta", true));
        var candidate = Build("candidate", ("alpha", true), ("beta", true));

        var reasons = Readiness.Assess(
            candidate, current, currentBaselineToken: "baseline-1",
            candidateBaselineToken: "baseline-1", gateOnRegression: true);

        Assert.Empty(reasons);
    }

    [Fact]
    public void IntroducedDroppedGrammarRefusesAndNamesTheObject()
    {
        var candidate = Build("candidate", ("alpha", true));
        var dropped = new DroppedGrammar([new("grammar.environment.invalid",
            [new DroppedGrammarObject("id/1", "PhEnvironment", "env", null)], "dropped")], 0);

        var decision = Readiness.Evaluate(candidate, null, null, "same", false, droppedGrammar: dropped);

        Assert.True(decision.HasDroppedGrammar);
        Assert.False(decision.IsRegression);
        var reason = Assert.Single(decision.Reasons);
        Assert.Contains("grammar.environment.invalid on PhEnvironment env (id/1)", reason);
    }

    [Fact]
    public void OmittedFindingsAreCountedInTheReason()
    {
        var candidate = Build("candidate", ("alpha", true));
        var dropped = new DroppedGrammar([new("grammar.environment.invalid", [], "dropped")], 0)
        {
            OmittedFindings = 7,
        };

        var decision = Readiness.Evaluate(candidate, null, null, "same", false, droppedGrammar: dropped);

        Assert.Contains("and 7 more", Assert.Single(decision.Reasons));
    }

    [Fact]
    public void PreExistingDroppedGrammarDoesNotRefuse()
    {
        var candidate = Build("candidate", ("alpha", true));

        var decision = Readiness.Evaluate(candidate, null, null, "same", false, droppedGrammar: new DroppedGrammar([], 3));

        Assert.Empty(decision.Reasons);
        Assert.False(decision.HasDroppedGrammar);
    }

    [Fact]
    public void AnUncheckedDroppedGrammarResultRefusesOnlyWhenNothingElseDoes()
    {
        var candidate = Build("candidate", ("alpha", true));
        var unchecked_ = DroppedGrammar.NotChecked("no parser");

        var alone = Readiness.Evaluate(candidate, null, null, "same", false, droppedGrammar: unchecked_);
        var withStale = Readiness.Evaluate(candidate, null, "old", "new", false, droppedGrammar: unchecked_);

        Assert.Equal("dropped grammar was not checked (no parser); try the Proposal again", Assert.Single(alone.Reasons));
        Assert.False(alone.HasDroppedGrammar);
        Assert.DoesNotContain(withStale.Reasons, reason => reason.Contains("not checked"));
    }

    [Fact]
    public void NoDroppedGrammarJudgmentLeavesReadinessAsItWas()
    {
        var candidate = Build("candidate", ("alpha", true));

        Assert.Empty(Readiness.Evaluate(candidate, null, null, "same", false).Reasons);
    }

    private static CorrectnessAssessment Build(string assessmentId, params (string Word, bool Analysed)[] words)
    {
        var selection = Selection.Create("test", words.Select(w => w.Word));
        var assessedWords = words
            .Select(w => SIL.Motif.Tests.TestFixtures.CorrectnessFixture.Word(w.Word, w.Analysed))
            .ToArray();
        return new CorrectnessAssessment(
            assessmentId, "pangloss", "none", "1",
            new StoredScope.Trial("all", Array.Empty<string>(), Array.Empty<AssessmentKind>(),
                TimeSpan.FromSeconds(1), SIL.Motif.Contract.Assess.StepCap.Default),
            selection, GrammarSha, assessedWords);
    }
}
