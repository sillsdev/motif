using SIL.Motif.Contract.Review;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Runtime.Review;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class ReviewFindingComparisonTests
{
    private static ReviewDiagnostic Warning(string code, string severity = "warning", string[]? subjects = null) =>
        new(null, "grammar", code, subjects ?? ["subject"], [], severity);
    private static ReviewFindingMeasure Measure(ReviewFinding[] findings) =>
        new("grammar", "measure", "v1", "v1", "universe", "findings", true, findings);
    private static ReviewFindingInputs Inputs(ReviewDiagnostic[] before, ReviewDiagnostic[] after) =>
        new(before, after, [], []);

    [Fact]
    public void GrammarWarningAdapterUsesStableSubjectsAndRejectsUnresolvedIdentity()
    {
        var subjectId = "11111111-1111-1111-1111-111111111111";
        var subject = new GrammarWarningPart("localized label", GrammarWarningPartRole.Object) { SubjectGuid = subjectId };
        var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "label", [subject], [], "display") { Code = "code" };
        var mapped = ReviewDiagnosticIdentity.FromGrammarWarning(warning);
        Assert.True(ReviewDiagnosticIdentity.TryCreate(mapped, out var first));
        var translated = warning with { CodeLabel = "other", Text = "other", Subject = [subject with { Text = "other" }] };
        Assert.True(ReviewDiagnosticIdentity.TryCreate(ReviewDiagnosticIdentity.FromGrammarWarning(translated), out var second));
        Assert.Equal(first, second);
        var unresolved = warning with { Subject = [subject with { SubjectGuid = null }] };
        Assert.False(ReviewDiagnosticIdentity.TryCreate(ReviewDiagnosticIdentity.FromGrammarWarning(unresolved), out _));
    }

    [Fact]
    public void ReaderObjectsWithoutIdentityCannotCreateWarningDifferences()
    {
        ReviewDiagnostic Read(string title) => ReviewDiagnosticIdentity.FromGrammarWarning(Assert.Single(
            GrammarHealthReportReader.ReadResponse(GrammarHealthReports.With(("code", [new("entry", title)]))).Findings));
        var first = Read("first");
        var second = Read("second");
        Assert.Null(first.SubjectIds);
        var changed = ReviewFindingComparison.Compare(Inputs([first], [second]));
        Assert.NotEmpty(changed.Limitations);
        Assert.Empty(changed.AddedWarnings);
        Assert.Empty(changed.RemovedWarnings);
        var added = ReviewFindingComparison.Compare(Inputs([], [second]));
        Assert.NotEmpty(added.Limitations);
        Assert.Empty(added.AddedWarnings);
    }

    [Fact]
    public void MissingOrMalformedRetainedEvidenceIsUnavailable()
    {
        Assert.Null(RetainedParsimonyReportReader.Read(null));
        Assert.Null(RetainedParsimonyReportReader.Read("{"));
    }

    [Fact]
    public void EqualTotalsWithDifferentWarningsStillFindsNewWarning()
    {
        var delta = ReviewFindingComparison.Compare(Inputs([Warning("old")], [Warning("new")]));
        Assert.Single(delta.AddedWarnings);
        Assert.Single(delta.RemovedWarnings);
    }

    [Fact]
    public void LabelLocaleAndOrderDoNotChangeIdentity()
    {
        var before = Warning("code", subjects: ["a", "b"]);
        var after = Warning("code", subjects: ["b", "a"]);
        Assert.Empty(ReviewFindingComparison.Compare(Inputs([before], [after])).AddedWarnings);
    }

    [Fact]
    public void InfoPromotionCountsAsNewWarning()
    {
        Assert.Single(ReviewFindingComparison.Compare(Inputs([Warning("code", "info")], [Warning("code")])).AddedWarnings);
    }

    [Fact]
    public void UnresolvedSubjectMakesComparisonPartial()
    {
        var diagnostic = Warning("code") with { SubjectIds = null };
        Assert.NotEmpty(ReviewFindingComparison.Compare(Inputs([], [diagnostic])).Limitations);
    }

    [Fact]
    public void UnresolvedBaselineCannotManufactureANewWarning()
    {
        var before = Warning("code") with { SubjectIds = null };
        var result = ReviewFindingComparison.Compare(Inputs([before], [Warning("code"), Warning("other")]));
        Assert.Single(result.AddedWarnings);
        Assert.NotEmpty(result.Limitations);
    }

    [Fact]
    public void NewParsimonyFindingCountsButChangedEvidenceDoesNot()
    {
        var input = Inputs([], []) with
        {
            BeforeParsimony = [Measure([new("same", "a", null)])],
            AfterParsimony = [Measure([new("same", "b", null), new("new", "x", null)])],
        };
        var measure = Assert.Single(ReviewFindingComparison.Compare(input).Measures);
        Assert.Equal(["new"], measure.Added);
        Assert.Equal(["same"], measure.EvidenceChanged);
    }

    [Fact]
    public void KeepDispositionDoesNotEraseNewFact()
    {
        var input = Inputs([], []) with { BeforeParsimony = [Measure([])], AfterParsimony = [Measure([new("new", "x", "keep")])] };
        Assert.Equal(["new"], Assert.Single(ReviewFindingComparison.Compare(input).Measures).Added);
    }

    [Fact]
    public void FailedDetectorIsIncompleteNotGrammarFailure()
    {
        var input = Inputs([], []) with { BeforeParsimony = [Measure([])], AfterParsimony = [Measure([]) with { Completed = false }] };
        var measure = Assert.Single(ReviewFindingComparison.Compare(input).Measures);
        Assert.Empty(measure.Added);
        Assert.NotNull(measure.Limitation);
    }

    [Fact]
    public void ChangedMeasureUniverseCannotMakeEverythingNew()
    {
        var input = Inputs([], []) with { BeforeParsimony = [Measure([])], AfterParsimony = [Measure([new("new", "x", null)]) with { UniverseDigest = "changed" }] };
        var measure = Assert.Single(ReviewFindingComparison.Compare(input).Measures);
        Assert.Empty(measure.Added);
        Assert.NotNull(measure.Limitation);
    }
}
