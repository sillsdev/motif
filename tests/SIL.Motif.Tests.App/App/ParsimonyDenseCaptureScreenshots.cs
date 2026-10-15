using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class ParsimonyDenseCaptureScreenshots
{
    private static readonly (string Name, int Width, int Height)[] Viewports =
    [
        ("thin", 640, 600),
        ("half", 960, 780),
        ("full", 1920, 1080),
        ("short", 960, 600),
    ];

    private static readonly (string Name, ThemeVariant Variant)[] Themes =
    [
        ("light", ThemeVariant.Light),
        ("dark", ThemeVariant.Dark),
    ];

    private static readonly int[] Zooms = [100, 150];

    [ScreenshotFact]
    public void CaptureMixedDecisionsAndSelectedInspectorAcrossMainWindowViewports()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        var notes = new List<string>
        {
            "Captures use the actual MainWindow and its WorkspaceLayoutHost with a synthetic Parsimony Report.",
            "Each requested width and height is assigned directly to MainWindow; zoom is set separately and never multiplies the client size.",
            "The primary matrix is 640x600, 960x780, 1920x1080, and 960x600 client pixels, each in light/dark at 100/150%.",
            "Every filename and inventory entry records the measured ClientSize; a size mismatch is retained as a failure.",
            "Earlier parsimony-dense-hosted-* and standalone parsimony-dense-active-* evidence remains untouched.",
            "Decision and evidence values are synthetic fixture data, not a project Report.",
            "Primary frames retain the host's automatic pane choices; explicit Inspector and returned frames are separate.",
            "Details, row Ask, and Inspector Ask request the page Inspector through the published route.",
            "A Suppressed decision is also captured at full size in each theme and zoom.",
            "H reference: /mnt/h/repos/motif.worktrees/_briefs/ui-2026-10-13/op-design-visions/render/H/Parsimony.png (1920x1080).",
        };
        var failures = new List<string>();

        foreach (var theme in Themes)
        foreach (var zoom in Zooms)
        foreach (var viewport in Viewports)
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
                var previousTheme = Application.Current!.RequestedThemeVariant;
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false,
                    configure: (fake, _) => ParsimonyStateScreenshots.Configure(fake, "dense-review"),
                    advancedAiModeEnabled: true);
                var settings = SettingsTestAccess.Model(window);
                var previousZoom = settings.ZoomPercent;
                try
                {
                    window.Width = viewport.Width;
                    window.Height = viewport.Height;
                    settings.ZoomPercent = zoom;
                    Application.Current!.RequestedThemeVariant = theme.Variant;
                    workspace.CurrentPage = WorkspacePage.Parsimony;
                    PageScreenshots.Settle(window);

                    var model = workspace.PageModel<ParsimonyPageModel>();
                    await ParsimonyStateScreenshots.DriveDenseReviewAsync(model);
                    var page = Assert.Single(window.GetVisualDescendants().OfType<ParsimonyPage>());
                    var host = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
                    Assert.Contains(host.Page!.GetVisualDescendants().OfType<ParsimonyPage>(),
                        hostedPage => ReferenceEquals(page, hostedPage));
                    var label = $"{theme.Name}/{viewport.Name}/{zoom}%";

                    var clientSize = window.ClientSize;
                    Save(window, Path.Combine(folder,
                        $"parsimony-dense-acceptance-active-{theme.Name}-{viewport.Name}-client-" +
                        $"{clientSize.Width:0}x{clientSize.Height:0}-{zoom}pct.png"));
                    notes.Add($"Active {label}: requested client {viewport.Width}x{viewport.Height}, " +
                        $"measured client {clientSize.Width:0}x{clientSize.Height:0}, " +
                        $"logical host width {host.Bounds.Width:0}; Active/Asked/Kept/Deferred remain listed, " +
                        $"automatic side panel visible={host.FindControl<Border>("SidePanelBorder")!.IsVisible}, " +
                        $"automatic Inspector visible={host.FindControl<Border>("InspectorPane")!.IsVisible}; " +
                        "the selected finding's row Ask is checked.");
                    CollectClientSizeFailure(window, viewport, $"{label} Active", failures);
                    failures.AddRange(InspectorFailures(window, host, model, label, requireInspector: false));
                    CollectLayoutFailure(window, $"{label} Active", failures);

                    RequestActiveDetails(page, window, label, failures);
                    BringInspectorAskIntoView(window);
                    clientSize = window.ClientSize;
                    Save(window, Path.Combine(folder,
                        $"parsimony-dense-acceptance-inspector-{theme.Name}-{viewport.Name}-client-" +
                        $"{clientSize.Width:0}x{clientSize.Height:0}-{zoom}pct.png"));
                    notes.Add($"Inspector {label}: requested client {viewport.Width}x{viewport.Height}, " +
                        $"measured client {clientSize.Width:0}x{clientSize.Height:0}, " +
                        $"logical host width {host.Bounds.Width:0}; selected evidence and Notebook question are shown, " +
                        "and the Inspector Ask and return actions are reachable.");
                    CollectClientSizeFailure(window, viewport, label, failures);
                    failures.AddRange(InspectorFailures(window, host, model, label, requireInspector: true));
                    CollectLayoutFailure(window, $"{label} Inspector", failures);

                    var hideInspector = host.FindControl<Button>("InspectorHideButton")!;
                    try
                    {
                        HeadlessClick.Click(window, hideInspector, "Hide Inspector after explicit Details");
                    }
                    catch (Exception error)
                    {
                        failures.Add($"{label}: the explicitly opened Inspector could not return to the page: {error.Message}");
                    }
                    PageScreenshots.Settle(window);
                    if (host.FindControl<Border>("InspectorPane")!.IsVisible ||
                        host.FindControl<ContentControl>("PageInspectorHost")!.IsEffectivelyVisible)
                        failures.Add($"{label}: the Inspector did not return to the collapsed page state.");
                    if (!ReferenceEquals(model.SelectedTableEntry, model.Rows.FirstOrDefault(row =>
                            !row.IsHeader && row.Items == "ka- + -ni")) ||
                        model.QuestionText != "Does the finding's 2-of-4 count include this named prohibition?")
                        failures.Add($"{label}: returning from the Inspector lost the selected finding or Notebook draft.");
                    clientSize = window.ClientSize;
                    Save(window, Path.Combine(folder,
                        $"parsimony-dense-acceptance-return-{theme.Name}-{viewport.Name}-client-" +
                        $"{clientSize.Width:0}x{clientSize.Height:0}-{zoom}pct.png"));
                    notes.Add($"Return {label}: the Inspector is collapsed after explicit Details; selected finding and Notebook draft remain.");
                    CollectClientSizeFailure(window, viewport, $"{label} Return", failures);
                    failures.AddRange(InspectorFailures(window, host, model, label, requireInspector: false));
                    CollectLayoutFailure(window, $"{label} Return", failures);

                    if (viewport.Name == "full")
                        CaptureSuppressed(theme.Name, zoom, viewport, window, page, host, model, folder, notes, failures);
                }
                finally
                {
                    settings.ZoomPercent = previousZoom;
                    Application.Current!.RequestedThemeVariant = previousTheme;
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromMinutes(3));
        }

        File.WriteAllLines(Path.Combine(folder, "parsimony-dense-acceptance-captures.txt"), notes);
        File.WriteAllLines(Path.Combine(folder, "parsimony-dense-acceptance-capture-failures.txt"),
            failures.Count == 0 ? ["None."] : failures);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static void RequestActiveDetails(ParsimonyPage page, Window window, string state,
        ICollection<string> failures)
    {
        Flyout? flyout = null;
        try
        {
            var list = page.FindControl<ListBox>("ParsimonyRowsItems")!;
            var selected = Assert.IsType<ParsimonyListRow>(((ParsimonyPageModel)page.DataContext!).SelectedTableEntry);
            var row = Assert.Single(list.GetVisualDescendants().OfType<ListBoxItem>(),
                item => ReferenceEquals(item.DataContext, selected));
            var menu = Assert.Single(row.GetVisualDescendants().OfType<Button>(),
                button => button.Classes.Contains("denseTableMenu"));
            flyout = Assert.IsType<Flyout>(menu.Flyout);
            flyout.ShowAt(menu);
            PageScreenshots.Settle(window);
            var details = Assert.Single(Assert.IsAssignableFrom<Control>(flyout.Content)
                .GetVisualDescendants().OfType<Button>(),
                button => AutomationProperties.GetAutomationId(button) == selected.DetailsAutomationId);
            HeadlessClick.Click(window, details, AutomationProperties.GetName(details) ?? "Finding details");
            PageScreenshots.Settle(window);
        }
        catch (Exception error)
        {
            flyout?.Hide();
            failures.Add($"{state}: explicit Details could not request the page Inspector: {error.Message}");
        }
    }

    private static void CaptureSuppressed(string theme, int zoom, (string Name, int Width, int Height) viewport,
        Window window, ParsimonyPage page, WorkspaceLayoutHost host, ParsimonyPageModel model, string folder,
        ICollection<string> notes, ICollection<string> failures)
    {
        model.IsShowingSuppressed = true;
        PageScreenshots.Settle(window);
        var suppressed = model.SuppressedItems.FirstOrDefault();
        if (suppressed is null)
        {
            failures.Add($"{theme}/full/{zoom}% Suppressed: the fixture did not load its saved decision.");
            var missingClient = window.ClientSize;
            Save(window, Path.Combine(folder,
                $"parsimony-dense-acceptance-suppressed-missing-{theme}-client-" +
                $"{missingClient.Width:0}x{missingClient.Height:0}-{zoom}pct.png"));
            notes.Add($"Suppressed {theme}/full/{zoom}%: no saved decision was available; preserved the current frame.");
            CollectClientSizeFailure(window, viewport, $"{theme}/full/{zoom}% Suppressed", failures);
            CollectLayoutFailure(window, $"{theme}/full/{zoom}% Suppressed", failures);
            return;
        }

        var suppressedList = page.FindControl<ListBox>("ParsimonySuppressedItems")!;
        var decision = suppressedList.GetVisualDescendants().OfType<ListBoxItem>()
            .FirstOrDefault(item => ReferenceEquals(item.DataContext, suppressed));
        if (decision is not null)
        {
            try
            {
                HeadlessClick.Click(window, decision, "Saved Suppressed decision");
                WorkspaceLayoutHost.RequestPageInspector(page);
            }
            catch (Exception error)
            {
                failures.Add($"{theme}/full/{zoom}% Suppressed: its details action could not be clicked: {error.Message}");
                model.SelectSuppressed(suppressed.Key);
                WorkspaceLayoutHost.RequestPageInspector(page);
            }
        }
        else
        {
            failures.Add($"{theme}/full/{zoom}% Suppressed: the decision's explicit details action is unavailable.");
            model.SelectSuppressed(suppressed.Key);
            WorkspaceLayoutHost.RequestPageInspector(page);
        }
        PageScreenshots.Settle(window);
        var clientSize = window.ClientSize;
        Save(window, Path.Combine(folder,
            $"parsimony-dense-acceptance-suppressed-{theme}-full-client-" +
            $"{clientSize.Width:0}x{clientSize.Height:0}-{zoom}pct.png"));
        notes.Add($"Suppressed {theme}/full/{zoom}%: requested client {viewport.Width}x{viewport.Height}, " +
            $"measured client {clientSize.Width:0}x{clientSize.Height:0}; one saved finding is selected and its Inspector detail is shown.");
        if (!suppressed.IsSelected)
            failures.Add($"{theme}/full/{zoom}% Suppressed: the saved decision was not selected.");
        if (!host.FindControl<Border>("InspectorPane")!.IsVisible ||
            !host.FindControl<ContentControl>("PageInspectorHost")!.IsVisible ||
            !host.GetVisualDescendants().OfType<Border>().Any(border =>
                AutomationProperties.GetAutomationId(border) == AutomationIds.ParsimonyEvidence &&
                border.IsEffectivelyVisible))
            failures.Add($"{theme}/full/{zoom}% Suppressed: selected decision Inspector detail is not visible.");
        CollectClientSizeFailure(window, viewport, $"{theme}/full/{zoom}% Suppressed", failures);
        CollectLayoutFailure(window, $"{theme}/full/{zoom}% Suppressed", failures);
    }

    private static void BringInspectorAskIntoView(Window window)
    {
        var question = window.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(input => AutomationProperties.GetName(input) == "Question");
        var ask = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => AutomationProperties.GetAutomationId(button) == AutomationIds.ParsimonyAsk);
        question?.BringIntoView();
        ask?.BringIntoView();
        PageScreenshots.Settle(window);
    }

    private static IReadOnlyList<string> InspectorFailures(Window window, WorkspaceLayoutHost host,
        ParsimonyPageModel model, string viewport, bool requireInspector)
    {
        var failures = new List<string>();
        if (model.QuestionText != "Does the finding's 2-of-4 count include this named prohibition?")
            failures.Add($"{viewport}: selected Inspector question does not match its model value.");
        if (model.Evidence?.Details.Any(detail => detail.Label == "Items" && detail.Value == "ka- + -ni") != true)
            failures.Add($"{viewport}: the selected finding's Inspector lost its named item.");
        var selected = model.SelectedTableEntry as ParsimonyListRow;
        if (selected is not { IsHeader: false })
            failures.Add($"{viewport}: the selected Active finding is not in the table.");

        var table = host.GetVisualDescendants().OfType<ListBox>()
            .SingleOrDefault(list => list.Name == "ParsimonyRowsItems");
        var row = table?.GetVisualDescendants().OfType<ListBoxItem>()
            .SingleOrDefault(item => ReferenceEquals(item.DataContext, selected));
        var rowAsk = row?.GetVisualDescendants().OfType<Button>()
            .SingleOrDefault(button => button.Content?.ToString() == "Ask");
        var pageVisible = host.FindControl<Grid>("WorkingPageRegion")!.IsEffectivelyVisible;
        if (pageVisible)
        {
            if (rowAsk is null || !rowAsk.IsEffectivelyVisible || !rowAsk.IsEnabled || !rowAsk.IsTabStop ||
                rowAsk.Command?.CanExecute(rowAsk.CommandParameter) != true)
                failures.Add($"{viewport}: the selected table row's Ask action is not reachable.");
            else
                AddViewportFailure(rowAsk, window, viewport, "selected row Ask action", failures);
        }
        else if (requireInspector)
        {
            var back = host.FindControl<Button>("InspectorHideButton")!;
            if (!back.IsEffectivelyVisible || !back.IsEnabled || !Equals(back.Content, "Back to page"))
                failures.Add($"{viewport}: the focused Inspector has no reachable Back to page action.");
            else
                AddViewportFailure(back, window, viewport, "Back to page", failures);
        }
        else failures.Add($"{viewport}: the working page is hidden before inspection or after return.");

        var pageInspector = host.FindControl<ContentControl>("PageInspectorHost")!;
        if (!requireInspector && !pageInspector.IsEffectivelyVisible) return failures;

        var evidence = host.GetVisualDescendants().OfType<Border>()
            .Where(border => AutomationProperties.GetAutomationId(border) == AutomationIds.ParsimonyEvidence).ToArray();
        var recordTypes = host.GetVisualDescendants().OfType<ComboBox>()
            .Where(combo => AutomationProperties.GetName(combo) == "Notebook record type").ToArray();
        var questions = host.GetVisualDescendants().OfType<TextBox>()
            .Where(input => AutomationProperties.GetName(input) == "Question").ToArray();
        var asks = host.GetVisualDescendants().OfType<Button>()
            .Where(button => AutomationProperties.GetAutomationId(button) == AutomationIds.ParsimonyAsk).ToArray();

        if (evidence.Length != 1 || !evidence[0].IsEffectivelyVisible)
            failures.Add($"{viewport}: selected finding evidence is not visible in the Inspector.");
        if (recordTypes.Length != 1 || !recordTypes[0].IsEffectivelyVisible)
            failures.Add($"{viewport}: Notebook record type is not visible in the Inspector.");
        if (questions.Length != 1 || !questions[0].IsEffectivelyVisible || !questions[0].IsEnabled)
            failures.Add($"{viewport}: the Notebook question field is unavailable.");
        else if (questions[0].Text != model.QuestionText)
            failures.Add($"{viewport}: the Notebook question field does not show the model value.");
        if (asks.Length != 1 || !asks[0].IsEffectivelyVisible || !asks[0].IsEnabled || !asks[0].IsTabStop ||
            asks[0].Command?.CanExecute(asks[0].CommandParameter) != true)
            failures.Add($"{viewport}: the Inspector Ask action is not reachable.");

        if (questions.Length == 1)
            AddViewportFailure(questions[0], window, viewport, "Notebook question", failures);
        if (recordTypes.Length == 1)
            AddViewportFailure(recordTypes[0], window, viewport, "Notebook record type", failures);
        if (asks.Length == 1)
            AddViewportFailure(asks[0], window, viewport, "Inspector Ask action", failures);
        return failures;
    }

    private static void AddViewportFailure(Control control, Window window, string viewport, string label,
        ICollection<string> failures)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window);
        var client = window.ClientSize;
        if (origin is null || origin.Value.X < 0 || origin.Value.Y < 0 ||
            origin.Value.X + control.Bounds.Width > client.Width ||
            origin.Value.Y + control.Bounds.Height > client.Height)
            failures.Add($"{viewport}: {label} is outside the rendered viewport.");
    }

    private static void CollectClientSizeFailure(Window window, (string Name, int Width, int Height) viewport,
        string label, ICollection<string> failures)
    {
        var client = window.ClientSize;
        if (Math.Abs(client.Width - viewport.Width) > 0.5 || Math.Abs(client.Height - viewport.Height) > 0.5)
            failures.Add($"{label}: requested {viewport.Width}x{viewport.Height} client pixels, " +
                $"measured {client.Width:0}x{client.Height:0}.");
    }

    private static void CollectLayoutFailure(Window window, string viewport, ICollection<string> failures)
    {
        try
        {
            LayoutAssertions.AssertCurrent(window);
        }
        catch (Exception failure)
        {
            failures.Add($"{viewport}: {failure.Message}");
        }
    }

    private static void Save(Window window, string path)
    {
        PageScreenshots.Settle(window);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException(
            $"No frame rendered for {path}.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }
}
