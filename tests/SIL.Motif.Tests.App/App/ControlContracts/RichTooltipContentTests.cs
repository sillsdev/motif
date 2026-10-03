using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using WordRow = SIL.Motif.App.Views.WordRow;

namespace SIL.Motif.Tests.App.ControlContracts;

/// <summary>
/// Pins that the authored tooltip owners and the tooltips the views declare are the same set, so a new tooltip
/// cannot ship without a scene that opens it and a check of its words.
/// </summary>
public sealed partial class TooltipOwnerManifestTests
{
    [GeneratedRegex(@"ToolTip\.Tip=""([^""]*)""|<Setter\s+Property=""ToolTip\.Tip""\s+Value=""([^""]*)""")]
    private static partial Regex MarkupTip();

    [GeneratedRegex(@"ToolTip\.SetTip\(\s*\w+\s*,\s*(.+?)\);")]
    private static partial Regex CodeTip();

    [Fact]
    public void EveryTooltipTheViewsDeclareHasAnAuthoredOwner()
    {
        var app = Path.Combine(TooltipScenes.RepositoryRoot(), "src", "SIL.Motif.App");
        var declared = Directory.GetFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".axaml", StringComparison.Ordinal) || file.EndsWith(".cs", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file =>
            {
                var source = Path.GetRelativePath(app, file).Replace(Path.DirectorySeparatorChar, '/');
                var text = File.ReadAllText(file);
                var tips = file.EndsWith(".cs", StringComparison.Ordinal)
                    ? CodeTip().Matches(text).Select(match => match.Groups[1].Value)
                    : MarkupTip().Matches(text).Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
                return tips.Select(tip => $"{source}: {tip}");
            })
            .Order(StringComparer.Ordinal)
            .ToList();
        var authored = TooltipOwners.All.Select(owner => $"{owner.Source}: {owner.Declaration}").Order(StringComparer.Ordinal).ToList();

        Assert.NotEmpty(declared);
        Assert.True(declared.SequenceEqual(authored),
            "Declared tooltips without an owner: " + string.Join("; ", declared.Except(authored)) + Environment.NewLine +
            "Owners for no declared tooltip: " + string.Join("; ", authored.Except(declared)));
        Assert.Equal(TooltipOwners.All.Count, TooltipOwners.All.Select(owner => owner.Key).Distinct().Count());
    }
}

/// <summary>
/// Opens every tooltip owner the sample window reaches, in the scene the manifest names, and checks that each tip's
/// words are the window's words and readable at small type in both themes. A realized tooltip whose owner the
/// manifest does not know fails, as does an owner the manifest expects and the scene never shows.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class RichTooltipContentTests(ITestOutputHelper output)
{
    private const double Readable = 4.5;

    [Fact]
    public void RichTooltipContentUsesWindowWordsAndReadableText()
    {
        var failures = new List<string>();
        var words = new List<string>();
        var gaps = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var scenes = await TooltipScenes.Open();
            try
            {
                foreach (var scene in Enum.GetValues<TooltipScene>())
                {
                    await scenes.Reach(scene);
                    foreach (var (control, owner) in scenes.RealizedOwners(failures, scene))
                    {
                        if (owner.Scene != scene) continue;
                        output.WriteLine($"{scene}: {owner.Key}: {ToolTip.GetTip(control)}");
                        if (ToolTip.GetTip(control) is string tip) words.Add($"{tip} ⟨{owner.Key}⟩");
                        if (!seen.Add(owner.Key)) continue;
                        CheckReadable(scenes, control, owner, failures, words, gaps);
                    }
                    scenes.Leave(scene);
                }
            }
            finally
            {
                scenes.Close();
            }
        }, TimeSpan.FromMinutes(3));

        foreach (var owner in TooltipOwners.All)
        {
            if (owner.Pending is null && !seen.Contains(owner.Key)) failures.Add($"{owner}: the {owner.Scene} scene showed no such tooltip");
            if (owner.Pending is not null && seen.Contains(owner.Key))
                failures.Add($"{owner}: the sample now reaches it, so it is no longer pending ({owner.Pending})");
        }
        failures.AddRange(TooltipOwners.All.Where(owner => owner.Gap is not null && !gaps.Contains(owner.Key))
            .Select(owner => $"{owner}: the reported gap no longer shows, so remove it ({owner.Gap})"));
        Assert.True(seen.Count > 0, "no tooltip opened");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        WindowWordsTests.AssertWindowWords(words);
    }

    private static void CheckReadable(TooltipScenes scenes, Control control, TooltipOwner owner, List<string> failures, List<string> words,
        HashSet<string> gaps)
    {
        var prior = Application.Current!.RequestedThemeVariant;
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current!.RequestedThemeVariant = theme;
                PageScreenshots.Settle(scenes.Window);
                var texts = TooltipScenes.OpenTipTexts(control);
                if (texts.Count == 0)
                {
                    if (owner.Gap is null) failures.Add($"{theme} {owner}: the open tooltip shows no words");
                    else gaps.Add(owner.Key);
                    continue;
                }
                Assert.True(Application.Current.TryGetResource("Intent.Type.Small", theme, out var small));
                foreach (var text in texts)
                {
                    words.Add($"{TooltipScenes.WordsOf(text)} ⟨{owner.Key}, opened⟩");
                    var ratio = ContrastTests.Effective(text);
                    if (ratio < Readable) failures.Add($"{theme} {owner}: '{TooltipScenes.WordsOf(text)}' is {ratio:F2}:1");
                    if (!Equals(small, text.FontSize)) failures.Add($"{theme} {owner}: type is {text.FontSize}, not Intent.Type.Small");
                }
                ToolTip.SetIsOpen(control, false);
                PageScreenshots.Settle(scenes.Window);
            }
        }
        finally
        {
            ToolTip.SetIsOpen(control, false);
            Application.Current!.RequestedThemeVariant = prior;
            PageScreenshots.Settle(scenes.Window);
        }
    }
}

