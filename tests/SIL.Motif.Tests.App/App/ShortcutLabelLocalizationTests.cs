using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ShortcutLabelLocalizationTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("de-DE")]
    public void HelpAndItsGeneratedGuideUseTheWindowCommandLabels(string locale)
    {
        var culture = CultureInfo.GetCultureInfo(locale);
        var help = HelpCatalog.Load(culture);
        Assert.Equal("Keyboard Shortcuts", help.Find(HelpEntryKind.Ui, "shortcut-page-title")!.Title);
        Assert.Equal("Analyze Texts", help.Find(HelpEntryKind.Ui, "shortcut-scope-analyze-texts")!.Title);
        Assert.Equal("Open the Word Card", help.Find(HelpEntryKind.Ui, "shortcut-open-word-card")!.Title);
        var page = help.GetHelpPage(HelpEntryKind.Guide, "keyboard-shortcuts")!;
        Assert.StartsWith("# Keyboard Shortcuts\n", page, StringComparison.Ordinal);
        Assert.Contains("Open the Word Card", page, StringComparison.Ordinal);
        Assert.Equal(KeyboardShortcutCatalog.RenderHelpPage(culture), page);
        Assert.Null(help.Find(HelpEntryKind.Ui, "shortcut-not-a-registered-control"));
    }

    [Fact]
    public void EveryShortcutAutomationIdReadsOneCanonicalWindowResource()
    {
        var assembly = typeof(HelpCatalog).Assembly;
        var resource = Assert.Single(assembly.GetManifestResourceNames(), name =>
            name.Replace('\\', '/') == "help/en/ui.json");
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var metadata = JsonDocument.Parse(stream);
        var ids = metadata.RootElement.EnumerateObject().Where(item =>
            item.Name.StartsWith("shortcut-", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(ids);
        var manager = new ResourceManager("SIL.Motif.App.MotifAppStrings", typeof(MotifAppStrings).Assembly);
        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("de-DE") })
        {
            var help = HelpCatalog.Load(culture);
            foreach (var item in ids)
            {
                Assert.False(item.Value.TryGetProperty("title", out _),
                    item.Name + " must not retain a second authored window title in Help JSON.");
                Assert.True(item.Value.TryGetProperty("titleResource", out var reference),
                    item.Name + " needs its canonical window resource reference.");
                var key = Assert.IsType<string>(reference.GetString());
                Assert.StartsWith("MotifApp.", key, StringComparison.Ordinal);
                var property = typeof(MotifAppStrings).GetProperty(key["MotifApp.".Length..],
                    BindingFlags.Public | BindingFlags.Static);
                Assert.NotNull(property);
                var title = manager.GetString(key, culture);
                Assert.False(string.IsNullOrWhiteSpace(title), item.Name + " has no canonical resource value.");
                Assert.Equal(title, help.Find(HelpEntryKind.Ui, item.Name)!.Title);
                Assert.Equal(item.Value.GetProperty("description").GetString(),
                    help.Find(HelpEntryKind.Ui, item.Name)!.Description);
            }
        }
    }

    [Theory]
    [InlineData(820)]
    [InlineData(1920)]
    public void TheRenderedSettingsShortcutRowsUseTheCanonicalCommandLabel(int width)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = FakeComposedWindow.Create();
            var settings = SettingsPageTests.WindowSettings(frame);
            var model = workspace.PageModel<SettingsPageModel>();
            model.Settings = settings;
            var page = Assert.IsType<SettingsPage>(PageRegistry.For(WorkspacePage.Settings).CreateView(model));
            var window = new MotifTestWindow { Width = width, Height = 780, Content = page };
            try
            {
                window.Show();
                workspace.ShowPageCommand.Execute(WorkspacePage.Settings);
                settings.OpenGroup(SettingsGroup.KeyboardShortcuts);
                page.ScrollToSelectedGroup();
                PageScreenshots.Settle(window);
                var search = page.FindControl<TextBox>("ShortcutSearchBox")!;
                search.Text = "Open the Word Card";
                PageScreenshots.Settle(window);
                Assert.Equal("Open the Word Card", settings.ShortcutSearch);
                Assert.Equal("Keyboard Shortcuts", KeyboardShortcutRegistry.Title);
                Assert.NotEmpty(settings.ShortcutEntries);
                Assert.All(settings.ShortcutEntries, entry => Assert.Equal("Open the Word Card", entry.Action));
                var list = SettingsTestAccess.ShortcutList(page);
                var rows = list.GetVisualDescendants().OfType<Border>()
                    .Where(row => row.Classes.Contains("settingsShortcutRow")).ToArray();
                Assert.Equal(settings.ShortcutEntries.Count, rows.Length);
                foreach (var row in rows)
                {
                    row.BringIntoView();
                    PageScreenshots.Settle(window);
                    Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.IsEffectivelyVisible && text.Text?.Contains("Open the Word Card", StringComparison.Ordinal) == true);
                }
            }
            finally
            {
                window.Close();
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }
}
