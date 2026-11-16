using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

/// <summary>What a person reads on a page, and the faults a real-project screenshot showed that a fake-data test missed.</summary>
internal static partial class RenderedPageText
{
    /// <summary>A control drawn as its type name.</summary>
    [GeneratedRegex(@"^(Avalonia|System|Microsoft|CommunityToolkit|SIL)\.[A-Za-z_][\w`]*(\.[A-Za-z_][\w`]*)+")]
    private static partial Regex TypeName();

    /// <summary>A state that says the page is still reading.</summary>
    [GeneratedRegex(@"^(Reading|Loading)\b.*(…|\.\.\.)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex StillReading();

    /// <summary>The non-empty text of every visible text block under <paramref name="root"/>.</summary>
    internal static IReadOnlyList<TextBlock> Visible(Visual root) =>
        root.GetSelfAndVisualDescendants().OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible && block.Bounds.Width > 0 && block.Bounds.Height > 0 &&
                !string.IsNullOrWhiteSpace(block.Text))
            .ToArray();

    internal static IEnumerable<string> TypeNameFaults(IEnumerable<TextBlock> texts) =>
        texts.Select(block => block.Text!.Trim()).Where(text => TypeName().IsMatch(text)).Distinct();

    internal static IEnumerable<string> StillReadingFaults(IEnumerable<TextBlock> texts) =>
        texts.Select(block => block.Text!.Trim()).Where(text => StillReading().IsMatch(text)).Distinct();

    /// <summary>Texts other than the page's title and the project's file name, which show on an otherwise blank page.</summary>
    internal static IReadOnlyList<string> Body(IEnumerable<TextBlock> texts, string title, string projectPath)
    {
        var file = Path.GetFileName(projectPath);
        var stem = Path.GetFileNameWithoutExtension(projectPath);
        return texts.Select(block => block.Text!.Trim())
            .Where(text => !string.Equals(text, title, StringComparison.OrdinalIgnoreCase) &&
                text != file && text != stem && text != projectPath)
            .ToArray();
    }

    /// <summary>One sentence per visible text that does not fit its box, using the suite's own fit rule.</summary>
    internal static IReadOnlyList<string> ClipFaults(IEnumerable<TextBlock> texts)
    {
        var faults = new List<string>();
        foreach (var block in texts)
        {
            try { LayoutAssertions.AssertTextFits(block); }
            catch (Xunit.Sdk.XunitException fault) { faults.Add(fault.Message.Split('\n')[0]); }
        }
        return faults;
    }
}