/// <summary>The sample window and the ways each <see cref="TooltipScene"/> is reached in it, through its own commands.</summary>
internal sealed class TooltipScenes
{
    private readonly FakeCommandClient _client;

    private TooltipScenes(WorkspaceShellViewModel workspace, MainWindow window, FakeCommandClient client)
    {
        Workspace = workspace;
        Window = window;
        _client = client;
    }

    public WorkspaceShellViewModel Workspace { get; }

    public MainWindow Window { get; }

    private ResultsInTextViewModel InText => Workspace.PageModel<TextsPageModel>().ResultsInText;

    private TextsListsViewModel Lists => Workspace.PageModel<TextsPageModel>().TextsLists;

    private GrammarViewModel Grammar => Workspace.PageModel<WarningsPageModel>().Grammar;

    public static async Task<TooltipScenes> Open()
    {
        FakeCommandClient? client = null;
        var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure: (fake, assessment) =>
        {
            OverviewTimingScreenshots.ReadOverviewAndTiming(fake, assessment);
            var ruleKey = OverviewTimingScreenshots.SubjectAgreementRuleKey;
            fake.StoredGrammarCheckIs(new GrammarCheckResponse([
                new GrammarWarning(GrammarDiagnosticLevel.Warning, "Subject agreement", [
                    new GrammarWarningPart("Subject agreement", GrammarWarningPartRole.Object, ruleKey, "PhRegularRule")
                    {
                        FieldWorksGuid = ruleKey,
                        Reach = new WarningReach(WarningWordsPath.Uses)
                        {
                            TimingKeys = [new TraceTimingKey("morph_rule", ruleKey)],
                        },
                    }], [], "warning: timing.rule.named")
                {
                    Code = "timing.rule.named",
                    Title = "Subject agreement",
                },
                new GrammarWarning(GrammarDiagnosticLevel.Warning, "Stem", [
                    new GrammarWarningPart("mbo", GrammarWarningPartRole.Object,
                        "88888888-8888-4888-8888-888888888888", "LexEntry",
                        "silfw://localhost/link?database=Sample&tool=lexiconEdit&guid=88888888-8888-4888-8888-888888888888")
                    {
                        Title = "mbo",
                        FieldWorksTool = "lexiconEdit",
                        Status = GrammarSubjectStatus.Object,
                    },
                    new GrammarWarningPart("u", GrammarWarningPartRole.Object, "phoneme-guid", "PhPhoneme",
                        "silfw://localhost/link?database=Sample&tool=phonemeEdit&guid=99999999-9999-4999-8999-999999999999")
                    {
                        Title = "u",
                        FieldWorksTool = "phonemeEdit",
                        Status = GrammarSubjectStatus.Object,
                    }], [], "warning: entry.named")
                {
                    Code = "entry.named",
                    Title = "Stem",
                },
            ], HasBaseline: true));
            client = fake;
        });
        await workspace.Context.EvidencePublication;
        window.Width = 1240;
        window.Height = 780;
        PageScreenshots.Settle(window);
        return new TooltipScenes(workspace, window, client!);
    }

    public void Close() => Window.Close();

    /// <summary>The width every scene but the collapsed sidebar is drawn at.</summary>
    public double Width { get; set; } = 1240;

    public async Task Reach(TooltipScene scene)
    {
        if (scene != TooltipScene.CollapsedSidebar) Window.Width = Width;
        switch (scene)
        {
            case TooltipScene.Overview: Show(WorkspacePage.Overview); break;
            case TooltipScene.OpenRecent:
                _client.KnownProjectsListIs([.. new[] { "Sample", "Kiswahili" }.Select(name =>
                    new KnownProjectSummary($@"C:\Users\linguist\FieldWorks\Projects\{name}\{name}.fwdata", DateTimeOffset.UtcNow.AddDays(-2)))]);
                Show(WorkspacePage.Overview);
                await OpenMenu(Window.FindControl<Button>("ProjectMenuButton")!, "the project menu");
                await OpenMenu(Window.FindControl<Button>("OpenRecentButton")!, "Open recent");
                break;
            case TooltipScene.CollapsedSidebar:
                Window.Width = WorkspaceShellViewModel.SidebarCollapseWidth - 100;
                Show(WorkspacePage.Overview);
                await Until(() => Workspace.IsSidebarCollapsed, "the collapsed sidebar");
                break;
            case TooltipScene.Matrix:
                Show(WorkspacePage.Texts, TextsTab.Matrix);
                Visible<WordRow>().First().FocusRow();
                PageScreenshots.Settle(Window);
                break;
            case TooltipScene.MatrixStaged: Show(WorkspacePage.Texts, TextsTab.Matrix); break;
            case TooltipScene.Reader: Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts); break;
            case TooltipScene.AnalyzeWordList:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                Workspace.PageModel<TextsPageModel>().ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.WordList);
                await Until(() => Visible<Control>().Any(control => control.FindAncestorOfType<TextWordsPanel>() is not null),
                    "the Analyze texts word list");
                break;
            case TooltipScene.ReaderDisapproved:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                InText.SelectedText = InText.Texts.Single(text => text.Lines.SelectMany(line => line.Tokens)
                    .Any(token => token.Form == "walikula"));
                PageScreenshots.Settle(Window);
                break;
            case TooltipScene.ReaderStaged:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                await StageChakula();
                PageScreenshots.Settle(Window);
                break;
            case TooltipScene.TextPicker:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                var picker = Visible<ComboBox>().First(box => AutomationProperties.GetName(box) == "Text to read");
                picker.IsDropDownOpen = true;
                await Until(() => picker.IsDropDownOpen && Visible<ComboBoxItem>().Any(), "the texts to read");
                break;
            case TooltipScene.WordCard:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                await InText.OpenTokenCardAsync(Token("alikula"));
                await Until(() => Visible<Border>().Any(border => border.Classes.Contains("wordCard")), "the word card");
                break;
            case TooltipScene.WordCardWithoutOccurrence:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                var assessedWord = Workspace.Assess.Result?.Words.FirstOrDefault()
                    ?? throw new InvalidOperationException("The sample must contain an assessed word.");
                var otherWordformId = CanonicalId.FromGuid(
                    Guid.Parse("f00dbabe-cafe-4dad-9dad-bbbbbbbbbbbb")).Value;
                InText.SelectWord(assessedWord.Word, otherWordformId);
                if (InText.SelectedToken is not { Occurrence: null })
                    throw new InvalidOperationException("The sample word must open without a chosen-text occurrence.");
                await Until(() => Visible<Border>().Any(border => border.Classes.Contains("wordCard")),
                    "the word card without a chosen-text occurrence");
                break;
            case TooltipScene.Lists or TooltipScene.ListsStaged:
                Show(WorkspacePage.Texts, TextsTab.Lists);
                var list = Lists.Lists.First(candidate => candidate.HasWords);
                if (Lists.SelectedList != list) Lists.SelectListCommand.Execute(list);
                await Until(() => Lists.SelectedList == list && Lists.Compare.Words.Count > 0, "the chosen list's words");
                break;
            case TooltipScene.TryAWordEarlierTiming:
                _client.TraceWordCompletesWith(WordTraceQuery.LoadDiagnostic(PageScreenshots.SampleTrace()).Value!);
                Workspace.Context.TryWord("hawajafika");
                await Workspace.Assess.Trace.TryCommand.ExecutionTask!;
                Show(WorkspacePage.TryAWord);
                break;
            case TooltipScene.TryAWordRecordedDetails:
                _client.TraceWordCompletesWith(WordTraceQuery.LoadDiagnostic(File.ReadAllText(Path.Combine(
                    AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-matinlu.json"))).Value!);
                Workspace.Context.TryWord("matinlu");
                await Workspace.Assess.Trace.TryCommand.ExecutionTask!;
                Show(WorkspacePage.TryAWord);
                break;
            case TooltipScene.ExpertTrace:
                Window.Height = 2400;
                var trace = Workspace.Assess.Trace;
                trace.Result = TraceWordViewModel.FromDiagnosticJson(ExpertTraceReadingTests.NotationDiagnostic).Result;
                trace.SelectedCandidate = trace.Candidates.Single();
                trace.SelectedStep = trace.SelectedCandidate.Steps[3];
                trace.ExpertWholeTree = false;
                trace.IsExpert = true;
                Show(WorkspacePage.TryAWord);
                PageScreenshots.Settle(Window);
                foreach (var expander in Visible<ExpertTracePanel>().Single().GetVisualDescendants().OfType<Expander>())
                    expander.IsExpanded = true;
                PageScreenshots.Settle(Window);
                break;
            case TooltipScene.TryAWordRepeatedRecords:
                _client.TraceWordCompletesWith(WordTraceQuery.LoadDiagnostic(TraceEnvelope.AnalysisRecords()).Value!);
                Workspace.Context.TryWord("word");
                await Workspace.Assess.Trace.TryCommand.ExecutionTask!;
                Show(WorkspacePage.TryAWord);
                break;
            case TooltipScene.Timing:
                Show(WorkspacePage.Timing);
                await Until(() => Visible<Button>().Any(button =>
                    AutomationProperties.GetName(button) == "Open the grammar warning"),
                    "a Timing rule named by a stored grammar warning");
                break;
            case TooltipScene.Statistics:
                Show(WorkspacePage.Timing);
                var statistics = Visible<Expander>().First(expander => expander.Header as string == "Detailed statistics");
                statistics.IsExpanded = true;
                await Until(() => Visible<StatisticsPanel>().Any(), "Detailed statistics");
                break;
            case TooltipScene.Warnings:
                Show(WorkspacePage.Warnings);
                await Until(() => !Grammar.IsLoading && Grammar.HasChecked && Visible<Button>().Any(HasNamedEntryAction),
                    "the completed grammar check with a named entry");
                var warningSummary = Visible<Button>().First(HasNamedEntryAction);
                var warningRow = Assert.IsType<GrammarWarningRowViewModel>(warningSummary.DataContext);
                var entry = warningRow.Details.SelectMany(detail => detail.EntryLinks).Single();
                if (!warningRow.IsOpen) warningRow.ToggleOpenCommand.Execute(null);
                PageScreenshots.Settle(Window);
                await Until(() => Visible<HyperlinkButton>().Any(button =>
                    button.Classes.Contains("warningEntryAction") && button.Content is string text &&
                    text == entry.Label), $"the finding's named FieldWorks entry '{entry.Entry}'");
                break;
            case TooltipScene.Handoff: Show(WorkspacePage.AiHandoff); break;
            case TooltipScene.ReviewStaged: Show(WorkspacePage.Review); break;
        }
    }

    /// <summary>Undoes what reaching <paramref name="scene"/> opened, so the next scene starts from the page alone.</summary>
    public void Leave(TooltipScene scene)
    {
        switch (scene)
        {
            case TooltipScene.ReaderDisapproved:
                InText.SelectedText = InText.Texts.First();
                break;
            case TooltipScene.AnalyzeWordList:
                Workspace.PageModel<TextsPageModel>().ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.TextReader);
                break;
            case TooltipScene.ExpertTrace: Workspace.Assess.Trace.IsExpert = false; Window.Height = 780; break;
            case TooltipScene.OpenRecent:
                foreach (var name in new[] { "OpenRecentButton", "ProjectMenuButton" }) Window.FindControl<Button>(name)!.Flyout!.Hide();
                break;
            case TooltipScene.CollapsedSidebar: Window.Width = Width; break;
            case TooltipScene.TextPicker: Visible<ComboBox>().First(box => AutomationProperties.GetName(box) == "Text to read").IsDropDownOpen = false; break;
            case TooltipScene.WordCard: InText.CloseTokenCard(); break;
            case TooltipScene.WordCardWithoutOccurrence: InText.CloseTokenCard(); break;
        }
        Window.MouseMove(new Point(4, Window.Bounds.Height - 4));
        PageScreenshots.Settle(Window);
    }

    /// <summary>
    /// Every visible control in the window and its open pop-ups that carries a tooltip, with the one owner it matches.
    /// A control that matches no owner, or several, is a failure named with its scene.
    /// </summary>
    public IEnumerable<(Control Control, TooltipOwner Owner)> RealizedOwners(List<string> failures, TooltipScene scene)
    {
        foreach (var control in Visible<Control>().Where(control => ToolTip.GetTip(control) is not null).ToList())
        {
            var owners = TooltipOwners.All.Where(owner => owner.Is(control)).ToList();
            if (owners.Count == 1) yield return (control, owners[0]);
            else failures.Add($"{scene}: a {control.GetType().Name} '{AutomationProperties.GetName(control) ?? ToolTip.GetTip(control)}' " +
                (owners.Count == 0 ? "has a tooltip no owner describes" : $"matches {string.Join(", ", owners.Select(owner => owner.Key))}"));
        }
    }

    public IEnumerable<T> Visible<T>() where T : Control =>
        Window.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible);

    /// <summary>Opens <paramref name="owner"/>'s tooltip, settled and opaque, and returns every text block that shows its words.</summary>
    public static IReadOnlyList<TextBlock> OpenTipTexts(Control owner)
    {
        ToolTip.SetIsOpen(owner, true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var tip = Assert.IsType<ToolTip>(owner.GetValue(TipProperty));
        // The tip fades in; it is measured as it settles, fully opaque.
        tip.Transitions = null;
        tip.Opacity = 1;
        tip.UpdateLayout();
        return tip.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible && WordsOf(block).Length > 0).ToList();
    }

    public static string WordsOf(TextBlock block) =>
        block.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(run => run.Text)) : block.Text ?? string.Empty;

    public static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return root.FullName;
    }

    private static readonly AvaloniaProperty TipProperty = (AvaloniaProperty)typeof(ToolTip)
        .GetField("ToolTipProperty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
        .GetValue(null)!;

    private void Show(WorkspacePage page, TextsTab? tab = null)
    {
        if (tab is { } chosen) Workspace.PageModel<TextsPageModel>().Tab = chosen;
        Workspace.CurrentPage = page;
        PageScreenshots.Settle(Window);
    }

    private async Task OpenMenu(Button button, string what)
    {
        var menu = button.Flyout ?? throw new InvalidOperationException($"{what} has no menu.");
        HeadlessClick.Click(Window, button, what);
        await Until(() => menu.IsOpen, what);
    }

    private ResultsTokenViewModel Token(string form) =>
        InText.VisibleLines.SelectMany(line => line.Tokens).First(token => token.Form == form);

    private static bool HasNamedEntryAction(Button button) => button.Classes.Contains("warningSummary") &&
        button.DataContext is GrammarWarningRowViewModel row &&
        row.Details.Any(detail => detail.EntryLinks.Count > 0);

    // The staged scenes, which come last, show a pending change; it is chakula's, as the state captures stage it.
    private async Task StageChakula()
    {
        var chakula = Token("chakula");
        if (chakula.HasStagedChanges) return;
        var add = chakula.Marking.FixChoices.Single(choice => choice.Label == "Add as Approved");
        await chakula.StageMarkingChoiceForTokenCommand!.ExecuteAsync(add);
        await Until(() => Token("chakula").HasStagedChanges, "the staged change on chakula");
    }

    private async Task Until(Func<bool> done, string what)
    {
        for (var pass = 0; pass < 50 && !done(); pass++)
        {
            await Task.Yield();
            PageScreenshots.Settle(Window);
        }
        if (!done()) throw new InvalidOperationException($"The window never showed {what}.");
    }
}
