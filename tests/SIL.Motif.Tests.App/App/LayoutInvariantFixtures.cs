using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using SIL.Motif.Commands.Assistants;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

internal sealed record LayoutCell(int Width, int Height, int Zoom)
{
    internal string Name => $"{Width}x{Height}";
}

internal static class LayoutInvariantFixtures
{
    internal static IReadOnlyList<WorkspacePage> RegisteredPages { get; } =
    [
        WorkspacePage.Overview, WorkspacePage.Proposals, WorkspacePage.Texts, WorkspacePage.TryAWord,
        WorkspacePage.Timing, WorkspacePage.Warnings, WorkspacePage.Parsimony, WorkspacePage.Review,
        WorkspacePage.AiHandoff, WorkspacePage.AiAssistants, WorkspacePage.Settings,
    ];

    internal static IReadOnlyList<WorkspacePage> PopulatedPages { get; } =
        RegisteredPages;

    /// <summary>
    /// Returns the process-wide Application state that one case can change to what a fresh launch has: the default
    /// theme variant and 100% zoom. Cases that compose a window over sample data start from here, not from
    /// whatever the case before left behind.
    /// </summary>
    internal static void ResetSharedState()
    {
        var application = Application.Current!;
        application.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
        WindowZoomPolicy.Apply(application, ZoomPolicy.DefaultPercent);
    }

    /// <summary>Opens the sample-data window as <see cref="PageScreenshots.OpenOverSampleData"/> does, from reset shared state.</summary>
    internal static Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenFreshSampleData(
        Action<FakeCommandClient, AssessCommandResponse>? configure = null, bool renderForCapture = true,
        bool composeWindow = true, bool parse = true, bool seedReaderToken = true)
    {
        ResetSharedState();
        return PageScreenshots.OpenOverSampleData(
            configure: configure, renderForCapture: renderForCapture, composeWindow: composeWindow,
            parse: parse, seedReaderToken: seedReaderToken);
    }

    internal static IReadOnlyList<LayoutCell> QuickCells { get; } = [new(640, 600, 150)];

    /// <summary>Low resolution, a half-screen window, and low resolution zoomed in.</summary>
    internal static IReadOnlyList<LayoutCell> Cells { get; } = [new(640, 600, 100), new(960, 780, 100), new(640, 600, 150)];

    internal static TextWordsResponse QuickWords()
    {
        var source = PageScreenshots.TextWords();
        var words = source.Words.Where(word => word.Form is "hawajafika" or "kitabu").ToArray();
        var texts = source.Texts.Select(text => text with
        {
            Lines = text.Lines.Select(line => line with
            {
                Tokens = line.Tokens.Where(token => token.Form is "hawajafika" or "kitabu").ToArray(),
            }).Where(line => line.Tokens.Count > 0).ToArray(),
        }).ToArray();
        return source with { Words = words, Texts = texts, OccurrenceCount = 2 };
    }

    internal static AssessCommandResponse QuickAssessment() => PageScreenshots.Assessment() with
    {
        Words = PageScreenshots.Assessment().Words.Where(word => word.Word is "hawajafika" or "kitabu").ToArray(),
        SummaryMarkdown = "Two words assessed.", CompletionSummary = "2 words assessed",
    };

    internal static void ConfigurePopulated(FakeCommandClient fake)
    {
        ParsimonyStateScreenshots.Configure(fake, "listed-evidence");
        ProposalsPageScreenshots.Configure(fake);
        fake.PendingChangesIs(ReviewScreenshots.Blocked());
    }

    internal static async Task PreparePopulated(WorkspaceShellViewModel workspace)
    {
        // The populated cells check returning use; the first-run notice is acknowledged as a returning user has.
        workspace.TechDemoNotice?.GotItCommand.Execute(null);
        workspace.Context.TryWord("hawajafika");
        await workspace.Assess.Trace.TryCommand.ExecutionTask!;
        Assert.True(workspace.PageModel<OverviewPageModel>().HasAssessment);
        Assert.True(workspace.PageModel<ProposalsPageModel>().IsListed);
        Assert.True(workspace.PageModel<ParsimonyPageModel>().IsListed);
        Assert.Contains(workspace.PageModel<ReviewPageModel>().ReviewEntries, entry => entry.IsChangeRow);
        var assistants = workspace.PageModel<AssistantConnectionsPageModel>();
        assistants.IsEnabled = true;
        Assert.True(assistants.HasAssistants);
    }

