using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A <c>Tokens/Components/*Scoped.axaml</c> file is included by the views that use it, not by the application, so a
/// test that builds a bare control wearing its classes includes the file in the host it builds, as the view does.
/// </summary>
internal static class ScopedComponentStyles
{
    private static readonly string[] All =
        ["Settings", "Setup", "TryWord", "Warnings", "Proposals", "Handoff", "Diagnostics", "Sources",
        // W9-progress
        "Progress"];

    /// <summary>Includes the named component's scoped styles, such as <c>SettingsScoped</c>, in <paramref name="host"/>.</summary>
    internal static void Include(StyledElement host, params string[] components)
    {
        foreach (var component in components)
        {
            var name = component.EndsWith("Scoped", StringComparison.Ordinal) ? component : component + "Scoped";
            var source = new Uri($"avares://SIL.Motif.App/Tokens/Components/{name}.axaml");
            host.Styles.Add(new StyleInclude(source) { Source = source });
        }
    }

    /// <summary>Includes every component's scoped styles in <paramref name="host"/>.</summary>
    internal static void IncludeAll(StyledElement host) => Include(host, All);
}
