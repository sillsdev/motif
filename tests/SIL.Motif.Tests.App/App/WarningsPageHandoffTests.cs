using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins the warning page's selected words and attribution through the Handoff request.</summary>
public sealed class WarningsPageHandoffTests
{
    private const string ProjectPath = "/tmp/warnings-page-handoff.fwdata";
    private const string OutputDirectory = "/tmp/warnings-page-handoff-output";
    private const string SubjectGuid = "66666666-6666-6666-6666-666666666666";

    [Fact]
    public Task ExactUsesReachTheWarningScopedHandoffRequest() => AssertRequest(
        Warning(WarningWordsMatch.Identity, [Word("exact-form")], new WarningReach(WarningWordsPath.Uses)),
        WarningDisplayState.ExactUses, ["exact-form"], assess: true);

    [Fact]
    public Task SpellingRowsReachTheWarningScopedHandoffAsCandidates() => AssertRequest(
        Warning(WarningWordsMatch.Spelling, [Word("candidate-form")], new WarningReach(WarningWordsPath.Spelling)),
        WarningDisplayState.SpellingCandidates, ["candidate-form"], assess: true);

    [Fact]
    public Task MembershipRowsReachTheWarningScopedHandoffAsCandidates() => AssertRequest(
        Warning(WarningWordsMatch.Membership, [Word("member-form")], new WarningReach(WarningWordsPath.Membership)),
        WarningDisplayState.MembershipCandidates, ["member-form"], assess: true);

    [Fact]
    public Task EmptyAttributionKeepsTheWarningScopeWithoutAnAssessment() => AssertRequest(
        Warning(WarningWordsMatch.Identity, [], new WarningReach(WarningWordsPath.Uses)),
        WarningDisplayState.NoneInSelection, [], assess: false);

    [Fact]
    public Task UnavailableAttributionKeepsTheWarningScopeWithoutAnAssessment() => AssertRequest(
        Warning(null, [], new WarningReach(WarningWordsPath.Uses)),
        WarningDisplayState.EvidenceUnavailable, [], assess: false);

    private static async Task AssertRequest(GrammarWarning warning, WarningDisplayState expectedState,
        IReadOnlyList<string> expectedWords, bool assess)
    {
        var fake = new FakeCommandClient();
        fake.HandoffRefusesWith(new Refusal("test.handoff-captured", FailureReason.Refused, "Captured request."));
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake));
        var warnings = new WarningsPageModel(context);
        var handoffPage = new AiHandoffPageModel(context);
        warnings.Grammar.Warnings.Load([warning]);
        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(warnings.Grammar.Warnings.Rows));

        warnings.Grammar.Warnings.HandOffCommand.Execute(row);
        handoffPage.Handoff.ProjectPath = ProjectPath;
        Assert.Null(handoffPage.Handoff.InvocationId);
        Assert.True(handoffPage.Handoff.RunCommand.CanExecute(null));
        if (expectedWords.Count == 0)
            Assert.Contains("No words were selected", handoffPage.Handoff.ChosenWordsText, StringComparison.Ordinal);

        await handoffPage.Handoff.RunCommand.ExecuteAsync(null);

        var request = Assert.Single(fake.HandoffRequests);
        Assert.Equal(expectedWords, request.Selection.Words);
        Assert.Equal(assess, request.Assess);
        Assert.Null(request.InvocationId);
        Assert.NotNull(request.WarningScope);
        Assert.Equal(expectedState, request.WarningScope.State);
        Assert.Equal(warning.Code, request.WarningScope.Code);
        Assert.Equal(warning.Description, request.WarningScope.Message);
        Assert.False(request.WarningScope.HasUnfollowedConnections);
    }

    private static GrammarWarning Warning(WarningWordsMatch? match, ObjectUseWord[] words, WarningReach reach)
    {
        var subject = new GrammarWarningPart("named form", GrammarWarningPartRole.Object, SubjectGuid,
            "MoForm", "silfw://localhost/link?tool=LexEntryEdit")
        {
            SubjectGuid = SubjectGuid,
            Reach = reach,
        };
        return new GrammarWarning(GrammarDiagnosticLevel.Warning, "Parser description",
            [subject], [], "warning: warning.scoped")
        {
            Group = "PanGloss warning title",
            Code = "warning.scoped",
            Description = "PanGloss's full warning description.",
            YourWords = match is { } value
                ? new WarningWords(value, words, []) { Paths = [reach.Path] }
                : null,
        };
    }

    private static ObjectUseWord Word(string form) => new(
        new WordRow(form, WordRowOutcome.NoParse, "Lost", WordRowTone.Problem));

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(OutputDirectory);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths,
            DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
