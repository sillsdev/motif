using Avalonia.Input;
using SIL.TextEditing.Tests;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>ADR 0021 (amended): when the host toolkit gives focus to the control that holds the field and Avalonia has nothing focused, the field takes it on the first key.</summary>
    public class HostFocusTests
    {
        static KeyEventArgs KeyOn(InputElement target) => new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, Source = target };

        [Fact]
        public void A_key_that_reaches_the_top_level_with_nothing_focused_gives_the_field_focus() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(Doc(Para("en", "en", "one")));
            field.Focus();
            Assert.True(field.IsFocused);
            field.IsEnabled = false;   // disabling the focused control is how a headless window ends up with nothing focused
            field.IsEnabled = true;
            Assert.Null(window.FocusManager!.GetFocusedElement());

            window.RaiseEvent(KeyOn(window));

            Assert.True(field.IsFocused);
        });

        [Fact]
        public void A_field_the_host_focused_itself_keeps_focus_when_a_key_arrives() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(Doc(Para("en", "en", "one")));
            field.Focus();

            field.RaiseEvent(KeyOn(field));

            Assert.True(field.IsFocused);
        });
    }
}
