using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using SIL.Motif.App;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the window's real menu bar: FieldWorks' order and names, shortcuts shown from the registry and nowhere else,
/// access keys only in the resx values, and no data text where an underscore would be read as an access key.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MenuBarTests
{
    private readonly AvaloniaHeadlessFixture _avalonia;

    public MenuBarTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    private static readonly string[] TopLevel = ["File", "Edit", "View", "Tools", "Help"];

    /// <summary>Existing Content or Header bindings per file; a count may fall, never rise.</summary>
    private static readonly Dictionary<string, int> BoundContentBaseline = new()
    {
        ["Controls/WordPresentation/WordCard.axaml"] = 3,
        ["Tokens/Components/WordStripParts.axaml"] = 1,
        ["Views/DiagnosticPanel.axaml"] = 2,
        ["Views/GrammarPanel.axaml"] = 3,
        ["Views/HandoffPanel.axaml"] = 1,
        ["Views/Inspector.axaml"] = 6,
        ["Views/Pages/AiHandoffPage.axaml"] = 1,
        ["Views/Pages/AssistantConnectionsPage.axaml"] = 1,
        ["Views/Pages/OverviewPage.axaml"] = 1,
        ["Views/Pages/ParsimonyPage.axaml"] = 2,
        ["Views/Pages/ProposalsPage.axaml"] = 4,
        ["Views/Pages/TimingPage.axaml"] = 3,
        ["Views/ParsePrompt.axaml"] = 1,
        ["Views/RefusalBlock.axaml"] = 1,
        ["Views/ResultsInTextPanel.axaml"] = 5,
        ["Views/ReviewInspector.axaml"] = 5,
        ["Views/ReviewPanel.axaml"] = 12,
        ["Views/SettingsPage.axaml"] = 3,
        ["Views/SetupDialog.axaml"] = 2,
        ["Views/TextWordsPanel.axaml"] = 1,
        ["Views/TextsListsPanel.axaml"] = 3,
        ["Views/TraceAnalysesView.axaml"] = 1,
        ["Views/TryWordPanel.axaml"] = 3,
        ["Views/WarningsFilterPanel.axaml"] = 1,
        ["Views/WarningsInspectorPanel.axaml"] = 1,
        ["Views/WordDispositionButtons.axaml"] = 2,
    };

    private T OnWindow<T>(Func<MainWindow, T> read)
    {
        T result = default!;
        _avalonia.Invoke(() =>
        {
            var window = new MainWindow();
            try { result = read(window); }
            finally { window.Close(); }
        });
        return result;
    }

    private static string Plain(MenuItem item) => ((string)item.Header!).Replace("_", "", StringComparison.Ordinal);

    private static char AccessKey(MenuItem item)
    {
        var header = (string)item.Header!;
        Assert.Equal(1, header.Count(c => c == '_'));
        return char.ToUpperInvariant(header[header.IndexOf('_') + 1]);
    }

    private static IEnumerable<MenuItem> Items(ItemsControl parent) => parent.Items.OfType<MenuItem>();

    private static IEnumerable<ItemsControl> Levels(Menu menu)
    {
        yield return menu;
        foreach (var item in menu.GetLogicalDescendants().OfType<MenuItem>().Where(i => i.Items.OfType<MenuItem>().Any()))
            yield return item;
    }

    [Fact]
    public void TheMenuBarHasFileEditViewToolsHelpInFieldWorksOrder()
    {
        var (top, tools, help, file) = OnWindow(window =>
        {
            var menu = window.FindInWindowOrWorkspace<Menu>("MainMenu")!;
            return (Items(menu).Select(Plain).ToArray(),
                Items(window.FindInWindowOrWorkspace<MenuItem>("ToolsMenu")!).Select(Plain).ToArray(),
                Items(window.FindInWindowOrWorkspace<MenuItem>("HelpMenu")!).Select(Plain).ToArray(),
                Items(window.FindInWindowOrWorkspace<MenuItem>("FileMenu")!).Select(Plain).ToArray());
        });
        Assert.Equal(TopLevel, top);
        Assert.Equal(["Options…", "Writing Systems and Fonts"], tools);
        Assert.Equal(["Help Topics", "Keyboard Shortcuts", "Report a Problem…"], help);
        Assert.Equal(["Select New…", "Open Recent", "Refresh", "Configure…", "Exit"], file);
    }

    [Fact]
    public void TheProjectFlyoutIsGoneAndSettingsIsOptionsUnderTools()
    {
        OnWindow(window =>
        {
            Assert.Null(window.FindInWindowOrWorkspace<Button>("ProjectMenuButton"));
            var options = window.FindInWindowOrWorkspace<MenuItem>("OptionsMenuItem")!;
            Assert.Same(window.FindInWindowOrWorkspace<MenuItem>("ToolsMenu"), options.Parent);
            Assert.Equal("OpenSettings", options.Tag);
            return 0;
        });
    }

    [Fact]
    public void EveryMenuItemWithAShortcutShowsTheRegistrysGestureAndOthersShowNone()
    {
        var rows = OnWindow(window => window.FindInWindowOrWorkspace<Menu>("MainMenu")!.GetLogicalDescendants().OfType<MenuItem>()
            .Select(item => (Name: Plain(item), item.Tag, item.InputGesture)).ToArray());
        Assert.NotEmpty(rows);
        foreach (var (name, tag, gesture) in rows)
        {
            if (tag is not string behaviorName)
            {
                Assert.True(gesture is null, $"'{name}' names no registry behavior, so it must show no shortcut");
                continue;
            }
            var behavior = Enum.Parse<KeyboardShortcutBehavior>(behaviorName);
            var binding = KeyboardShortcutRegistry.FirstBindingFor(KeyboardShortcutScope.Window, behavior);
            Assert.True(binding is not null, $"'{name}' names {behaviorName}, which the registry does not bind in the Window scope");
            Assert.Equal(new KeyGesture(binding!.Key, binding.Modifiers), gesture);
        }
    }

    [Fact]
    public void TheMenusShowTheShortcutsFieldWorksUsersExpect()
    {
        var gestures = OnWindow(window => new[]
        {
            "UndoMenuItem", "RedoMenuItem", "CopyMenuItem", "ZoomInMenuItem", "ZoomOutMenuItem", "ResetZoomMenuItem",
            "OptionsMenuItem", "HelpTopicsMenuItem", "KeyboardShortcutsMenuItem",
        }.ToDictionary(name => name, name => window.FindInWindowOrWorkspace<MenuItem>(name)!.InputGesture));
        Assert.All(gestures, pair => Assert.True(pair.Value is not null, pair.Key + " shows no shortcut"));
        Assert.Equal(Key.F1, gestures["HelpTopicsMenuItem"]!.Key);
        Assert.Equal(Key.Z, gestures["UndoMenuItem"]!.Key);
        Assert.Equal(Key.OemComma, gestures["OptionsMenuItem"]!.Key);
    }

    [Fact]
    public void TheMenusOnlyNameShortcutsThatKeepTheirCatalogIds()
    {
        var ids = KeyboardShortcutRegistry.Bindings.Where(b => b.Scope == KeyboardShortcutScope.Window)
            .Select(b => b.DefinitionId).ToHashSet();
        foreach (var id in new[]
                 {
                     "window-help", "window-shortcuts", "window-settings", "window-zoom-in", "window-zoom-out",
                     "window-zoom-reset", "window-copy", "window-undo", "window-redo",
                 })
            Assert.Contains(id, ids);
    }

    [Fact]
    public void AccessKeysAreUniqueWithinEachMenuLevelAndHeadersComeFromTheResx()
    {
        var levels = OnWindow(window => Levels(window.FindInWindowOrWorkspace<Menu>("MainMenu")!)
            .Select(level => Items(level).Select(item => (Header: (string)item.Header!, Key: AccessKey(item))).ToArray())
            .ToArray());
        Assert.True(levels.Length >= 6, "the bar and its five menus each form a level");
        foreach (var level in levels)
            Assert.Equal(level.Length, level.Select(item => item.Key).Distinct().Count());
        var resx = typeof(MotifAppStrings).GetProperties().Where(p => p.Name.StartsWith("Menu", StringComparison.Ordinal))
            .Select(p => (string)p.GetValue(null)!).ToHashSet();
        Assert.All(levels.SelectMany(level => level), item => Assert.Contains(item.Header, resx));
    }

    [Fact]
    public void AnUnderscoreInAResxValueMarksAnAccessKeyOnlyInMenuStrings()
    {
        foreach (var property in typeof(MotifAppStrings).GetProperties().Where(p => p.PropertyType == typeof(string)))
        {
            // An email address's underscore is text, not an access key.
            var value = Regex.Replace((string)property.GetValue(null)!, @"\S+@\S+", string.Empty);
            // The project card's buttons carry access keys too: they are the card's commands, as menu items are the bar's.
            var carriesAccessKey = property.Name.StartsWith("Menu", StringComparison.Ordinal) &&
                !property.Name.EndsWith("Pattern", StringComparison.Ordinal) ||
                property.Name.StartsWith("ProjectCardButton", StringComparison.Ordinal) ||
                property.Name == nameof(MotifAppStrings.TechDemoNoticeGotIt);
            Assert.Equal(carriesAccessKey,
                value.Contains('_', StringComparison.Ordinal));
        }
    }

    [Fact]
    public void MenuHeadersInTheWindowXamlAreResxReferencesNeverLiteralsOrBindings()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "SIL.Motif.App", "Views", "MainWindow.axaml"));
        var headers = Regex.Matches(xaml, @"<MenuItem\b[^>]*?\bHeader=""([^""]*)""").Select(m => m.Groups[1].Value).ToArray();
        Assert.NotEmpty(headers);
        Assert.All(headers, header => Assert.StartsWith("{x:Static app:MotifAppStrings.Menu", header, StringComparison.Ordinal));
        Assert.DoesNotMatch(@"(Content|Header)=""\{Binding", xaml);
    }

    [Fact]
    public void DataBoundTextIsNeverTheContentOrHeaderStringOfAControl()
    {
        // In a string Content or Header an underscore is an access key; data text goes in a TextBlock.
        var appRoot = Path.Combine(RepoRoot(), "src", "SIL.Motif.App");
        var found = Directory.EnumerateFiles(appRoot, "*.axaml", SearchOption.AllDirectories)
            .Select(path => (Path: Path.GetRelativePath(appRoot, path).Replace('\\', '/'),
                Count: Regex.Matches(File.ReadAllText(path), @"(Content|Header)=""\{Binding").Count))
            .Where(row => row.Count > 0).ToArray();
        foreach (var (path, count) in found)
        {
            Assert.True(BoundContentBaseline.TryGetValue(path, out var allowed),
                $"{path} binds data text to Content or Header; put it in a TextBlock or Run");
            Assert.True(count <= allowed, $"{path} binds {count} Content or Header strings; the baseline is {allowed}");
        }
    }

    [Fact]
    public void TheProjectNameIsATextBlockSoAnUnderscoreInItIsNotAnAccessKey()
    {
        OnWindow(window =>
        {
            var label = window.FindInWindowOrWorkspace<TextBlock>("ProjectNameLabel");
            Assert.NotNull(label);
            Assert.Null(label!.Text);
            return 0;
        });
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Motif.sln was not found above the test output.");
    }
}
