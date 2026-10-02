using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

internal static class InteractiveControlSweep
{
    public static void AssertScene(
        WalkthroughWindow walkthrough,
        string scene,
        params InteractiveControlFamily[] expectedFamilies)
    {
        AssertSceneCore(walkthrough, scene, expectedFamilies, []);
    }

    public static void AssertSceneWithReportedGaps(
        WalkthroughWindow walkthrough,
        string scene,
        string test,
        params InteractiveControlFamily[] expectedFamilies)
    {
        var gaps = InteractiveControlManifest.ReportedSceneGaps
            .Where(gap => gap.Test == test && gap.Scene == scene)
            .ToArray();
        Assert.NotEmpty(gaps);
        AssertSceneCore(walkthrough, scene, expectedFamilies, gaps);
    }

    private static void AssertSceneCore(
        WalkthroughWindow walkthrough,
        string scene,
        InteractiveControlFamily[] expectedFamilies,
        ReportedSceneGap[] gaps)
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
        var expected = expectedFamilies.ToHashSet();
        var gapFamilies = gaps.Select(gap => gap.Family).ToHashSet();
        var missing = expected.Except(observedFamilies).Order().ToArray();
        var missingGaps = gaps.Where(gap => !observedFamilies.Contains(gap.Family)).ToArray();
        var unexpected = observedFamilies.Except(expected).Except(gapFamilies).Order().ToArray();
        var observed = string.Join("; ", discovered.OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key} ({pair.Value.Count}): " + (unexpected.Contains(pair.Key)
                ? string.Join(" | ", pair.Value.Take(6))
                : pair.Value[0])));
        Assert.True(missing.Length == 0 && missingGaps.Length == 0 && unexpected.Length == 0 && unknown.Count == 0,
            $"Scene '{scene}' family mismatch. Missing=[{string.Join(", ", missing)}]; " +
            $"reported gaps no longer present=[{string.Join(" | ", missingGaps.Select(gap => $"{gap.Family}: {gap.Reason}"))}]; " +
            $"unexpected=[{string.Join(", ", unexpected)}]; " +
            $"unknown peers ({unknown.Count})=[{string.Join(" | ", unknown.Take(12))}]. " +
            $"Observed=[{observed}].");
    }

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
        OpinionMark or UnreadMark or MarkChip => InteractiveControlFamily.Mark,
        HyperlinkButton => InteractiveControlFamily.Link,
        SplitButton => InteractiveControlFamily.Action,
        CheckBox => InteractiveControlFamily.Check,
        RadioButton => InteractiveControlFamily.Radio,
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
        var sources = InteractiveControlManifest.MarkupDeclarations
            .Where(declaration => InteractiveControlManifest.MarkupFamilies[declaration.MarkupType] == family)
            .Select(declaration => declaration.Source)
            .Concat(InteractiveControlManifest.GeneratedFamilies
                .Where(generated => generated.Family == family)
                .Select(generated => generated.Source))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        return string.Join(", ", sources.Take(2));
    }
}
