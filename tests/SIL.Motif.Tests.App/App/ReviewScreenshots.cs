using SIL.Motif.App;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.App.Walkthrough;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Shared sample data and window setup for Proposal presentation checks.</summary>
internal static class ReviewScreenshots
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ProjectPath = @"C:\Users\linguist\FieldWorks\Projects\Sample\Sample.fwdata";
    private static readonly Guid Story = Guid.Parse("11111111-0000-0000-0000-000000000001");

    internal static RetirementProposalReviewProjection RetirementProjection(bool includeUnresolved)
    {
        var dryRun = new DryRunProjection("draft/one", "intent/one", "Baseline",
            [new("bundle/one", "analysis/wfiMorphBundle/morph", [new("qaa", "old", "new")], null)
            {
                OperationIds = ["operation/bundle"],
            }, new("bundle/one", "analysis/wfiMorphBundle/form", [new("qaa", "ta", "ra")], null)
            {
                OperationIds = ["operation/bundle"],
            }], "effects/one", "footprint/one")
        {
            Operations =
            [
                new("operation/class", "grammar/phNaturalClass/setName", null, null, [], "{\"ws\":\"qaa\",\"text\":\"Vowels\"}"),
                new("operation/rule", "grammar/phSegmentRule/setDisabled", null, null,
                    ["operation/class"], "{\"value\":false}"),
                new("operation/bundle", "analysis/wfiMorphBundle/setMorph", null, null,
                    ["operation/rule"], null),
                new("operation/reference", "grammar/moAlloAdhocProhib/retargetReferences", null, null,
                    ["operation/rule"], null),
                new("operation/delete", "lexical/lexEntry/deleteAlternateForm", null, null,
                    ["operation/bundle", "operation/reference"], null),
            ],
        };
        var statistics = new RetirementReviewStatistics(
            new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), 1,
            [new(true, true, 1, 1), new(true, false, 0, 0),
                new(false, true, 0, 0), new(false, false, 0, 0)],
            0, 1, 1, 0, new("finding/one", 1, 1, 0, 1, Digest, Digest, true), Digest);
        var details = new RetirementReviewDetail("bundle/one", "bundle-morph",
            "WfiMorphBundle.Morph", "wordform/one", "analysis/one", "bundle/one", null,
            "approved", "Morph", 0, "qaa: ta", "qaa: ra");
        return new RetirementProposalReviewProjection("draft/one", "intent/one", dryRun, statistics,
            [new("rule", "The new rule and sound class", ["operation/class", "operation/rule"], [])
                { Details = [new("rule/one", "rule-component", "New sound rule: r becomes t before a vowel", null, null, null,
                    "rule/one", null, null, null, null, null)] },
             new("bundle", "Analyses and bundle text", ["operation/bundle"], [.. dryRun.Effects])
                { Details = [details] },
             new("other-references", "Other references", ["operation/reference"], [])
                { Details = [new("source/one", "adhoc-single", "Restriction moved to the remaining form", null,
                    null, null, null, null, null, null, null, null)] },
             new("retired-form", "Deleted forms", ["operation/delete"], [])
                { Details = [new("form/retired", "retired-form", "Deleted form: -ta (alternate of -ra)", null, null,
                    null, null, null, null, null, null, null)] }],
            [new("case/one", "reading/one", "wordform/one", "ata", "qaa", "ata", "ata",
                "approved", "A", "preserved", true, true, true, "traced", "rule/one", Digest, null)],
            new("finding/one", 1, 1, 0, 1, Digest, Digest, true, false),
            includeUnresolved
                ? new(1, 1, 1, 0, [new("source/one", "other-native", "WfiAnalysis.PartOfSpeech",
                    "wordform/one", "analysis/one", null, null, "approved", "PartOfSpeech", null, null, null)])
                    { Message = "Declare every replacement destination." }
                : new(0, 0, 0, 0, [])
                { Message = string.Empty },
            []);
    }

    internal static void SaveAll(MainWindow window, string folder, string name)
    {
        window.Height = 780;
        PageScreenshots.SaveAll(window, folder, name);
    }

    internal static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenAsync(FakeCommandClient fake)
    {
        var clock = PageScreenshots.NewCaptureClock();
        fake.KnownProjectsListIs([new KnownProjectSummary(ProjectPath, PageScreenshots.CaptureStartAt)]);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token(), PageScreenshots.BaselineSaveAt, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(Story, "Hadithi ya sungura")], HasBaseline: true));
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(fake, new Picker()), new BaselineViewModel(fake),
            selection, new AssessViewModel(fake, selection), new Folder(), new Drag(), fake,
            clock, techDemoNotice: PageScreenshots.FirstRunNotice());
        var window = new MainWindow();
        window.Compose(workspace);
        workspace.Context.Diagnostics = true;
        window.Show();
        await workspace.SetProjectAsync(ProjectPath);
        workspace.Context.Setup?.SkipCommand.Execute(null);
        workspace.Context.RegisterOccurrenceLocationProvider(occurrence =>
            new TextOccurrenceLocation(0, occurrence.Index, occurrence.Index,
                $"Hadithi ya sungura, line {occurrence.Index + 1}"));
        await workspace.Context.Changes.ReloadAsync();
        return (workspace, window);
    }

    internal static PendingChangesSnapshot Blocked()
    {
        var kitabu = Change("kitabu", ChangeKinds.Approve, "analysis/kitabu", Stored(("ki-", "7"), ("tabu", "book")));
        var anapenda = Change("anapenda", ChangeKinds.Approve, "analysis/anapenda",
            Stored(("a-", "3SG"), ("na-", "PRS"), ("pend", "love"), ("-a", "FV"))) with
        {
            Occurrence = new OccurrenceAnchor(Story, Story, Story, 2),
        };
        var chakula = Change("chakula", ChangeKinds.Approve, null,
            new ReviewAnalysis(Reading(("ch-", "7"), ("akula", "food")), ReadingGrade.NoOpinion, true, false));
        var watoto = Change("watoto", ChangeKinds.Candidate, "analysis/watoto",
            new ReviewAnalysis(Reading(("wa-", "2"), ("toto", "child")), ReadingGrade.Approved, true, true));
        var uncertain = new ChangeFit("watoto", ChangeFitStatus.Uncertain, ["The words in the source sentence have changed."])
        {
            Uncertainty = new ChangeUncertainty("The words in the source sentence have changed.",
                [Token(0, "watoto"), Token(1, "hawajafika"), Token(2, "mwalimu")],
                [Token(0, "watoto"), Token(1, "hawajaja"), Token(2, "mwalimu")]),
        };
        return new PendingChangesSnapshot("draft/one", "revision/one", [kitabu, anapenda, chakula, watoto],
        [
            new ChangeFit("kitabu", false, ["Analysis analysis/kitabu was deleted or moved from wordform wordform/kitabu."]),
            new ChangeFit("anapenda", true, []),
            new ChangeFit("chakula", true, []),
            uncertain,
        ]);
    }

    private static OccurrenceWordToken Token(int index, string form) => new(index, "wordform/" + form, form);

    internal static PendingChange Change(string word, string kind, string? storedAnalysisId, ReviewAnalysis analysis) =>
        new(word, "wordform/" + word, word, kind, "assessment/one", word, ["operation/" + word])
        {
            Analyses = [analysis],
            OriginPage = WorkspacePage.Texts.ToString(),
            StoredAnalysisId = storedAnalysisId,
        };

    internal static ReviewAnalysis Stored(params (string Form, string Gloss)[] morphs) =>
        new(Reading(morphs), ReadingGrade.Candidate, true, true);

    internal static ParserReading Reading(params (string Form, string Gloss)[] morphs) =>
        new(morphs.Select(morph => new ParserReadingMorph(morph.Form, morph.Gloss, "n", null, false, null)).ToArray());

    internal static BaselineToken Token() =>
        new("project-1", "sha256:" + new string('a', 64), "1", "2026-09-22T10:00:00Z", "sha256:" + new string('b', 64));

    private sealed class Picker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(ProjectPath);
    }

    private sealed class Folder : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class Drag : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
