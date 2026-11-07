using System.Collections.Generic;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>ADR 0008: the field announces the writing system the host should switch the keyboard to. A host also needs it when
    /// the field gains focus, because the keyboard in effect then belongs to whatever had focus before.</summary>
    public class ActiveWsEventTests
    {
        static (global::Avalonia.Controls.Window Window, RichTextField Field, global::Avalonia.Controls.Button Other) WithOther()
        {
            var field = new RichTextField(Basic());
            var other = new global::Avalonia.Controls.Button { Content = "other" };
            var panel = new global::Avalonia.Controls.StackPanel();
            panel.Children.Add(other);
            panel.Children.Add(field);
            var window = new global::Avalonia.Controls.Window { Width = 520, Height = 400, Content = panel };
            window.Show();
            Headless.Pump();
            return (window, field, other);
        }

        [Fact]
        public void Gaining_focus_announces_the_writing_system_at_the_caret_once_with_the_same_old_and_new() => Headless.Run(() =>
        {
            var (window, field, other) = WithOther();
            try
            {
                field.Editor.SetCaret(new TextPosition(0, 9));   // inside "bonjour" (fr)
                var seen = new List<string>();
                field.ActiveWsChanged += (o, n) => seen.Add(o + ">" + n);
                other.Focus();   // the field had focus when the window opened
                Headless.Pump();
                Assert.False(field.IsFocused);
                field.Focus();
                Headless.Pump();
                Assert.True(field.IsFocused);
                Assert.Equal(new[] { "fr>fr" }, seen);   // the keyboard the host has may be another field's: switch to fr

                field.Focus();                           // already focused: nothing to announce
                field.Editor.SetCaret(new TextPosition(0, 10));
                Assert.Equal(new[] { "fr>fr" }, seen);   // and moving inside the run says nothing

                other.Focus();                           // losing focus announces nothing; gaining it again does
                Headless.Pump();
                Assert.False(field.IsFocused);
                Assert.Equal(new[] { "fr>fr" }, seen);
                field.Editor.SetCaret(new TextPosition(0, 2));
                Assert.Equal(new[] { "fr>fr" }, seen);   // a change while it has no focus is not reported (#59)
                field.Focus();
                Headless.Pump();
                Assert.Equal(new[] { "fr>fr", "en>en" }, seen);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void An_unfocused_field_announces_nothing_however_its_writing_system_changes_and_focus_gain_announces_where_it_ended() => Headless.Run(() =>
        {
            var (window, field, other) = WithOther();
            try
            {
                other.Focus();
                Headless.Pump();
                Assert.False(field.IsFocused);
                var seen = new List<string>();
                field.ActiveWsChanged += (o, n) => seen.Add(o + ">" + n);
                field.Editor.SetCaret(new TextPosition(0, 9));      // caret into fr
                field.Editor.SetCaret(new TextPosition(0, 2), true); // selection whose first ws changes
                field.Editor.SetActiveWs("fr");                     // the toolbar
                field.Editor.SetCaret(new TextPosition(0, 9));
                Assert.Empty(seen);
                Assert.Equal("fr", field.Editor.ActiveWs);

                field.Focus();
                Headless.Pump();
                Assert.Equal(new[] { "fr>fr" }, seen);   // one announcement, of where the field ended up
                field.Editor.SetCaret(new TextPosition(0, 2));
                Assert.Equal(new[] { "fr>fr", "fr>en" }, seen);   // focused again: changes are reported
            }
            finally { window.Close(); }
        });

        [Fact]
        public void Focus_then_a_caret_move_into_another_writing_system_announces_the_focus_and_then_the_change() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(Basic());
            try
            {
                field.Editor.SetCaret(new TextPosition(0, 2));
                var seen = new List<string>();
                field.ActiveWsChanged += (o, n) => seen.Add(o + ">" + n);
                field.Focus();
                Headless.Pump();
                field.Editor.SetCaret(new TextPosition(0, 9));
                Assert.Equal(new[] { "en>en", "en>fr" }, seen);
            }
            finally { window.Close(); }
        });
    }
}
