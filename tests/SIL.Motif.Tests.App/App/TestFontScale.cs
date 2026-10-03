using Avalonia;
using Avalonia.Controls;

namespace SIL.Motif.Tests.App;

internal static class TestFontScale
{
    private const double CrossPlatformIncrease = 1.08;

    private static readonly string[] TypographyResources =
    [
        "Intent.Type.Title",
        "Intent.Type.Section",
        "Intent.Type.Brand",
        "Intent.Type.Navigation",
        "Intent.Type.Body",
        "Intent.Type.Status",
        "Intent.Type.Small",
        "Intent.Type.Caption",
        "Intent.Type.Label",
        "Intent.Type.Dense",
        "Intent.Type.Micro",
        "Component.ActionChip.Type",
        "Component.Density.CompactType",
        "Component.Density.NormalType",
        "Component.Handoff.TitleSize",
        "Component.HoverReveal.ButtonType",
        "Component.HoverReveal.LinkType",
        "Component.Mark.GlyphType",
        "Component.OpinionMark.Type",
        "Component.PanGlossLine.ExtraType",
        "Component.PanGlossLine.Type",
        "Component.StagedStrip.Type",
        "Component.WordCard.HeadingType",
        "Component.WordStrip.MorphType",
        "Component.WordStrip.NoteType",
    ];

    public static void ApplyEightPercentIncrease(Window window)
    {
        ApplyTypographyScale(window, CrossPlatformIncrease);
    }

    private static void ApplyTypographyScale(Window window, double scale, IReadOnlyCollection<string>? selected = null)
    {
        var application = Application.Current ?? throw new InvalidOperationException("Avalonia is not initialized.");
        foreach (var key in selected ?? TypographyResources)
        {
            if (application.FindResource(key) is not double baseSize)
                throw new InvalidOperationException($"Typography resource '{key}' is not a font size.");
            window.Resources[key] = baseSize * scale;
        }
    }
}
