using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>The colours a field takes from its theme when the host has not set them: the text and border colour and the page colour. A
    /// theme change (dark and light, high contrast) must redraw them, not only the caret and selection.</summary>
    public class ThemeColourTests
    {
        static (Window Window, RichTextField Field) Themed()
        {
            var doc = OneParagraph("HHHHHHHH");
            doc.Paragraphs[0].Props.SetInt(TextPropertyNumbers.BorderTop, 4000, 1);
            var field = new RichTextField(doc);
            field.Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
            {
                ["TextControlForeground"] = new SolidColorBrush(Color.Parse("#FF0000")),
                ["SystemControlBackgroundAltHighBrush"] = new SolidColorBrush(Color.Parse("#FFFFE0")),
            };
            field.Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
            {
                ["TextControlForeground"] = new SolidColorBrush(Color.Parse("#0000FF")),
                ["SystemControlBackgroundAltHighBrush"] = new SolidColorBrush(Color.Parse("#202020")),
            };
            var window = new Window { Width = 300, Height = 400, Content = field, RequestedThemeVariant = ThemeVariant.Light };
            window.Show();
            Headless.Pump();
            return (window, field);
        }

        /// <summary>The most saturated pixel of the first rows of the field: the text and the border have no other colour here.</summary>
        static Color Ink(Fixtures.Shot shot, int bottom)
        {
            Color best = Colors.Transparent;
            int bestScore = -1;
            for (int y = 0; y < bottom; y++)
                for (int x = 0; x < shot.Width; x++)
                {
                    var c = shot.Pixel(x, y);
                    int score = System.Math.Abs(c.R - c.B) + System.Math.Abs(c.R - c.G) + System.Math.Abs(c.B - c.G) + (255 - (c.R + c.G + c.B) / 3) / 4;
                    if (score > bestScore) { bestScore = score; best = c; }
                }
            return best;
        }

        [Fact]
        public void A_theme_change_redraws_the_border_and_text_in_the_new_foreground_and_the_page_in_the_new_background() => Headless.Run(() =>
        {
            var (window, field) = Themed();
            var light = Fixtures.Render(field);
            Assert.Equal(Color.Parse("#FFFFE0"), light.Pixel(290, 40));
            var border = light.Pixel(100, 1);
            Assert.True(border.R > 200 && border.B < 60, "the border has no colour of its own: it takes the foreground, " + border);
            var text = Ink(light, 30);
            Assert.True(text.R > 200 && text.B < 60, "light text " + text);

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Headless.Pump();
            var dark = Fixtures.Render(field);
            Assert.Equal(Color.Parse("#202020"), dark.Pixel(290, 40));
            border = dark.Pixel(100, 1);
            Assert.True(border.B > 200 && border.R < 60, "dark border " + border);
            text = Ink(dark, 30);
            Assert.True(text.B > 200 && text.R < 60, "dark text " + text);
            window.Close();
        });

        [Fact]
        public void A_foreground_or_background_the_host_set_is_not_changed_by_the_theme() => Headless.Run(() =>
        {
            var (window, field) = Themed();
            field.Foreground = Brushes.Green;
            field.Background = Brushes.White;
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Headless.Pump();
            var shot = Fixtures.Render(field);
            Assert.Equal(Colors.White, shot.Pixel(290, 40));
            var border = shot.Pixel(100, 1);
            Assert.True(border.G > 100 && border.R < 60 && border.B < 60, "the border stays green: " + border);
            window.Close();
        });

        [Fact]
        public void Setting_the_foreground_redraws_the_text_at_once() => Headless.Run(() =>
        {
            var (window, field) = Themed();
            field.Foreground = Brushes.Green;
            Headless.Pump();
            var border = Fixtures.Render(field).Pixel(100, 1);
            Assert.True(border.G > 100 && border.R < 60, "green border " + border);
            window.Close();
        });

        [Fact]
        public void A_theme_change_does_not_lay_the_paragraphs_out_again() => Headless.Run(() =>
        {
            // The default text colour is resolved when the text is drawn, so a switch costs a redraw and not a layout of every paragraph.
            var (window, field) = Themed();
            var layout = field.ParagraphLayoutFor(0);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Headless.Pump();
            Assert.Same(layout, field.ParagraphLayoutFor(0));
            var text = Ink(Fixtures.Render(field), 30);
            Assert.True(text.B > 200 && text.R < 60, "the text is drawn in the dark theme's colour " + text);
            window.RequestedThemeVariant = ThemeVariant.Light;
            Headless.Pump();
            Assert.Same(layout, field.ParagraphLayoutFor(0));
            text = Ink(Fixtures.Render(field), 30);
            Assert.True(text.R > 200 && text.B < 60, "and back in the light theme's " + text);
            window.Close();
        });

        /// <summary>Runs a body with the system saying it is in a high contrast theme with yellow text on a black page and a blue highlight.</summary>
        static void InHighContrast(System.Action body)
        {
            var isOn = SystemColours.Probe;
            var get = SystemColours.Get;
            SystemColours.Probe = () => true;
            SystemColours.Get = kind => kind == SystemColour.WindowText ? Color.Parse("#FFFF00") : kind == SystemColour.Window ? Color.Parse("#000000") : kind == SystemColour.Highlight ? Color.Parse("#1AEBFF") : Color.Parse("#000000");
            try { body(); }
            finally { SystemColours.Probe = isOn; SystemColours.Get = get; }
        }

        [Fact]
        public void In_high_contrast_the_default_text_and_page_are_the_system_window_text_and_window_colours() => Headless.Run(() => InHighContrast(() =>
        {
            var (window, field) = Themed();
            var shot = Fixtures.Render(field);
            Assert.Equal(Color.Parse("#000000"), shot.Pixel(290, 40));
            var text = Ink(shot, 30);
            Assert.True(text.R > 200 && text.G > 200 && text.B < 60, "yellow text, not the theme's " + text);
            var border = shot.Pixel(100, 1);
            Assert.True(border.R > 200 && border.G > 200 && border.B < 60, "and the border with no colour of its own " + border);
            Assert.Equal(Color.Parse("#1AEBFF"), ((ISolidColorBrush)field.ResolveSelectionBrush()).Color);
            window.Close();
        }));

        [Fact]
        public void A_change_to_or_from_high_contrast_redraws_the_default_colours_without_laying_the_text_out_again() => Headless.Run(() =>
        {
            var (window, field) = Themed();
            var layout = field.ParagraphLayoutFor(0);
            var before = Ink(Fixtures.Render(field), 30);
            Assert.True(before.R > 200 && before.B < 60 && before.G < 60, "red theme text " + before);
            InHighContrast(() =>
            {
                field.RefreshSystemColours();
                Headless.Pump();
                var shot = Fixtures.Render(field);
                Assert.Equal(Color.Parse("#000000"), shot.Pixel(290, 40));
                var text = Ink(shot, 30);
                Assert.True(text.G > 200, "yellow text " + text);
            });
            field.RefreshSystemColours();
            Headless.Pump();
            var after = Ink(Fixtures.Render(field), 30);
            Assert.True(after.R > 200 && after.G < 60, "red again " + after);
            Assert.Same(layout, field.ParagraphLayoutFor(0));
            window.Close();
        });

        [Fact]
        public void A_foreground_or_background_the_host_set_is_the_hosts_in_high_contrast_too() => Headless.Run(() => InHighContrast(() =>
        {
            var (window, field) = Themed();
            field.Foreground = Brushes.Green;
            field.Background = Brushes.White;
            var shot = Fixtures.Render(field);
            Assert.Equal(Colors.White, shot.Pixel(290, 40));
            var border = shot.Pixel(100, 1);
            Assert.True(border.G > 100 && border.R < 60 && border.B < 60, "the border stays green: " + border);
            window.Close();
        }));

        [Fact]
        public void Reading_the_foreground_gives_a_copy_that_a_theme_change_does_not_alter() => Headless.Run(() =>
        {
            var (window, field) = Themed();
            var read = field.Foreground;
            Assert.IsNotType<SolidColorBrush>(read);   // not the field's own, mutable brush
            var before = ((ISolidColorBrush)read).Color;
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Headless.Pump();
            Assert.Equal(before, ((ISolidColorBrush)read).Color);
            Assert.NotEqual(before, ((ISolidColorBrush)field.Foreground).Color);
            window.Close();
        });

        [Fact]
        public void Resetting_the_foreground_or_the_background_goes_back_to_following_the_theme() => Headless.Run(() =>
        {
            var (window, field) = Themed();
            field.Foreground = Brushes.Green;
            field.Background = Brushes.White;
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Headless.Pump();
            field.ResetForeground();
            field.ResetBackground();
            Headless.Pump();
            var shot = Fixtures.Render(field);
            Assert.Equal(Color.Parse("#202020"), shot.Pixel(290, 40));
            var text = Ink(shot, 30);
            Assert.True(text.B > 200 && text.R < 60, "the dark theme's text " + text);
            window.Close();
        });

        [Fact]
        public void A_host_that_sets_only_the_foreground_keeps_the_themes_page() => Headless.Run(() =>
        {
            // The contract (docs/reference/rendering.md): a colour the host sets is kept, and the other follows the theme, so a black foreground on a dark theme is
            // black on the dark page. A host that sets one sets the other, or resets it.
            var (window, field) = Themed();
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Headless.Pump();
            field.Foreground = Brushes.Black;
            var shot = Fixtures.Render(field);
            Assert.Equal(Color.Parse("#202020"), shot.Pixel(290, 40));
            window.Close();
        });
    }
}
