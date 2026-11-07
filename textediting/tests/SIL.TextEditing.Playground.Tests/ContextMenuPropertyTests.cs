using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using SIL.TextEditing.Avalonia;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>ADR 0025: a host builds its context menu from ContextMenuRequested or sets Avalonia's ContextMenu on the field. These
    /// need the Fluent theme (a menu needs an overlay layer), which this project loads.</summary>
    public class ContextMenuPropertyTests
    {
        static (Window Window, RichTextField Field, Point At) Open()
        {
            var doc = new Document();
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "hello world" });
            doc.Paragraphs.Add(p);
            var field = new RichTextField(doc) { PreferredWidth = 300 };
            var window = new Window { Width = 400, Height = 300, Content = field };
            window.Show();
            Headless.Pump();
            var r = field.ParagraphLayoutFor(0).SelectionRects(0, 2)[0];
            var at = field.TranslatePoint(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), window)!.Value;
            return (window, field, at);
        }

        static void RightClick(Window window, Point at)
        {
            window.MouseDown(at, MouseButton.Right);
            window.MouseUp(at, MouseButton.Right);
            Headless.Pump();
        }

        [Fact]
        public void A_host_that_does_not_use_the_event_still_gets_the_ContextMenu_it_set_on_the_field() => Headless.Run(() =>
        {
            var (window, field, at) = Open();
            var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "Host item" } } };
            field.ContextMenu = menu;
            RightClick(window, at);
            Assert.True(menu.IsOpen);
            menu.Close();
            window.Close();
        });

        [Fact]
        public void Shift_F10_opens_a_ContextMenu_a_host_set_without_listening_to_the_event() => Headless.Run(() =>
        {
            var (window, field, at) = Open();
            var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "Host item" } } };
            field.ContextMenu = menu;
            field.Focus();
            Headless.Pump();
            Assert.True(field.HandleKey(Key.F10, KeyModifiers.Shift));
            Headless.Pump();
            Assert.True(menu.IsOpen);
            menu.Close();
            window.Close();
        });

        [Fact]
        public void A_host_that_listens_gets_the_event_and_its_ContextMenu_is_not_opened_too() => Headless.Run(() =>
        {
            var (window, field, at) = Open();
            var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "Host item" } } };
            field.ContextMenu = menu;
            int requests = 0;
            field.ContextMenuRequested += (o, e) => requests++;
            RightClick(window, at);
            Assert.Equal(1, requests);
            Assert.False(menu.IsOpen);
            window.Close();
        });
    }
}