using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins how the Proposals inspector shows the Parsimony status and that it leaves Try again in the first view.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalParsimonyPanelTests
{
    private const string Selected = ProposalsCaptureFixture.SelectedProposalId;
    private const string CanTry = "proposal/reduplicated-forms";

    private static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window, FakeCommandClient Fake)> OpenAsync(
        ProposalParsimonyStatus? status = null, string? choose = null)
    {
        var fake = new FakeCommandClient();
        ProposalsCaptureFixture.Configure(fake);
        if (status is not null) fake.ProposalParsimonyStatusIs(status);
        var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
        workspace.CurrentPage = WorkspacePage.Proposals;
        LayoutInvariantFixtures.Apply(window, new LayoutCell(1280, 720, 100));
        PageScreenshots.Settle(window);
        var page = workspace.PageModel<ProposalsPageModel>();
        if (choose is not null)
        {
            page.SelectProposalCommand.Execute(page.Groups.SelectMany(group => group.Cards)
                .Single(card => card.ProposalId == choose));
            // A queued or running job keeps its watch open, so wait for the first status, not for the watch to end.
            if (status is not null)
                await Wait.UntilAsync(() => page.Parsimony.Status is not null, "The Parsimony section never showed a status.");
            PageScreenshots.Settle(window);
        }
        return (workspace, window, fake);
    }

    private static ProposalParsimonyPanel Panel(Control root) =>
        Assert.Single(root.GetVisualDescendants().OfType<ProposalParsimonyPanel>());

    private static IEnumerable<string> Texts(Control root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible)
            .Select(block => block.Text ?? string.Empty);

    public static TheoryData<string, string> States => new()
    {
        { "Not Set Up", "not-set-up" }, { "Not Yet Run", "not-yet-run" }, { "Queued", "queued" },
        { "Running", "running" }, { "Ready", "ready" }, { "Out of Date", "stale" }, { "Did Not Finish", "failed" },
    };

    private static ProposalParsimonyStatus StatusFor(string name, string id) => name switch
    {
        "not-set-up" => ProposalParsimonyStatusFixtures.NotSetUp(id),
        "not-yet-run" => ProposalParsimonyStatusFixtures.NotYetRun(id),
        "queued" => ProposalParsimonyStatusFixtures.Waiting(id),
        "running" => ProposalParsimonyStatusFixtures.Running(id),
        "ready" => ProposalParsimonyStatusFixtures.Ready(id),
        "stale" => ProposalParsimonyStatusFixtures.Stale(id, ProposalParsimonyStaleReason.BaselineChanged),
        _ => ProposalParsimonyStatusFixtures.Failed(id, ProposalParsimonyFailureCategory.TimedOut,
            code: "status-skipped-timeout"),
    };

    [Theory]
    [MemberData(nameof(States))]
    public void EachStateReadsAsItselfInTheInspector(string label, string name)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = await OpenAsync(StatusFor(name, Selected), Selected);
            try
            {
                var panel = Panel(window);
                Assert.True(panel.IsEffectivelyVisible);
                Assert.Equal(label, panel.FindControl<TextBlock>("ParsimonyState")!.Text);
                Assert.Contains("Parsimony", Texts(panel));
                Assert.Contains("Grammar Group", Texts(panel));
                Assert.Contains("Text Group", Texts(panel));
            }
            finally { Close(workspace, window); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ReadyShowsEachMeasureCountsAndNoScore()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = await OpenAsync(ProposalParsimonyStatusFixtures.Ready(Selected), Selected);
            try
            {
                var panel = Panel(window);
                foreach (var expander in panel.GetVisualDescendants().OfType<Expander>()) expander.IsExpanded = true;
                PageScreenshots.Settle(window);
                var texts = Texts(panel).ToArray();
                Assert.Contains(texts, text => text.Contains("Repeated ad hoc restrictions: 2 new, 1 resolved, 5 unchanged"));
                Assert.Contains(texts, text => text.Contains("Environment may be too broad: 0 new"));
                Assert.Contains("Counts are per measure and are not added together.", texts);
                Assert.DoesNotContain(texts, text => text.Contains("total", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(texts, text => text.Contains(" ms", StringComparison.Ordinal));
            }
            finally { Close(workspace, window); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ThePendingDraftShowsNoSection()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, fake) = await OpenAsync(choose: "pending/one");
            try
            {
                Assert.False(Panel(window).IsEffectivelyVisible);
                Assert.DoesNotContain(fake.ProposalParsimonyStatusRequests, request => request.ProposalId == "pending/one");
            }
            finally { Close(workspace, window); }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [MemberData(nameof(States))]
    public void TheSectionNeverMovesTryAgainOrOpenInReviewAt720(string label, string name)
    {
        _ = label;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = await OpenAsync(StatusFor(name, CanTry), CanTry);
            try
            {
                var page = workspace.PageModel<ProposalsPageModel>();
                var shown = (Try: Box(Find(window, page.SelectedTryAgainAutomationId), window),
                    Open: Box(Find(window, page.SelectedOpenAutomationId), window));
                page.Parsimony.Stop();
                PageScreenshots.Settle(window);
                var without = (Try: Box(Find(window, page.SelectedTryAgainAutomationId), window),
                    Open: Box(Find(window, page.SelectedOpenAutomationId), window));

                Assert.Equal(without.Try.Top, shown.Try.Top);
                Assert.Equal(without.Open.Top, shown.Open.Top);
            }
            finally { Close(workspace, window); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheSectionScrollsWithTheDetailsWhileTheActionsStayPinnedBelowIt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = await OpenAsync(ProposalParsimonyStatusFixtures.Ready(CanTry), CanTry);
            try
            {
                var page = workspace.PageModel<ProposalsPageModel>();
                var tryAgain = Find(window, page.SelectedTryAgainAutomationId);
                var body = Panel(window).FindAncestorOfType<ScrollViewer>()!;
                Assert.DoesNotContain(body, tryAgain.GetVisualAncestors());
                Assert.True(Box(tryAgain, window).Top >= Box(body, window).Bottom - 1);
                Assert.True(body.Extent.Height > body.Viewport.Height);
            }
            finally { Close(workspace, window); }
        }, TimeSpan.FromMinutes(3));
    }

    private static Button Find(Control window, string? automationId) =>
        Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible &&
            Avalonia.Automation.AutomationProperties.GetAutomationId(button) == automationId);

    [ScreenshotFact]
    public void CaptureReadyRunningAndFailed()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { "ready", "running", "failed" })
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
                var (workspace, window, _) = await OpenAsync(StatusFor(name, CanTry), CanTry);
                try
                {
                    var panel = Panel(window);
                    if (name == "ready")
                        foreach (var expander in panel.GetVisualDescendants().OfType<Expander>().Take(1))
                            expander.IsExpanded = true;
                    PageScreenshots.Settle(window);
                    panel.BringIntoView();
                    PageScreenshots.Settle(window);
                    ReviewScreenshots.SaveAll(window, folder, "proposal-parsimony-" + name);
                }
                finally
                {
                    Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                    Close(workspace, window);
                }
            }, TimeSpan.FromMinutes(3));
        }
    }

    private static void Close(WorkspaceShellViewModel workspace, MainWindow window)
    {
        workspace.PageModel<ProposalsPageModel>().Parsimony.Stop();
        window.Close();
    }

    private static Rect Box(Control control, Visual window) =>
        new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
}
