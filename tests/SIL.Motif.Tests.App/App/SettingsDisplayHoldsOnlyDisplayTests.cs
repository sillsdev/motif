using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsDisplayHoldsOnlyDisplayTests
{
    [Fact]
    public void TheSectionAfterDisplayIsKeyboardShortcutsAndSettingsHasNoAssistantSection()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                window.Show();
                var page = SettingsTestAccess.Open(window);
                PageScreenshots.Settle(window);
                var display = page.FindControl<StackPanel>("SettingsDisplaySection")!;
                Assert.Null(page.FindControl<StackPanel>("SettingsAssistantConnectionsSection"));
                Assert.DoesNotContain(display.GetVisualDescendants().OfType<TextBox>(), _ => true);
                var list = Assert.IsAssignableFrom<Panel>(display.Parent);
                var order = list.Children.Select(child => child.Name).Where(name => name?.EndsWith("Section", StringComparison.Ordinal) == true).ToList();
                Assert.Equal("SettingsKeyboardShortcutsSection", order[order.IndexOf("SettingsDisplaySection") + 1]);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(2));
    }
}
