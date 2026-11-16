using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Views.Settings;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsDisplayHoldsOnlyDisplayTests
{
    [Fact]
    public void DisplayHoldsNoAssistantSetupAndTheNextSectionIsKeyboardShortcuts()
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
                var assistants = page.FindControl<StackPanel>("SettingsAssistantConnectionsSection")!;
                Assert.Empty(display.GetVisualDescendants().OfType<AssistantConnectionsPanel>());
                Assert.DoesNotContain(display.GetVisualDescendants().OfType<TextBox>(), _ => true);
                var list = Assert.IsAssignableFrom<Panel>(display.Parent);
                var order = list.Children.Select(child => child.Name).Where(name => name?.EndsWith("Section", StringComparison.Ordinal) == true).ToList();
                Assert.Equal("SettingsKeyboardShortcutsSection", order[order.IndexOf("SettingsDisplaySection") + 1]);
                Assert.True(order.IndexOf("SettingsWritingSystemsSection") < order.IndexOf("SettingsAssistantConnectionsSection"));
                var title = assistants.GetVisualDescendants().OfType<TextBlock>().First();
                Assert.Equal(MotifAppStrings.AssistantConnectionsHeading, title.Text);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(2));
    }
}
