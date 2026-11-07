using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    public class AutomationTests
    {
        static AutomationPeer Peer(RichTextField f) => ControlAutomationPeer.CreatePeerForElement(f);

        [Fact]
        public void The_peer_is_an_edit_with_the_host_name_and_the_text_as_its_value() => Headless.Run(() =>
        {
            var doc = Doc(Para("en", "en", "one"), Para("en", "en", "two"));
            doc.Paragraphs[1].Runs.Add(Run.Object("en", "\u0008pic"));
            var field = Fixtures.Field(doc);
            AutomationProperties.SetName(field, "Gloss");
            var peer = Peer(field);
            Assert.Equal(AutomationControlType.Edit, peer.GetAutomationControlType());
            Assert.Equal("Gloss", peer.GetName());
            Assert.True(peer.IsKeyboardFocusable());
            Assert.False(peer.HasKeyboardFocus());
            Assert.Equal("one\r\ntwo\uFFFC", ((IValueProvider)peer).Value);
            Assert.False(((IValueProvider)peer).IsReadOnly);
        });

        [Fact]
        public void The_peer_reports_the_field_level_read_only_flag_and_not_read_only_runs() => Headless.Run(() =>
        {
            var doc = OneParagraph("ab");
            doc.Paragraphs[0].Runs[0].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            var field = Fixtures.Field(doc);
            var peer = Peer(field);
            Assert.False(((IValueProvider)peer).IsReadOnly);
            field.IsReadOnly = true;
            Assert.True(((IValueProvider)peer).IsReadOnly);
            field.IsReadOnly = false;
            Assert.False(((IValueProvider)peer).IsReadOnly);
        });

        [Fact]
        public void Without_a_host_name_the_peer_has_no_hard_coded_one() => Headless.Run(() =>
        {
            Assert.DoesNotContain("Rich text field", Peer(Fixtures.Field(OneParagraph("a"))).GetName() ?? "");
        });

        [Fact]
        public void The_value_follows_typing() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("ab"));
            var peer = Peer(field);
            Assert.Equal("ab", ((IValueProvider)peer).Value);
            field.Editor.SetCaret(new TextPosition(0, 2));
            field.Type("c");
            Assert.Equal("abc", ((IValueProvider)peer).Value);
        });

        [Fact]
        public void The_peer_reports_keyboard_focus() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("ab"));
            var peer = Peer(field);
            Assert.False(peer.HasKeyboardFocus());
            Assert.True(peer.GetBoundingRectangle().Width > 0 && peer.GetBoundingRectangle().Height > 0);
            field.Focus();
            Headless.Pump();
            Assert.True(peer.HasKeyboardFocus());
            window.Close();
        });

        [Fact]
        public void Selection_and_caret_colours_follow_theme_resources_with_the_old_look_as_fallback() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("ab"));
            Assert.Equal(Colors.LightSkyBlue, ((ISolidColorBrush)field.ResolveSelectionBrush()).Color);
            Assert.Equal(Colors.Black, ((ISolidColorBrush)field.ResolveCaretBrush()).Color);
            field.Resources["TextControlSelectionHighlightColor"] = Color.Parse("#112233");
            field.Resources["TextControlForeground"] = new SolidColorBrush(Color.Parse("#445566"));
            Assert.Equal(Color.Parse("#112233"), ((ISolidColorBrush)field.ResolveSelectionBrush()).Color);
            Assert.Equal(Color.Parse("#445566"), ((ISolidColorBrush)field.ResolveCaretBrush()).Color);
        });

        [Fact]
        public void Selection_and_caret_repaint_in_the_new_colour_when_the_theme_variant_changes() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("abcdef"));
            var light = new ResourceDictionary { ["TextControlSelectionHighlightColor"] = Color.Parse("#FF0000") };
            var dark = new ResourceDictionary { ["TextControlSelectionHighlightColor"] = Color.Parse("#0000FF") };
            field.Resources.ThemeDictionaries[ThemeVariant.Light] = light;
            field.Resources.ThemeDictionaries[ThemeVariant.Dark] = dark;
            window.RequestedThemeVariant = ThemeVariant.Light;
            field.Editor.Anchor = new TextPosition(0, 0);
            field.Editor.Caret = new TextPosition(0, 6);
            field.InvalidateVisual();
            Headless.Pump();
            Assert.Equal(Color.Parse("#FF0000"), ((ISolidColorBrush)field.ResolveSelectionBrush()).Color);
            var before = SelectionPixel(window);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Headless.Pump();
            Assert.Equal(Color.Parse("#0000FF"), ((ISolidColorBrush)field.ResolveSelectionBrush()).Color);
            var after = SelectionPixel(window);
            Assert.True(before.R > before.B, "light variant paints red: " + before);
            Assert.True(after.B > after.R, "dark variant repaints blue");
            window.Close();
        });

        // A pixel inside the selection but outside the glyphs: just under the first line's top-left corner area.
        static Color SelectionPixel(global::Avalonia.Controls.Window window)
        {
            Headless.Pump();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame()!;
            using var fb = frame.Lock();
            int x = fb.Size.Width > 0 ? 1 : 0, y = 1;
            var px = System.Runtime.InteropServices.Marshal.ReadInt32(fb.Address, y * fb.RowBytes + x * 4);
            bool bgra = fb.Format == global::Avalonia.Platform.PixelFormat.Bgra8888;
            byte lo = (byte)px, hi = (byte)(px >> 16);
            return Color.FromArgb((byte)(px >> 24), bgra ? hi : lo, (byte)(px >> 8), bgra ? lo : hi);
        }
    }
}
