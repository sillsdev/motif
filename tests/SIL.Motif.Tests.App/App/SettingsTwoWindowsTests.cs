using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Services;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsTwoWindowsTests
{
    [Fact]
    public void SettingsHostsDiagnosticsWithoutAssistantSetupOrASecondSourcesEditor()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var preferences = new MemoryUserPreferencesStore();
            var (workspace, window) = FakeComposedWindow.Create(preferencesStore: preferences);
            try
            {
                window.Show();
                var page = SettingsTestAccess.Open(window);
                PageScreenshots.Settle(window);
                var settings = Assert.IsType<SettingsViewModel>(page.DataContext);
                Assert.Null(page.FindControl<StackPanel>("SettingsTextsSection"));
                Assert.Null(page.FindControl<Button>("UseAllTextsButton"));
                Assert.Null(page.FindControl<StackPanel>("SettingsAssistantConnectionsSection"));
                Assert.False(settings.Diagnostics);
                settings.Diagnostics = true;
                Assert.True(preferences.Current.Diagnostics);
                settings.Diagnostics = false;
                Assert.False(workspace.Context.Diagnostics);
                Assert.False(preferences.Current.Diagnostics);
                workspace.Context.Diagnostics = true;
                Assert.True(settings.Diagnostics);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(2));
    }
}
