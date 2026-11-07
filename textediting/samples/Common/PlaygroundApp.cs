using Avalonia;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace SIL.TextEditing.Samples
{
    /// <summary>The Avalonia application both sample hosts run: the Fluent theme, light variant.</summary>
    public class PlaygroundApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Light;
        }

        /// <summary>The Avalonia generation this process runs, for window titles: 11 or 12.</summary>
        public static string AvaloniaVersion => typeof(Application).Assembly.GetName().Version!.ToString(3);
    }
}
