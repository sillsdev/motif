using Avalonia.Media;
using SIL.TextEditing.Avalonia;

namespace SIL.TextEditing.Playground
{
    /// <summary>The playground's colours for the environment notation's token kinds: a chip behind the glyphs for each kind, a pale one on the light theme and a
    /// stronger one on the dark theme (the host decides; the field only follows the theme). Not shipped.</summary>
    public static class SampleTokenStyles
    {
        static SolidColorBrush Brush(byte a, byte r, byte g, byte b) => new SolidColorBrush(Color.FromArgb(a, r, g, b));

        public static TokenStyleMap Environment() => new TokenStyleMap()
            .Set(SampleEnvironmentService.Kinds.Class, new TokenStyle(Brush(70, 60, 120, 255), Brush(160, 40, 90, 230)), new TokenStyle(Brush(110, 80, 140, 255), Brush(200, 130, 170, 255)))
            .Set(SampleEnvironmentService.Kinds.Segment, new TokenStyle(Brush(60, 40, 170, 80)), new TokenStyle(Brush(90, 60, 200, 100)))
            .Set(SampleEnvironmentService.Kinds.Boundary, new TokenStyle(Brush(80, 255, 150, 0), Brush(170, 220, 110, 0)), new TokenStyle(Brush(110, 255, 170, 40), Brush(200, 255, 180, 70)))
            .Set(SampleEnvironmentService.Kinds.Slot, new TokenStyle(Brush(60, 150, 60, 200), Brush(170, 120, 40, 170)), new TokenStyle(Brush(100, 180, 100, 230), Brush(210, 190, 130, 240)))
            .Set(SampleEnvironmentService.Kinds.Optional, new TokenStyle(Brush(50, 120, 120, 120)), new TokenStyle(Brush(90, 170, 170, 170)))
            .Set(SampleEnvironmentService.Kinds.Punct, new TokenStyle(null, Brush(130, 110, 110, 110)), new TokenStyle(null, Brush(190, 190, 190, 190)))
            .Set(SampleEnvironmentService.Kinds.Unknown, new TokenStyle(Brush(40, 255, 0, 0), Brush(190, 220, 0, 0)), new TokenStyle(Brush(80, 255, 60, 60), Brush(220, 255, 90, 90)));
    }
}
