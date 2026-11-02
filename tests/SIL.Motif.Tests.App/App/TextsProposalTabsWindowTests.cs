using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Texts Lists and What Changed tabs in a real window while a Proposal is open: the chips read the pair and
/// the Proposal's alone, What Changed shows the moves in the pair and a note alone in the Proposal view, and nothing
/// is read for a flip. It also saves the pair and Proposal views of both tabs as images, in Light and Dark.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsProposalTabsWindowTests
{
    private const string ProposalId = "proposal/sound";

    // The same words as today's, with the last four that the Proposal no longer parses.
    private static TrialRecord Trial(AssessCommandResponse today) => new("job/trial", ProposalId, "sha256:intent",
        PageScreenshots.Token(), null, new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "trial-root" },
        [.. today.Words.Select(word => word.Word)], false, null, JobStatus.Completed,
        new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero), TrialFreshness.Current)
    {
        Label = "Sound change",
        WordResults = [.. today.Words.Select((word, index) => index >= today.Words.Count - 4
            ? word with { Outcome = "unassessed", Morphology = null, ReadingGrades = [] } : word)],
    };

    internal static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenedAsync(TextsTab tab)
    {
        var today = PageScreenshots.Assessment();
        var (workspace, window) = await PageScreenshots.OpenOverSampleData(
            configure: (fake, _) => fake.ReadTrialCompletesWith(Trial(today)), capturedAssessment: today);
        workspace.CurrentPage = WorkspacePage.Texts;
        workspace.PageModel<TextsPageModel>().Tab = TextsTab.Matrix;
        await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
        for (var wait = 0; wait < 100 && workspace.Assess.Compare.ProposalWordsMatch is null; wait++)
        {
            await Task.Delay(20);
            PageScreenshots.Settle(window);
        }
        Assert.True(workspace.Assess.Compare.ProposalWordsMatch);
        workspace.PageModel<TextsPageModel>().Tab = tab;
        PageScreenshots.Settle(window);
        return (workspace, window);
    }

    internal static Control Part(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<Control>().First(control => control.Name == name);

    internal static IEnumerable<string> Texts(Control root) => root.GetVisualDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text)).Select(text => text.Text!);

    [Fact]
    public void TheListChipsAndHeaderReadThePairAndTheProposalsAloneAndAFlipReadsNothing()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(TextsTab.Lists);
            try
            {
                var compare = workspace.Assess.Compare;
                var lists = workspace.PageModel<TextsPageModel>().TextsLists;
                workspace.Context.ShowProposalView(MarkupView.TodayToProposal);
                PageScreenshots.Settle(window);
                var pairChip = Part(window, "TextsListsPanelListsItems");
                Assert.Contains(Texts(pairChip), text => text.Contains('→'));
                var work = compare.ProposalWork;

                workspace.Context.ShowProposalView(MarkupView.Proposal);
                PageScreenshots.Settle(window);
                Assert.DoesNotContain(Texts(Part(window, "TextsListsPanelListsItems")), text => text.Contains('→'));
                workspace.Context.ShowProposalView(MarkupView.Today);
                PageScreenshots.Settle(window);
                workspace.Context.ShowProposalView(MarkupView.TodayToProposal);
                PageScreenshots.Settle(window);

                Assert.Equal(work, compare.ProposalWork);
                Assert.Contains(lists.Lists, list => list.ChipCount.Contains('→'));
                Assert.True(Part(window, "ListsHost").IsEffectivelyVisible);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void WhatChangedListsTheSameMovesInThePairAndTheProposalViewAndTheTwoRunsInToday()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(TextsTab.WhatChanged);
            try
            {
                var page = workspace.PageModel<TextsPageModel>();
                workspace.Context.ShowProposalView(MarkupView.TodayToProposal);
                PageScreenshots.Settle(window);
                Assert.True(Part(window, "ProposalMovesHost").IsEffectivelyVisible);
                Assert.False(Part(window, "DifferenceHost").IsEffectivelyVisible);
                Assert.True(page.ProposalChanges.MovedCount > 0);
                var pairMoves = Texts(Part(window, "ProposalMovesHost")).ToArray();
                Assert.NotEmpty(pairMoves);
                var sets = workspace.Assess.Compare.MoveSets;

                workspace.Context.ShowProposalView(MarkupView.Proposal);
                PageScreenshots.Settle(window);
                Assert.True(Part(window, "ProposalMovesHost").IsEffectivelyVisible);
                Assert.False(Part(window, "DifferenceHost").IsEffectivelyVisible);
                Assert.Equal(pairMoves, Texts(Part(window, "ProposalMovesHost")).ToArray());
                Assert.Equal(sets, workspace.Assess.Compare.MoveSets);
                Assert.Equal(MotifAppStrings.TextsWhatChangedViewNote, page.ProposalNote);
                Assert.Contains(Texts(window), text => text == MotifAppStrings.TextsWhatChangedViewNote);

                workspace.Context.ShowProposalView(MarkupView.Today);
                PageScreenshots.Settle(window);
                Assert.True(Part(window, "DifferenceHost").IsEffectivelyVisible);
                Assert.False(Part(window, "ProposalMovesHost").IsEffectivelyVisible);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void CaptureTheListsAndWhatChangedTabsInThePairAndTheProposalViewInLightAndDark()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(TextsTab.Lists);
            try
            {
                var page = workspace.PageModel<TextsPageModel>();
                var views = new[] { ("today", MarkupView.Today), ("pair", MarkupView.TodayToProposal), ("proposal", MarkupView.Proposal) };
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    window.Width = 1240;
                    window.Height = 1000;
                    foreach (var (tab, tabName) in new[] { (TextsTab.Lists, "lists"), (TextsTab.WhatChanged, "whatchanged") })
                    {
                        page.Tab = tab;
                        if (tab == TextsTab.Lists) page.TextsLists.SelectedList = page.TextsLists.Lists.First(list => list.HasWords);
                        foreach (var (name, view) in views)
                        {
                            workspace.Context.ShowProposalView(view);
                            PageScreenshots.Settle(window);
                            PageScreenshots.Save(window, Path.Combine(folder, $"texts-{tabName}-{name}-{theme}.png"));
                        }
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }
}
