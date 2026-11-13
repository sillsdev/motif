using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Drawing a field never invalidates it: Avalonia throws when a visual is invalidated during the render pass.</summary>
    public class RenderPassInvalidationTests
    {
        static string Words(string stem, int count) => string.Join(" ", Enumerable.Repeat(stem, count));

        [Fact]
        public void A_field_below_the_viewport_that_the_window_resizes_under_is_drawn_without_invalidating_during_the_render_pass() => Headless.Run(() =>
        {
            var field = new RichTextField(Doc(Para("en", "en", Words("word", 100)))) { WritingSystems = Fixtures.SixteenPoint() };
            var panel = new StackPanel();
            panel.Children.Add(new Border { Height = 2000 });
            panel.Children.Add(field);
            var scroller = new ScrollViewer { Content = panel };
            var window = new Window { Width = 400, Height = 300, Content = scroller };
            window.Show();
            Headless.Pump();
            window.Width = 1200;
            Headless.Pump();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (window.CaptureRenderedFrame()) { }
            Headless.Pump();
            Assert.True(field.IsMeasureValid);   // the height the drawing found was asked for after it, and is measured now
            window.Close();
        });
    }
}
