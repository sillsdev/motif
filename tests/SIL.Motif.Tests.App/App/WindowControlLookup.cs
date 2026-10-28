using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Views;

namespace SIL.Motif.Tests.App;

internal static class WindowControlLookup
{
    /// <summary>Finds a named control, looking inside the hosted workspace view when the window is a <see cref="MainWindow"/>.</summary>
    public static T? FindInWindowOrWorkspace<T>(this Window window, string name) where T : Control =>
        window is MainWindow main
            ? main.FindControl<T>(name)
            : NameScope.GetNameScope(window)?.Find(name) as T;

    /// <summary>
    /// The visible "Choose what to parse" buttons outside the project card. Unlike a lookup by x:Name, which matches
    /// nothing because no control carries that name, this finds the buttons by their accessible name.
    /// </summary>
    public static IReadOnlyList<Button> VisibleChooseWhatToParseButtonsOutsideProjectCard(this Window window) =>
        window.GetVisualDescendants().OfType<Button>().Where(button =>
            button.IsEffectivelyVisible &&
            AutomationProperties.GetName(button) == MotifAppStrings.ChooseWhatToParseAccessibleName &&
            button.FindAncestorOfType<ProjectCardView>() is null).ToList();
}
