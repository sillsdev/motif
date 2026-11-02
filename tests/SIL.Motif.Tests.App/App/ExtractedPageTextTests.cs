using System.Text.RegularExpressions;
using SIL.Motif.Generator;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that the pages whose window text moved to MotifAppStrings.resx stay free of literal text, so a new
/// literal cannot slip back in unnoticed between runs of the report-only localization ratchet.
/// </summary>
public sealed partial class ExtractedPageTextTests
{
    public static TheoryData<string> ExtractedViews() =>
    [
        "Views/TryWordPanel.axaml",
        "Views/Pages/ParsimonyPage.axaml",
        "Views/HandoffPanel.axaml",
        "Views/ComparePanel.axaml",
        "Views/MiniMatrix.axaml",
        "Views/Pages/AssistantConnectionsPage.axaml",
    ];

    [Theory]
    [MemberData(nameof(ExtractedViews))]
    public void ExtractedViewsCarryNoLiteralWindowText(string relativePath)
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), "src", "SIL.Motif.App", relativePath);
        var literals = LiteralText().Matches(File.ReadAllText(path))
            .Select(m => m.Value)
            .ToArray();

        Assert.True(literals.Length == 0, $"{relativePath} still has literal window text: {string.Join(" | ", literals)}");
    }

    // The ratchet's own pattern: Text, Content, ToolTip.Tip and AutomationProperties.Name, bindings excluded.
    [GeneratedRegex(@"(?<![\w.:])(?:Text|Content|ToolTip\.Tip|AutomationProperties\.Name)\s*=\s*""(?![{\s""])[^""]*""")]
    private static partial Regex LiteralText();
}
