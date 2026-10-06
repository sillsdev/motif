using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Reflection;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class KeyboardInteractionContractTests(AvaloniaHeadlessFixture avalonia)
{
    private static ShortcutPlatform CurrentPlatform => OperatingSystem.IsMacOS()
        ? ShortcutPlatform.MacOS
        : OperatingSystem.IsWindows() ? ShortcutPlatform.Windows : ShortcutPlatform.Linux;
    private static string PrimarySearchGesture => KeyboardShortcutCatalog.FormatGesture(
        new ShortcutGesture(ShortcutKey.F, ShortcutModifiers.Primary), CurrentPlatform, CultureInfo.CurrentUICulture);

    [Fact]
    public void ShortcutCatalogMatchesRegisteredHandlersAndGeneratedHelp()
    {
        var assembly = typeof(KeyboardShortcutRegistry).Assembly;
        var handlers = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => method.IsDefined(typeof(KeyboardShortcutHandlerAttribute), inherit: false))
            .ToArray();
        var keyEventHandlers = assembly.GetTypes()
            .Where(type => type.Namespace == "SIL.Motif.App.Views")
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(KeyEventArgs)))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(keyEventHandlers, handlers.Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .Order(StringComparer.Ordinal));

        var catalogActions = KeyboardShortcutRegistry.Bindings
            .Select(binding => $"{binding.Scope}:{binding.Behavior}")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var witnessedActions = handlers.SelectMany(method => method
                .GetCustomAttribute<KeyboardShortcutHandlerAttribute>()!.ActionScopes)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(catalogActions, witnessedActions);
        var scopedGestures = KeyboardShortcutRegistry.Bindings
            .Select(binding => (binding.Scope, binding.Key, binding.Gesture, binding.Behavior))
            .ToArray();
        Assert.Equal(scopedGestures.Length, scopedGestures.Distinct().Count());

        Assert.Equal(KeyboardShortcutRegistry.Bindings
            .GroupBy(binding => (binding.DefinitionId, binding.Scope))
            .Select(group => group.First().Row), KeyboardShortcutRegistry.Entries);
        Assert.All(KeyboardShortcutRegistry.Bindings, binding =>
            Assert.Same(binding, KeyboardShortcutRegistry.Find(binding.Scope, binding.Key, binding.Modifiers,
                targetBehaviors: [binding.Behavior])));
        Assert.Null(KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Matrix, Key.Enter, KeyModifiers.None));
        Assert.Equal(KeyboardShortcutBehavior.ToggleMatrixCell,
            KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Matrix, Key.Enter, KeyModifiers.None,
                targetBehaviors: [KeyboardShortcutBehavior.ToggleMatrixCell])?.Behavior);
        Assert.Equal(KeyboardShortcutBehavior.OpenWordCard,
            KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Matrix, Key.Enter, KeyModifiers.None,
                targetBehaviors: [KeyboardShortcutBehavior.OpenWordCard])?.Behavior);
        Assert.Equal(new[] { "Action", "Gesture", "Key", "Scope" },
            typeof(KeyboardShortcutEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name).Order(StringComparer.Ordinal));

        var rows = KeyboardShortcutRegistry.Entries.ToHashSet();
        var wordMotion = new KeyboardShortcutEntry("← or →", "← or →", "Analyze texts", "Move between words");
        Assert.Contains(wordMotion, rows);
        Assert.Equal("Move between words (← or →)", KeyboardShortcutRegistry.TooltipFor(wordMotion));
        Assert.Contains(new KeyboardShortcutEntry("↑", "↑", "Analyze texts",
            "Move focus to the first word on the previous line"), rows);
        Assert.Contains(new KeyboardShortcutEntry("↓", "↓", "Analyze texts",
            "Move focus to the first word on the next line"), rows);
        foreach (var (key, action) in new[] { ("A", "Stage Approve"), ("D", "Stage Disapprove"), ("U", "Stage Unknown") })
        foreach (var scope in new[] { "Analyze texts", "Word list", "Lists" })
            Assert.Contains(new KeyboardShortcutEntry(key, key, scope, action), rows);

        var expectedSettingsKeys = new List<(string Key, string Gesture, string Scope)>
        {
            ("← or →", "← or →", "Analyze texts"),
            ("↑", "↑", "Analyze texts"), ("↓", "↓", "Analyze texts"),
            ("Enter or Space", "Enter or Space", "Analyze texts"),
            ("Page Up", "Page Up", "Analyze texts"), ("Page Down", "Page Down", "Analyze texts"),
            ("Home", "Home", "Analyze texts"), ("End", "End", "Analyze texts"),
            ("Enter", "Enter", "Analyze texts"), ("Escape", "Esc", "Analyze texts"),
        };
        var primarySearchGesture = PrimarySearchGesture;
        foreach (var scope in new[] { "Word list", "Lists" })
        {
            expectedSettingsKeys.AddRange(new[]
            {
                ("Enter or Space", "Enter or Space", scope),
                ("↑", "↑", scope), ("↓", "↓", scope),
                ("Page Up", "Page Up", scope), ("Page Down", "Page Down", scope),
                ("Home", "Home", scope), ("End", "End", scope), ("Escape", "Esc", scope),
                ("F", primarySearchGesture, scope), ("A", "A", scope), ("D", "D", scope), ("U", "U", scope),
            });
        }
        expectedSettingsKeys.AddRange(new[]
        {
            ("↑", "↑", "Diagnostic tree"), ("↓", "↓", "Diagnostic tree"),
            ("Escape", "Esc", "Analyze texts"), ("F", primarySearchGesture, "Analyze texts"),
            ("A", "A", "Analyze texts"), ("D", "D", "Analyze texts"), ("U", "U", "Analyze texts"),
            ("F", primarySearchGesture, "Matrix"), ("Enter or Space", "Enter or Space", "Matrix"),
            ("Enter or Space", "Enter or Space", "Matrix"),
            ("↑", "↑", "Matrix"), ("↓", "↓", "Matrix"),
            ("Page Up", "Page Up", "Matrix"), ("Page Down", "Page Down", "Matrix"),
            ("Home", "Home", "Matrix"), ("End", "End", "Matrix"), ("Escape", "Esc", "Matrix"),
            ("Enter or Space", "Enter or Space", "Review changes"),
            ("↑", "↑", "Review changes"), ("↓", "↓", "Review changes"),
            ("Page Up", "Page Up", "Review changes"), ("Page Down", "Page Down", "Review changes"),
            ("Home", "Home", "Review changes"), ("End", "End", "Review changes"),
            ("Escape", "Esc", "Review changes"),
            ("Enter or Space", "Enter or Space", "Matrix preview"),
            ("Enter or Space", "Enter or Space", "Morpheme"),
            ("F1", "F1", "Window"), ("Escape", "Esc", "Window"), ("?", "Shift+/", "Window"),
            (",", "Ctrl+,", "Window"),
            ("= or Keypad +", "Ctrl+= or Ctrl+Shift+= or Ctrl+Keypad +", "Window"),
            ("- or Keypad -", "Ctrl+- or Ctrl+Keypad -", "Window"),
            ("0 or Keypad 0", "Ctrl+0 or Ctrl+Keypad 0", "Window"),
            ("Escape", "Esc", "Settings"),
            ("Enter", "Enter", "Settings"), ("Escape", "Esc", "Settings"),
            ("← or →", "← or →", "Settings"),
            ("Tab", "Tab", "Settings"), ("Tab", "Shift+Tab", "Settings"),
        });
        Assert.Equal(expectedSettingsKeys.OrderBy(row => (row.Scope, row.Key, row.Gesture)),
            KeyboardShortcutRegistry.Entries.Select(row => (row.Key, row.Gesture, row.Scope))
                .OrderBy(row => (row.Scope, row.Key, row.Gesture)));

        var helpRoot = Path.Combine(RepositoryRoot(), "src", "SIL.Motif.Help", "Content");
        var help = File.ReadAllText(Path.Combine(helpRoot, "en", "guide",
            "keyboard-shortcuts.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(KeyboardShortcutCatalog.RenderHelpPage(CultureInfo.GetCultureInfo("en")), help);
        foreach (var localeDirectory in Directory.GetDirectories(helpRoot)
                     .Where(path => File.Exists(Path.Combine(path, "ui.json"))))
        {
            var culture = CultureInfo.GetCultureInfo(Path.GetFileName(localeDirectory));
            var catalog = HelpCatalog.Load(culture);
            Assert.Equal(KeyboardShortcutCatalog.RenderHelpPage(culture),
                catalog.GetHelpPage(HelpEntryKind.Guide, "keyboard-shortcuts"));
        }
    }

    [Fact]
    public void PrimaryModifierResolvesByPlatformAndRejectsExtraModifiers()
    {
        Assert.Equal(KeyboardShortcutBehavior.FocusWordSearch,
            KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, Key.F, KeyModifiers.Control,
                ShortcutPlatform.Windows)?.Behavior);
        Assert.Equal(KeyboardShortcutBehavior.FocusWordSearch,
            KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, Key.F, KeyModifiers.Control,
                ShortcutPlatform.Linux)?.Behavior);
        Assert.Equal(KeyboardShortcutBehavior.FocusWordSearch,
            KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, Key.F, KeyModifiers.Meta,
                ShortcutPlatform.MacOS)?.Behavior);
        Assert.Null(KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, Key.F, KeyModifiers.Control,
            ShortcutPlatform.MacOS));
        Assert.Null(KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, Key.F,
            KeyModifiers.Control | KeyModifiers.Shift, ShortcutPlatform.Windows));
        Assert.Null(KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, Key.A,
            KeyModifiers.Control, ShortcutPlatform.Windows));
        Assert.Equal("Ctrl+F", KeyboardShortcutCatalog.FormatGesture(
            new ShortcutGesture(ShortcutKey.F, ShortcutModifiers.Primary), ShortcutPlatform.Linux,
            CultureInfo.GetCultureInfo("en")));
        Assert.Equal("⌘+F", KeyboardShortcutCatalog.FormatGesture(
            new ShortcutGesture(ShortcutKey.F, ShortcutModifiers.Primary), ShortcutPlatform.MacOS,
            CultureInfo.GetCultureInfo("en")));
        Assert.Equal("= or Keypad +", KeyboardShortcutCatalog.FormatKeyLabels(
            [ShortcutKey.OemPlus, ShortcutKey.OemPlus, ShortcutKey.Add], CultureInfo.GetCultureInfo("en")));
    }

    [Fact]
    public void ViewKeyHandlersHaveRegisteredActionWitnessesAndNoLiteralKeyBranches()
    {
        var root = RepositoryRoot();
        var viewFiles = Directory.GetFiles(Path.Combine(root, "src", "SIL.Motif.App", "Views"), "*.cs",
            SearchOption.AllDirectories);
        var literalKeyBranch = new Regex(@"(?:\.Key\s*(?:==|!=|is\s+Key\.)|switch\s*\(\s*\w+\.Key\s*\))", RegexOptions.CultureInvariant);
        Assert.All(viewFiles, path => Assert.False(literalKeyBranch.IsMatch(File.ReadAllText(path)), path));
        var handlerWitnesses = typeof(KeyboardShortcutRegistry).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => method.IsDefined(typeof(KeyboardShortcutHandlerAttribute), inherit: false))
            .Select(handler =>
            {
                var typeName = handler.DeclaringType!.Name;
                var sourcePath = viewFiles.Single(path =>
                {
                    var name = Path.GetFileName(path);
                    return name == $"{typeName}.cs" || name == $"{typeName}.axaml.cs";
                });
                var source = File.ReadAllText(sourcePath);
                return (ActionScopes: handler.GetCustomAttribute<KeyboardShortcutHandlerAttribute>()!.ActionScopes,
                    Body: MethodBody(source, handler.Name));
            }).ToArray();
        foreach (var witness in handlerWitnesses)
        {
            Assert.Contains("KeyboardShortcutRegistry.Find(", witness.Body, StringComparison.Ordinal);
            foreach (var actionScope in witness.ActionScopes)
            {
                var separator = actionScope.LastIndexOf(':');
                Assert.True(separator > 0, $"Invalid scoped action '{actionScope}'.");
                var behavior = actionScope[(separator + 1)..];
                Assert.Contains($"KeyboardShortcutBehavior.{behavior}", witness.Body, StringComparison.Ordinal);
            }
        }
        Assert.All(KeyboardShortcutRegistry.Bindings, binding => Assert.Contains(handlerWitnesses,
            witness => witness.ActionScopes.Contains($"{binding.Scope}:{binding.Behavior}", StringComparer.Ordinal)));
        Assert.All(KeyboardShortcutRegistry.Bindings, binding =>
            Assert.Contains(binding.ActionId, Enum.GetNames<KeyboardShortcutBehavior>()));
        var approve = Assert.Single(KeyboardShortcutRegistry.Bindings,
            binding => binding.Scope == KeyboardShortcutScope.TextReader && binding.Key == Key.A);
        Assert.False(KeyboardShortcutRegistry.Allows(approve, isTextInput: true, hasFocusedItem: true));
        Assert.True(KeyboardShortcutRegistry.Allows(approve, isTextInput: false, hasFocusedItem: true));
        Assert.True(KeyboardShortcutRegistry.IsTextInput(new NumericUpDown()));
        Assert.True(KeyboardShortcutRegistry.IsTextInput(new TextBox()));
        var search = Assert.Single(KeyboardShortcutRegistry.Bindings,
            binding => binding.Scope == KeyboardShortcutScope.TextReader
                && binding.Behavior == KeyboardShortcutBehavior.FocusWordSearch);
        Assert.True(KeyboardShortcutRegistry.Allows(search, isTextInput: true, hasFocusedItem: true));
        var focusedItem = Assert.Single(KeyboardShortcutRegistry.Bindings,
            binding => binding.Scope == KeyboardShortcutScope.TextReader && binding.Key == Key.Home);
        Assert.False(KeyboardShortcutRegistry.Allows(focusedItem, isTextInput: false, hasFocusedItem: false));
    }

    private static string MethodBody(string source, string methodName)
    {
        var methodStart = source.IndexOf($"{methodName}(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find the source for {methodName}.");
        var openingBrace = source.IndexOf('{', methodStart);
        Assert.True(openingBrace >= 0, $"Could not find the body for {methodName}.");
        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            if (source[index] != '}') continue;
            depth--;
            if (depth == 0) return source[openingBrace..(index + 1)];
        }
        Assert.Fail($"Could not find the end of {methodName}.");
        return string.Empty;
    }

    [Theory]
    [InlineData("Approve", ReadingGrade.Candidate, ChangeKinds.Approve)]
    [InlineData("Disapprove", ReadingGrade.Approved, ChangeKinds.Reject)]
    [InlineData("Unknown", ReadingGrade.Approved, ChangeKinds.Candidate)]
    public async Task OpinionShortcutStagesOneMatchingFixChoice(string behavior, string opinion, string kind)
    {
        PendingStoredOpinionChange? staged = null;
        var routes = new WordRowRoutes { StageOpinion = change => { staged = change; return Task.FromResult(true); } };
        var analysis = new KeyboardStoredAnalysis("analysis-1", opinion,
            [new ParserReadingMorph("kitabu", "book", "n", null, false, null)]);

        var result = await KeyboardOpinionShortcuts.StageAsync("kitabu", null, [analysis], routes,
            Enum.Parse<KeyboardShortcutBehavior>(behavior));

        Assert.True(result.Handled);
        Assert.Null(result.StatusMessage);
        Assert.Equal(kind, Assert.IsType<PendingStoredOpinionChange>(staged).Kind);
        Assert.Equal("analysis-1", staged.StoredAnalysisId);
    }

    [Fact]
    public async Task OpinionShortcutExplainsAmbiguousAndUnavailableActionsWithoutStaging()
    {
        var calls = 0;
        var routes = new WordRowRoutes { StageOpinion = _ => { calls++; return Task.FromResult(true); } };
        var one = new KeyboardStoredAnalysis("analysis-1", ReadingGrade.Disapproved, []);

        var ambiguous = await KeyboardOpinionShortcuts.StageAsync("kitabu", null, [one, one], routes,
            KeyboardShortcutBehavior.Approve);
        var unavailable = await KeyboardOpinionShortcuts.StageAsync("kitabu", null, [one], routes,
            KeyboardShortcutBehavior.Disapprove);

        Assert.Contains("2 analyses", ambiguous.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("already Disapproved", unavailable.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ControlFocusingSearchReachesEachTextsView()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
            try
            {
                var page = workspace.PageModel<TextsPageModel>();
                var targets = new (TextsTab Tab, AnalyzeTextsView View, string Name)[]
                {
                    (TextsTab.AnalyzeTexts, AnalyzeTextsView.TextReader, "ReaderWordSearch"),
                    (TextsTab.AnalyzeTexts, AnalyzeTextsView.WordList, "WordListSearch"),
                    (TextsTab.Lists, AnalyzeTextsView.TextReader, "ListsWordSearch"),
                    (TextsTab.Matrix, AnalyzeTextsView.TextReader, "MatrixWordSearch"),
                };
                var primaryModifier = CurrentPlatform == ShortcutPlatform.MacOS
                    ? RawInputModifiers.Meta
                    : RawInputModifiers.Control;
                foreach (var (tab, view, name) in targets)
                {
                    page.Tab = tab;
                    page.AnalyzeView = view;
                    workspace.CurrentPage = WorkspacePage.Texts;
                    AnalyzeTextsLayoutTests.Settle(window);
                    window.KeyPress(Key.F, primaryModifier, PhysicalKey.None, null);
                    AnalyzeTextsLayoutTests.Settle(window);
                    var search = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), box => box.Name == name);
                    Assert.True(search.IsFocused, $"{PrimarySearchGesture} focuses {name} on {tab}.");
                    window.KeyRelease(Key.F, primaryModifier, PhysicalKey.None, null);
                }
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TypingDoesNotStageAnOpinionAndUnavailableOpinionUsesTheLiveStatus()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
            try
            {
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var search = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), box => box.Name == "ReaderWordSearch");
                Assert.True(search.Focus());
                window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.None, null);
                AnalyzeTextsLayoutTests.Settle(window);
                Assert.Empty(workspace.Context.Changes.Items);

                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                inText.SearchText = string.Empty;
                Assert.True(AnalyzeTextsLayoutTests.StripOf(panel, "Sungura").Focus());
                window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.None, null);
                AnalyzeTextsLayoutTests.Settle(window);
                var status = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("KeyboardShortcutStatusLine"));
                Assert.True(status.IsVisible);
                Assert.Equal("Sungura is already Approved.", status.Text);
                Assert.Empty(workspace.Context.Changes.Items);

                window.KeyPress(Key.OemQuestion, RawInputModifiers.Shift, PhysicalKey.None, null);
                AnalyzeTextsLayoutTests.Settle(window);
                var settingsButton = Assert.IsType<Button>(window.FindControl<Button>("SettingsButton"));
                var settingsFlyout = Assert.IsType<Flyout>(settingsButton.Flyout);
                Assert.True(settingsFlyout.IsOpen);
                var settingsPopup = Assert.IsType<SettingsPopupView>(settingsFlyout.Content);
                var settings = Assert.IsType<SettingsViewModel>(settingsPopup.DataContext);
                Assert.True(settings.IsKeyboardShortcutsSelected);
                Assert.True(settingsPopup.FindControl<TextBox>("ShortcutSearchBox")!.IsFocused);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void GeneratedControlFamiliesKeepTheirAuthoredSourceMarkers()
    {
        var repository = RepositoryRoot();
        foreach (var generated in InteractiveControlManifest.GeneratedFamilies)
        {
            var source = Path.Combine(repository, generated.Source.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(source), $"{generated.Key} source is missing: {generated.Source}.");
            Assert.Contains(generated.Marker, File.ReadAllText(source), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TimingKindBarIsDisplayOnlyAndDoesNotTakeKeyboardFocus()
    {
        var bar = new TimingKindBar();

        Assert.False(bar.Focusable);
        Assert.False(bar.IsTabStop);
    }

    [Fact]
    public async Task WarningsFilterToggleRespondsToSpace()
    {
        var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Finding", [], [], "warning: finding")
        {
            Title = "Finding",
            Description = "Finding description",
            Code = "finding",
        };
        var fake = new FakeCommandClient();
        fake.OnCheckGrammar((_, _) => Task.FromResult(CommandOutcome<GrammarCheckResponse>.Success(
            new GrammarCheckResponse([warning], HasBaseline: true))));
        var grammar = new GrammarViewModel(fake);
        await grammar.SetProjectAsync(@"C:\projects\sample.fwdata");

        avalonia.Invoke(() =>
        {
            var panel = new GrammarPanel(grammar);
            var window = new Window { Content = panel, Width = 1040, Height = 900 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var toggle = Assert.Single(panel.GetVisualDescendants().OfType<ToggleButton>(),
                    button => AutomationProperties.GetName(button) == "Show findings with exact uses in your words");
                Assert.True(toggle.Focus());

                var topLevel = TopLevel.GetTopLevel(toggle)!;
                topLevel.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                topLevel.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Dispatcher.UIThread.RunJobs();

                Assert.True(grammar.Warnings.TouchYourWords);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task WarningSortMenuCanBeOpenedAndChangedWithTheKeyboard()
    {
        var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Finding", [], [], "warning: finding")
        {
            Title = "Finding",
            Description = "Finding description",
            Code = "finding",
        };
        var fake = new FakeCommandClient();
        fake.OnCheckGrammar((_, _) => Task.FromResult(CommandOutcome<GrammarCheckResponse>.Success(
            new GrammarCheckResponse([warning], HasBaseline: true))));
        var grammar = new GrammarViewModel(fake);
        await grammar.SetProjectAsync(@"C:\projects\sample.fwdata");

        avalonia.Invoke(() =>
        {
            var panel = new GrammarPanel(grammar);
            var window = new Window { Content = panel, Width = 1040, Height = 900 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var sort = Assert.Single(panel.GetVisualDescendants().OfType<Button>(),
                    button => AutomationProperties.GetName(button) == "Choose warning sort order");
                Assert.True(sort.Focus());
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Dispatcher.UIThread.RunJobs();

                var flyout = Assert.IsAssignableFrom<Flyout>(sort.Flyout);
                Assert.True(flyout.IsOpen);
                var reportOrder = Assert.Single(((Control)flyout.Content!).GetVisualDescendants().OfType<Button>(),
                    button => AutomationProperties.GetName(button) == "Sort findings in report order");
                Assert.True(reportOrder.Focus());
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Dispatcher.UIThread.RunJobs();

                Assert.False(grammar.Warnings.MostYourWordsFirst);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PointerAndKeyboardSelectionRespectTheSameCellGuards()
    {
        avalonia.Invoke(() =>
        {
            var compare = LoadedCompare();
            var window = MatrixWindow(new ComparePanel(compare));
            try
            {
                window.Show();
                window.UpdateLayout();
                var cell = Assert.Single(window.GetLogicalDescendants().OfType<MatrixCell>(), control =>
                    control.Tag is CompareCellViewModel { IsEmptyImpossible: true });
                var model = Assert.IsType<CompareCellViewModel>(cell.Tag);

                Click(window, cell);
                Assert.False(model.IsSelected);

                Assert.True(cell.Focus(), "The matrix cell can receive keyboard focus.");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.False(model.IsSelected, "Enter cannot select an impossible empty matrix cell.");

                var ordinaryCell = window.GetLogicalDescendants().OfType<MatrixCell>().First(control =>
                    control.Tag is CompareCellViewModel { IsEmptyImpossible: false });
                var ordinaryModel = Assert.IsType<CompareCellViewModel>(ordinaryCell.Tag);
                Assert.True(ordinaryCell.Focus(), "An ordinary matrix cell can receive keyboard focus.");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.True(ordinaryModel.IsSelected, "Enter selects an ordinary matrix cell.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AReadOnlyMiniMatrixCannotChangeSelection()
    {
        avalonia.Invoke(() =>
        {
            var compare = LoadedCompare();
            var miniMatrix = new MiniMatrix { DataContext = compare, IsInteractive = false };
            var window = MatrixWindow(miniMatrix);
            try
            {
                window.Show();
                window.UpdateLayout();
                var cell = Assert.Single(window.GetLogicalDescendants().OfType<MatrixCell>(), control =>
                    control.Tag is CompareCellViewModel { Count: > 0 });
                var model = Assert.IsType<CompareCellViewModel>(cell.Tag);
                Assert.False(cell.IsEffectivelyEnabled);

                Click(window, cell);
                Assert.False(model.IsSelected);

                var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
                cell.RaiseEvent(key);
                Assert.False(key.Handled);
                Assert.False(model.IsSelected);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void MiniMatrixCannotSelectAnImpossibleCellByKeyboard()
    {
        avalonia.Invoke(() =>
        {
            var compare = LoadedCompare();
            var miniMatrix = new MiniMatrix { DataContext = compare, IsInteractive = true };
            var window = MatrixWindow(miniMatrix);
            try
            {
                window.Show();
                window.UpdateLayout();
                var impossibleCell = Assert.Single(window.GetLogicalDescendants().OfType<MatrixCell>(), control =>
                    control.Tag is CompareCellViewModel { IsEmptyImpossible: true });
                var model = Assert.IsType<CompareCellViewModel>(impossibleCell.Tag);
                Assert.True(impossibleCell.Focus(), "The matrix cell can receive keyboard focus.");

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);

                Assert.False(model.IsSelected, "Enter cannot select an impossible empty matrix cell.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void WordCardArrowsRespectOccurrenceBoundariesAndChildInputs()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
            try
            {
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                var occurrences = inText.SelectedText!.Lines.SelectMany(line => line.Tokens)
                    .Where(token => token.IsWord).ToArray();
                Assert.NotEmpty(occurrences);

                await inText.OpenTokenCardAsync(occurrences[0]);
                AnalyzeTextsLayoutTests.Settle(window);
                window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);
                AnalyzeTextsLayoutTests.Settle(window);
                Assert.Same(occurrences[0], inText.SelectedToken);

                await inText.OpenTokenCardAsync(occurrences[^1]);
                AnalyzeTextsLayoutTests.Settle(window);
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                AnalyzeTextsLayoutTests.Settle(window);
                Assert.Same(occurrences[^1], inText.SelectedToken);

                var readingOccurrence = occurrences.FirstOrDefault(occurrence => occurrence.HasReadings);
                Assert.NotNull(readingOccurrence);
                await inText.OpenTokenCardAsync(readingOccurrence);
                AnalyzeTextsLayoutTests.Settle(window);
                var fixActions = Assert.Single(AnalyzeTextsLayoutTests.OpenCard(window)
                    .GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Fix actions for this word");
                Assert.True(fixActions.Focus(), "The card's Fix actions button can receive keyboard focus.");

                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                AnalyzeTextsLayoutTests.Settle(window);
                Assert.True(ReferenceEquals(readingOccurrence, inText.SelectedToken),
                    "The card's arrow navigation must leave the selected occurrence alone while its reading picker has focus.");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ClosingTheCardReturnsFocusToItsOccurrence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
            try
            {
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var occurrence = Assert.IsType<ResultsTokenViewModel>(
                    AnalyzeTextsLayoutTests.Strips(panel).Last().Tag);
                await inText.OpenTokenCardAsync(occurrence);
                AnalyzeTextsLayoutTests.Settle(window);

                var close = AnalyzeTextsLayoutTests.Named<Button>(
                    AnalyzeTextsLayoutTests.OpenCard(window), "Close the word card");
                Click(window, close);
                AnalyzeTextsLayoutTests.Settle(window);

                Assert.Null(inText.SelectedToken);
                var strip = Assert.Single(AnalyzeTextsLayoutTests.Strips(panel), candidate =>
                    ReferenceEquals(candidate.Tag, occurrence));
                Assert.True(strip.IsFocused, "Closing the card returns focus to that word occurrence.");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(60));
    }

    private static CompareViewModel LoadedCompare()
    {
        var approved = new ParserReading([new ParserReadingMorph("kitabu", "book", "n", null, false, null)])
        {
            StoredAnalysisId = "analysis-1",
            StoredAnalysisOpinion = ReadingGrade.Approved,
        };
        var compare = new CompareViewModel();
        compare.Load([new AssessWordRowViewModel(new AssessmentWordResult(
            "kitabu", "no-analysis", false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            ExpectedAnalysis = approved,
            MissedApproved = [approved],
            StoredAnalyses = [approved],
        })]);
        return compare;
    }

    private static Window MatrixWindow(Control content) => new()
    {
        Content = content,
        RequestedThemeVariant = ThemeVariant.Light,
        Width = 1400,
        Height = 900,
    };

    private static void Click(Window window, Control control)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The control is not placed in the window.");
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("The Motif repository root was not found.");
    }
}
