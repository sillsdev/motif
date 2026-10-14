using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class LegacyActionVisibilityTests(AvaloniaHeadlessFixture avalonia)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedActionLinksAreVisibleAndClickableBeforeHoverOrFocus(bool dark)
    {
        avalonia.Invoke(() =>
        {
            var previous = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var outside = new TextBox { Text = "Focus outside the action row" };
            var action = new Button { Content = "Inspect the recorded rule",
                Classes = { "revealControl", "revealLink", "revealOnHover" } };
            var owner = new Border { Classes = { "hoverReveal" }, Child = action };
            var window = new Window { Width = 640, Height = 600,
                Content = new StackPanel { Children = { outside, owner } } };
            var clicks = 0;
            action.Click += (_, _) => clicks++;
            try
            {
                window.Show();
                outside.Focus();
                window.MouseMove(new Point(630, 590));
                PageScreenshots.Settle(window);
                if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is { } folder)
                {
                    Directory.CreateDirectory(folder);
                    using var frame = window.CaptureRenderedFrame()!;
                    frame.Save(Path.Combine(folder, $"shared-actions-at-rest-{(dark ? "dark" : "light")}.png"),
                        PngBitmapEncoderOptions.Default);
                }
                Assert.False(owner.IsPointerOver);
                Assert.False(owner.IsKeyboardFocusWithin);
                Assert.Equal(1, action.Opacity);
                Assert.True(action.IsHitTestVisible);
                HeadlessClick.Click(window, action, "Inspect the recorded rule");
                Assert.Equal(1, clicks);
            }
            finally { window.Close(); Application.Current.RequestedThemeVariant = previous; }
        });
    }
}
