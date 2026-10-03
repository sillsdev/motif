using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

internal static class InteractiveControlSweep
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> CriticalActions =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["no project selected"] = [AutomationIds.ProjectMenu],
            ["first project setup"] = [AutomationIds.SetupNext, AutomationIds.SkipSetup],
            ["setup text selection"] = [AutomationIds.SetupSelectAllTexts, AutomationIds.SetupClearTexts],
            ["completed Overview"] = [AutomationIds.RefreshProject],
            ["completed Compare Matrix"] = [AutomationIds.RunAssessment],
            ["pending changes in Review"] = [AutomationIds.MeasureChanges, AutomationIds.ApplyChanges],
            ["pending change in Review"] = [AutomationIds.MeasureChanges, AutomationIds.ApplyChanges],
            ["Review receipt"] = [AutomationIds.ApplyReceipt],
            ["Apply receipt"] = [AutomationIds.ApplyReceipt],
            ["stale Review values"] = [AutomationIds.RefreshProject],
            ["completed Timing"] = [AutomationIds.RefreshProject],
            ["completed Warnings"] = [AutomationIds.RefreshProject],
            ["completed Analyze texts"] = [AutomationIds.AnalyzeTextsTab, AutomationIds.RunAssessment],
            ["Analyze texts with many Texts"] =
                [AutomationIds.SelectAllTexts, AutomationIds.ClearTexts, AutomationIds.RunAssessment],
            ["completed Word list with opinion counts"] = [AutomationIds.AnalyzeTextsTab, AutomationIds.RunAssessment],
            ["word card with a reading"] = [AutomationIds.AnalyzeTextsTab],
            ["opened diagnostic window"] = [AutomationIds.TryWordInput, AutomationIds.TryWordRun],
            ["completed Handoff"] = [AutomationIds.WriteHandoff],
            ["older store refused"] = [AutomationIds.ProjectMenu],
            ["ready to assess pasted words"] = [AutomationIds.RunAssessment],
            ["parse progress with stopped words"] = [AutomationIds.ParseProgressDetails, AutomationIds.ParseStoppedWords, AutomationIds.CancelAssessment],
            ["parse with no progress"] = [AutomationIds.ParseProgressDetails, AutomationIds.ParseNoProgress, AutomationIds.ParseReportProblem, AutomationIds.CancelAssessment],
            ["worker parse progress"] = [AutomationIds.ParseProgressDetails, AutomationIds.ParseAllWordsProgress, AutomationIds.CancelAssessment],
            ["Assessment running"] = [AutomationIds.CancelAssessment],
            ["Assessment cancelled"] = [AutomationIds.RunAssessment],
        };

    public static void AssertScene(
        WalkthroughWindow walkthrough,
        string scene,
        params InteractiveControlFamily[] expectedFamilies)
    {
        AssertSceneCore(walkthrough, scene, expectedFamilies);
    }

    private static void AssertSceneCore(
        WalkthroughWindow walkthrough,
        string scene,
        InteractiveControlFamily[] expectedFamilies)
    {
        Assert.NotEmpty(expectedFamilies);
        var roots = CollectRoots(walkthrough);
        var controls = roots.SelectMany(root => Descendants(root.Root)
                .Select(control => (root.Label, Control: control)))
            .DistinctBy(entry => entry.Control, ReferenceEqualityComparer.Instance)
            .Where(entry => entry.Control.IsEffectivelyVisible && !IsScrollChrome(entry.Control) &&
                IsOnCurrentPage(walkthrough, entry.Control))
            .ToArray();
        var discovered = new Dictionary<InteractiveControlFamily, List<string>>();
        var unknown = new List<string>();

        foreach (var (root, control) in controls)
        {
            if (FamilyOf(control) is { } family)
            {
                if (!discovered.TryGetValue(family, out var peers))
                    discovered.Add(family, peers = []);
                peers.Add(Describe(root, control, family));
            }
            else if (control.Focusable ||
                     !string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)) ||
                     !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(control)))
            {
                unknown.Add(Describe(root, control, null));
            }
        }

        var observedFamilies = discovered.Keys.ToHashSet();
        var missing = MissingRequiredFamilies(expectedFamilies, observedFamilies);
        var criticalActions = CriticalActions.TryGetValue(scene, out var requiredActions)
            ? requiredActions
            : throw new Xunit.Sdk.XunitException($"Scene '{scene}' has no authored critical AutomationId contract.");
        var currentPageId = AutomationIds.ForPage(walkthrough.Workspace.CurrentPage);
        var observedAutomationIds = controls.Select(entry => AutomationProperties.GetAutomationId(entry.Control))
            .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToArray();
        var missingAutomationIds = MissingCriticalAutomationIds(
            [currentPageId, .. criticalActions], observedAutomationIds);
        var observed = string.Join("; ", discovered.OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key} ({pair.Value.Count}): {pair.Value[0]}"));
        Assert.True(missing.Count == 0 && missingAutomationIds.Count == 0 && unknown.Count == 0,
            $"Scene '{scene}' contract mismatch. Missing required families=[{string.Join(", ", missing)}]; " +
            $"missing critical AutomationIds=[{string.Join(", ", missingAutomationIds)}]; " +
            $"unknown peers ({unknown.Count})=[{string.Join(" | ", unknown.Take(12))}]. " +
            $"Observed=[{observed}].");
    }

    internal static IReadOnlyList<InteractiveControlFamily> MissingRequiredFamilies(
        IEnumerable<InteractiveControlFamily> required, IEnumerable<InteractiveControlFamily> observed) =>
        required.Except(observed).Distinct().Order().ToArray();

    internal static IReadOnlyList<string> MissingCriticalAutomationIds(
        IEnumerable<string> required, IEnumerable<string> observed) =>
        required.Except(observed, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<(Control Root, string Label)> CollectRoots(WalkthroughWindow walkthrough)
    {
        var roots = new List<(Control Root, string Label)> { (walkthrough.Window, "main window") };
        roots.AddRange(walkthrough.Window.OwnedWindows.Select(window =>
            ((Control)window, $"owned window '{window.Title ?? window.GetType().Name}'")));

        var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < roots.Count; index++)
        {
            var root = roots[index];
            if (!visited.Add(root.Root)) continue;
            foreach (var control in Descendants(root.Root))
            {
                if (control is Popup { IsOpen: true, Child: Control child } popup)
                    roots.Add((child, $"popup from {popup.PlacementTarget?.Name ?? popup.GetType().Name}"));
                if (control is Button { Flyout: Flyout { IsOpen: true, Content: Control content } })
                    roots.Add((content, $"flyout from {control.Name ?? control.GetType().Name}"));
            }
        }

        return roots;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var control in root.GetLogicalDescendants().OfType<Control>()) yield return control;
        foreach (var control in root.GetVisualDescendants().OfType<Control>()) yield return control;
    }

    // A theme's scroll bar appears only when a page overflows the window, and ScrollViewer owns its keys.
    private static bool IsScrollChrome(Control control) =>
        control is ScrollBar || control.GetVisualAncestors().OfType<ScrollBar>().Any();

    private static bool IsOnCurrentPage(WalkthroughWindow walkthrough, Control control)
    {
        var ancestors = control.GetLogicalAncestors().OfType<Control>()
            .Concat(control.GetVisualAncestors().OfType<Control>())
            .DistinctBy(ancestor => ancestor, ReferenceEqualityComparer.Instance)
            .ToArray();
        if (ancestors.OfType<Popup>().Any(popup => !popup.IsOpen)) return false;

        var names = ancestors
            .Select(ancestor => ancestor.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.Ordinal);
        var pageOwners = Enum.GetValues<WorkspacePage>()
            .Where(page => names.Contains($"{page}Page"))
            .ToArray();
        if (pageOwners.Any(page => page != walkthrough.Workspace.CurrentPage)) return false;
        if (walkthrough.Workspace.CurrentPage != WorkspacePage.Texts) return true;

        var tab = walkthrough.Workspace.PageModel<TextsPageModel>().Tab;
        if (names.Contains("CompareHost") && tab != TextsTab.Matrix) return false;
        if (names.Contains("ListsHost") && tab != TextsTab.Lists) return false;
        if ((names.Contains("SelectionHost") || names.Contains("ResultsInTextHost")) &&
            tab != TextsTab.AnalyzeTexts) return false;
        return true;
    }

    private static InteractiveControlFamily? FamilyOf(Control control) => control switch
    {
        Control when control.GetType().Name == "MarkdownRenderer" => InteractiveControlFamily.Container,
        FilterChip => InteractiveControlFamily.Filter,
        MatrixCell => InteractiveControlFamily.MatrixCell,
        CopyableTextBlock => InteractiveControlFamily.SelectableText,
        OpinionMark or UnreadMark or MarkChip or MarkGlyph => InteractiveControlFamily.Mark,
        HyperlinkButton => InteractiveControlFamily.Link,
        SplitButton => InteractiveControlFamily.Action,
        RadioButton => InteractiveControlFamily.Radio,
        CheckBox => InteractiveControlFamily.Check,
        ToggleButton toggle when toggle.Name == "ExpanderHeader" &&
            toggle.GetVisualAncestors().OfType<Expander>().Any() => InteractiveControlFamily.Disclosure,
        ToggleButton => InteractiveControlFamily.Check,
        Button => InteractiveControlFamily.Action,
        TextBox => InteractiveControlFamily.TextEntry,
        NumericUpDown => InteractiveControlFamily.TextEntry,
        ComboBox => InteractiveControlFamily.Choice,
        Expander => InteractiveControlFamily.Disclosure,
        ListBox or ListBoxItem => InteractiveControlFamily.List,
        MenuItem => InteractiveControlFamily.Action,
        DataGrid or DataGridColumnHeader or DataGridRow or DataGridCell => InteractiveControlFamily.Grid,
        TreeView or TreeViewItem => InteractiveControlFamily.Tree,
        MorphemeRow => InteractiveControlFamily.Morpheme,
        ScrollViewer { Name: "FieldWorksMorphemeScroll" } => InteractiveControlFamily.ScrollViewport,
        TimingKindBar => InteractiveControlFamily.Summary,
        OutcomeBar => InteractiveControlFamily.Summary,
        ProgressBar => InteractiveControlFamily.Progress,
        Ellipse mark when AutomationProperties.GetName(mark) == "Unread" => InteractiveControlFamily.Mark,
        DockPanel panel when !string.IsNullOrWhiteSpace(AutomationProperties.GetName(panel)) =>
            InteractiveControlFamily.Container,
        WrapPanel panel when !string.IsNullOrWhiteSpace(AutomationProperties.GetName(panel)) =>
            InteractiveControlFamily.Container,
        Border { Focusable: true } => InteractiveControlFamily.FocusableSurface,
        Border { DataContext: ResultsTokenViewModel } part
            when !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(part)) => InteractiveControlFamily.Occurrence,
        Border { } border when !string.IsNullOrWhiteSpace(AutomationProperties.GetName(border)) =>
            InteractiveControlFamily.ContentSurface,
        Border { Classes: var classes } when classes.Contains("handoffFile") => InteractiveControlFamily.FocusableSurface,
        TextBlock text when !string.IsNullOrWhiteSpace(AutomationProperties.GetName(text)) ||
            !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(text)) => InteractiveControlFamily.StaticText,
        ItemsControl collection when !string.IsNullOrWhiteSpace(AutomationProperties.GetName(collection)) ||
            !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(collection)) => InteractiveControlFamily.Collection,
        StackPanel { Tag: ResultsTokenViewModel } => InteractiveControlFamily.Occurrence,
        UserControl named when !string.IsNullOrWhiteSpace(AutomationProperties.GetName(named)) =>
            InteractiveControlFamily.Container,
        StackPanel panel when panel.Focusable ||
            !string.IsNullOrWhiteSpace(AutomationProperties.GetName(panel)) ||
            !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(panel)) => InteractiveControlFamily.Container,
        _ => null,
    };

    private static string Describe(string root, Control control, InteractiveControlFamily? family)
    {
        var name = AutomationProperties.GetName(control);
        var id = AutomationProperties.GetAutomationId(control);
        var source = family is null ? "unmapped source" : SourceFor(family.Value);
        return $"page/root={root}, source={source}, family={family?.ToString() ?? "unmapped"}, " +
            $"type={control.GetType().Name}, name='{name ?? control.Name}', id='{id}'";
    }

    private static string SourceFor(InteractiveControlFamily family)
    {
        if (family == InteractiveControlFamily.ScrollViewport)
            return "src/SIL.Motif.App/Views/WordRow.axaml";

        var sources = InteractiveMarkupContracts.TypesForFamily(family)
            .Concat(InteractiveControlManifest.GeneratedFamilies
                .Where(generated => generated.Family == family)
                .Select(generated => generated.Source))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        return string.Join(", ", sources.Take(2));
    }
}