    internal static IReadOnlyList<LayoutFinding> RequiredActions(
        MainWindow window, WorkspaceShellViewModel workspace, LayoutFindingContext context)
    {
        var expected = new List<string>();
        var overview = workspace.Pages.Single(page => page.Page == WorkspacePage.Overview);
        var rail = window.FindControl<ListBox>("PageList")!;
        var navigation = ReachabilityAssertions.CanReachItem(rail, rail.Items.IndexOf(overview), window);
        if (!navigation) expected.Add(overview.AutomationName);
        if (context.Scene.EndsWith("/populated", StringComparison.Ordinal))
            expected.Add(workspace.CurrentPage switch
            {
                WorkspacePage.Overview => "Open slowest words in Timing",
                WorkspacePage.Proposals => "Open",
                WorkspacePage.Texts => "Analyze texts tab",
                WorkspacePage.TryAWord => "Try the word",
                WorkspacePage.Timing => "All timed words",
                WorkspacePage.Warnings => "Choose warning sort order",
                // The tabs live in the side bar; a collapsed side bar offers its labelled reveal instead.
                WorkspacePage.Parsimony => $"id:{AutomationIds.ParsimonyActiveTab}|Show side bar",
                WorkspacePage.Review => "Apply to FieldWorks project",
                WorkspacePage.AiHandoff => "Write the AI Handoff folder",
                WorkspacePage.AiAssistants => "Turn on Advanced AI mode",
                WorkspacePage.Settings => "Leave Settings",
                _ => throw new InvalidOperationException($"No required-action fixture for {workspace.CurrentPage}."),
            });
        if (context.Scene.Contains("/inspector-", StringComparison.Ordinal)) expected.Add("Close the inspector");
        if (context.Scene == "TryAWord/loading") expected.Add("Cancel the trace");
        if (context.Scene == "AiHandoff/loading") expected.Add("Cancel the running AI Handoff");
        var controls = window.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible)
            .Where(ReachabilityAssertions.IsInteractive).ToArray();
        return expected.Where(name => !name.Split('|').Any(route => controls.Any(control =>
                route.StartsWith("id:", StringComparison.Ordinal) ? AutomationProperties.GetAutomationId(control) == route[3..] :
                ControlAutomationPeer.CreatePeerForElement(control)?.GetName() == route)))
            .Select(name => new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                LayoutCheckNames.RequiredActionMissing, new("ExpectedAction", null, null, name, ""),
                new(0, 0, 0, 0), name, "The fixture requires this visible action or navigation route.")).ToArray();
    }

    internal static void Apply(MainWindow window, LayoutCell cell)
    {
        // Smaller cells deliberately probe below the shipping shell's minimum; this does not change its policy.
        window.MinWidth = 0;
        window.MinHeight = 0;
        WindowZoomPolicy.Apply(Application.Current!, cell.Zoom);
        window.Width = cell.Width;
        window.Height = cell.Height;
        LayoutInvariantEngineTests.Settle(window);
        Assert.InRange(Math.Abs(window.ClientSize.Width - cell.Width), 0, 0.5);
        Assert.InRange(Math.Abs(window.ClientSize.Height - cell.Height), 0, 0.5);
    }

    internal static void RequirePage(MainWindow window, WorkspaceShellViewModel workspace, WorkspacePage page)
    {
        Assert.Equal(page, workspace.CurrentPage);
        var host = window.FindControl<Panel>("PageHost")!;
        // A narrow open Inspector takes the working area, so the current page is shown but may be covered.
        var view = Assert.Single(host.Children, child => child.IsVisible);
        var model = workspace.Pages.Single(entry => entry.Page == page).Model;
        // The Settings view binds the window's shared Settings model that its page registration carries.
        Assert.Same(model is SettingsPageModel settings ? settings.Settings : model, view.DataContext);
        Assert.NotEmpty(view.GetVisualDescendants().OfType<Control>());
    }
}
