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
        ProposalParsimonyStatus? status = null, string? choose = null, Action<FakeCommandClient>? configure = null)
    {
        var fake = new FakeCommandClient();
        ProposalsCaptureFixture.Configure(fake);
        configure?.Invoke(fake);
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
        var checks = Assert.Single(window.GetVisualDescendants().OfType<Expander>(),
            expander => expander.Header is TextBlock { Text: var text } && text == MotifAppStrings.ProposalOptionalChecks);
        checks.IsExpanded = true;
        PageScreenshots.Settle(window);
        return (workspace, window, fake);
    }

    private static ProposalParsimonyPanel Panel(Control root) =>
        Assert.Single(root.GetVisualDescendants().OfType<ProposalParsimonyPanel>());

    private static IEnumerable<string> Texts(Control root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible)
            .Select(block => block.Text ?? string.Empty);

    [Fact]
    public void ProposalWorkLabelAndValueBothFitInTheInspector()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = await OpenAsync(choose: Selected);
            try
            {
                var numbers = Assert.Single(window.GetVisualDescendants().OfType<ItemsControl>(),
                    control => control.Name == "ProposalsNumberItems");
                var label = Assert.Single(numbers.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == MotifAppStrings.ProposalWork &&
                        text.Parent is Grid { Classes: var classes } && classes.Contains("inspectorFact"));
                var row = Assert.IsType<Grid>(label.Parent);
                var value = Assert.Single(row.Children.OfType<TextBlock>(), text => text != label);
                var fullLabel = new TextBlock
                {
                    Text = label.Text, FontFamily = label.FontFamily, FontSize = label.FontSize,
                    FontWeight = label.FontWeight,
                };
                fullLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(label.Bounds.Width >= fullLabel.DesiredSize.Width - 1,
                    $"Work label has {label.Bounds.Width} pixels for {fullLabel.DesiredSize.Width} pixels of text.");
                Assert.True(Box(label, window).Right <= Box(value, window).Left + 1);
                Assert.Equal(Avalonia.Media.TextWrapping.Wrap, value.TextWrapping);
            }
            finally { Close(workspace, window); }
        }, TimeSpan.FromMinutes(3));
    }

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
    public void ReadyShowsTheReadTheFindingsWithTheirDescriptionsAndEvidenceOnRequest()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = await OpenAsync(ProposalParsimonyStatusFixtures.Ready(Selected), Selected,
                configure: ProposalParsimonyPresentationTests.ConfigureStoredReports);
            try
            {
                var page = workspace.PageModel<ProposalsPageModel>();
                await Wait.UntilAsync(() => page.Parsimony.Groups.Count > 0 && page.Parsimony.Groups[0].HasFindingsRead,
                    "The Findings were never read.");
                var panel = Panel(window);
                foreach (var expander in panel.GetVisualDescendants().OfType<Expander>()) expander.IsExpanded = true;
                PageScreenshots.Settle(window);
                var texts = Texts(panel).ToArray();
                Assert.Contains(texts, text => text.StartsWith("1 measure found new or changed Findings.", StringComparison.Ordinal));
                Assert.Contains("3 Findings to Look at", texts);
                Assert.Contains(texts, text => text == "Repeated ad hoc restrictions: Prohibition ma- + -tu repeats a slot");
                var ids = panel.GetVisualDescendants().OfType<Control>()
                    .Select(control => Avalonia.Automation.AutomationProperties.GetAutomationId(control)).ToHashSet();
                Assert.Superset(new HashSet<string?>
                {
                    "motif-proposal-parsimony-findings-grammar", "motif-proposal-parsimony-findings-list-grammar",
                    "motif-proposal-parsimony-findings-more-grammar", "motif-proposal-parsimony-judgments-grammar",
                    "motif-proposal-parsimony-evidence-text-f-new",
                }, ids);
                var read = panel.FindControl<Control>("ParsimonyRead")!;
                Assert.Equal(AutomationIds.ProposalParsimonyRead, Avalonia.Automation.AutomationProperties.GetAutomationId(read));
                var button = Assert.Single(panel.GetVisualDescendants().OfType<Button>(), candidate =>
                    candidate.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetAutomationId(candidate) ==
                    AutomationIds.ForProposalParsimonyEvidence("f-new"));
                await page.Parsimony.Groups[0].Findings.Single(item => item.FindingId == "f-new")
                    .ShowEvidenceCommand.ExecuteAsync(null);
                PageScreenshots.Settle(window);
                Assert.Contains(Texts(panel), text => text.Contains("Allomorph: not kept for this Proposal", StringComparison.Ordinal));
                Assert.False(button.IsEffectivelyVisible);
                Assert.DoesNotContain(Texts(panel), text => text.Contains(" ms", StringComparison.Ordinal));
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
