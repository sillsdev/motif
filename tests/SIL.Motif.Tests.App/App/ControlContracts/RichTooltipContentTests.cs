using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

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

    [GeneratedRegex(@"SetValue\(ToolTip\.TipProperty,\s*(?!null\b)([^,]+),\s*BindingPriority\.Style\)")]
    private static partial Regex StyledCodeTip();

    [GeneratedRegex(@"^\{x:Static\s+app:MotifAppStrings\.(\w+)\}$")]
    private static partial Regex LocalizedStaticTip();

    /// <summary>A MotifAppStrings {x:Static} tip is declared by the resx text of its name.</summary>
    private static string ResolveStaticText(string tip)
    {
        var match = LocalizedStaticTip().Match(tip);
        if (!match.Success) return tip;
        var property = typeof(MotifAppStrings).GetProperty(match.Groups[1].Value);
        return property?.GetValue(null) as string ?? tip;
    }

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
                        .Concat(StyledCodeTip().Matches(text).Select(match => match.Groups[1].Value.Trim())
                            .Where(tip => tip != "null"))
                    : MarkupTip().Matches(text).Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
                return tips.Select(tip => $"{source}: {ResolveStaticText(tip)}");
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
            var prior = Application.Current!.RequestedThemeVariant;
            try
            {
                foreach (var scene in Enum.GetValues<TooltipScene>())
                {
                    await scenes.Reach(scene);
                    var fresh = new List<(Control Control, TooltipOwner Owner)>();
                    foreach (var (control, owner) in scenes.RealizedOwners(failures, scene))
                    {
                        if (owner.Scene != scene) continue;
                        output.WriteLine($"{scene}: {owner.Key}: {ToolTip.GetTip(control)}");
                        if (ToolTip.GetTip(control) is string tip) words.Add($"{tip} ⟨{owner.Key}⟩");
                        if (seen.Add(owner.Key)) fresh.Add((control, owner));
                    }
                    CheckReadable(scenes, fresh, failures, words, gaps);
                    await scenes.Leave(scene);
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = prior;
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

    // A theme restyles the whole window, so a scene starts in the theme the last one ended in and switches once.
    private static void CheckReadable(TooltipScenes scenes, IReadOnlyList<(Control Control, TooltipOwner Owner)> owners,
        List<string> failures, List<string> words, HashSet<string> gaps)
    {
        if (owners.Count == 0) return;
        var themes = Application.Current!.ActualThemeVariant == ThemeVariant.Dark
            ? new[] { ThemeVariant.Dark, ThemeVariant.Light } : new[] { ThemeVariant.Light, ThemeVariant.Dark };
        try
        {
            foreach (var theme in themes)
            {
                if (Application.Current!.ActualThemeVariant != theme)
                {
                    Application.Current!.RequestedThemeVariant = theme;
                    PageScreenshots.Settle(scenes.Window);
                }
                Assert.True(Application.Current.TryGetResource("Intent.Type.Small", theme, out var small));
                foreach (var (control, owner) in owners)
                {
                    var texts = TooltipScenes.OpenTipTexts(control);
                    if (texts.Count == 0)
                    {
                        if (owner.Gap is null) failures.Add($"{theme} {owner}: the open tooltip shows no words");
                        else gaps.Add(owner.Key);
                    }
                    foreach (var text in texts)
                    {
                        words.Add($"{TooltipScenes.WordsOf(text)} ⟨{owner.Key}, opened⟩");
                        var ratio = ContrastTests.Effective(text);
                        if (ratio < Readable) failures.Add($"{theme} {owner}: '{TooltipScenes.WordsOf(text)}' is {ratio:F2}:1");
                        if (!Equals(small, text.FontSize)) failures.Add($"{theme} {owner}: type is {text.FontSize}, not Intent.Type.Small");
                    }
                    ToolTip.SetIsOpen(control, false);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                }
            }
        }
        finally
        {
            foreach (var (control, _) in owners) ToolTip.SetIsOpen(control, false);
        }
    }
}

/// <summary>The sample window and the ways each <see cref="TooltipScene"/> is reached in it, through its own commands.</summary>
internal sealed class TooltipScenes
{
    private readonly FakeCommandClient _client;
    private TaskCompletionSource<bool>? _parseProgressRelease;
    private Task<bool>? _parseProgressTask;
    public WordRow? MatrixTargetRow { get; private set; }

    public Button ParseProgressStatusButton => Window.FindInWindowOrWorkspace<Button>("StatusParseProgress")!;

    private TooltipScenes(WorkspaceShellViewModel workspace, MainWindow window, FakeCommandClient client)
    {
        Workspace = workspace;
        Window = window;
        _client = client;
    }

    public WorkspaceShellViewModel Workspace { get; }

    public MainWindow Window { get; }

    private ResultsInTextViewModel InText => Workspace.PageModel<TextsPageModel>().ResultsInText;

    private GrammarViewModel Grammar => Workspace.Context.GrammarCheck;

    public static async Task<TooltipScenes> Open(bool seedReaderToken = true)
    {
        FakeCommandClient? client = null;
        var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: seedReaderToken, configure: (fake, assessment) =>
        {
            fake.ReadWordStateCompletesWith(new WordReadStateResponse([], true));
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
        var wordRows = PageScreenshots.Assessment().Words.Select(word => JsonSerializer.SerializeToElement(new
        {
            kind = "word", form = word.Word, attempts = word.Attempts, passes = word.Passes,
            elapsed_ns = word.ElapsedMs * 1_000_000L, capped = word.Outcome == "capped", timed_out = false,
        })).ToArray();
        var objectRows = new[]
        {
            JsonSerializer.SerializeToElement(new
            {
                kind = "rule", label = "Sungura", attempts = 21, passes = 1, time_ns = 1_200_000L,
                capped = false, timed_out = false,
            }),
        };
        client!.OnStats((request, _) => Task.FromResult(CommandOutcome<StatsCommandResponse>.Success(
            new StatsCommandResponse("assessment/one", "grammar.json", "cache.sqlite", null,
                request.ForwardedArguments.Contains("object", StringComparer.Ordinal) ? objectRows : wordRows))));
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
            case TooltipScene.Overview: Show(WorkspacePage.Progress); break;
            case TooltipScene.OpenRecent:
                _client.KnownProjectsListIs([.. new[] { "Sample", "Kiswahili" }.Select(name =>
                    new KnownProjectSummary($@"C:\Users\linguist\FieldWorks\Projects\{name}\{name}.fwdata", DateTimeOffset.UtcNow.AddDays(-2)))]);
                Show(WorkspacePage.Progress);
                await OpenMenu(Window.FindInWindowOrWorkspace<MenuItem>("FileMenu")!, "the File menu");
                await OpenMenu(Window.FindInWindowOrWorkspace<MenuItem>("OpenRecentMenuItem")!, "Open recent");
                break;
            case TooltipScene.CollapsedSidebar:
                Window.Width = WorkspaceShellViewModel.SidebarCollapseWidth - 100;
                Show(WorkspacePage.Progress);
                await Until(() => Workspace.IsSidebarCollapsed, "the collapsed sidebar");
                break;
            case TooltipScene.FrameSidePanel:
                Show(WorkspacePage.Texts);
                if (Window.Width >= (double)Window.FindResource("Component.Shell.SidePanelCollapseWidth")!)
                    await Until(() => Visible<Button>().Any(button => button.Name == "SidePanelCollapseButton"),
                        "the Texts side panel");
                break;
            case TooltipScene.FrameInspectorStrip:
            case TooltipScene.FrameInspectorOpen:
                Show(WorkspacePage.Progress);
                var host = Window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                host.SetPagePanels(new TooltipDetailsPane());
                host.ShowInspector();
                PageScreenshots.Settle(Window);
                if (scene == TooltipScene.FrameInspectorStrip)
                    HeadlessClick.Click(Window, host.FindControl<Button>("InspectorHideButton")!, "Hide Inspector");
                PageScreenshots.Settle(Window);
                break;
            case TooltipScene.WhatChanged:
                Show(WorkspacePage.Progress);
                break;
            case TooltipScene.Matrix:
                Show(WorkspacePage.Words);
                var wordRows = Workspace.PageModel<WordsPageModel>().Words;
                await wordRows.SettleVisibleDetailsAsync();
                await Until(() => Visible<WordRow>().Any(), "the Selection word rows");
                MatrixTargetRow = Visible<WordRow>().First();
                MatrixTargetRow.BringIntoView();
                PageScreenshots.Settle(Window);
                break;
            case TooltipScene.Reader:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                await AnalyzeTextsLayoutTests.SettleReaderAsync(Workspace, Window);
                var readerWord = (WordStripToken)AnalyzeTextsLayoutTests.StripOf(AnalyzeTextsLayoutTests.Panel(Window), "chakula");
                readerWord.FocusWord();
                PageScreenshots.Settle(Window);
                break;
            case TooltipScene.AnalyzeWordList:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                await InText.ReadStateRefresh;
                await InText.MarkTextUnreadAsync();
                await InText.ReadStateRefresh;
                PageScreenshots.Settle(Window);
                Show(WorkspacePage.Words);
                await Until(() => Visible<TextWordsPanel>().Any(), "the Analyze texts word list");
                await Workspace.PageModel<TextsPageModel>().Words.SettleVisibleDetailsAsync();
                PageScreenshots.Settle(Window);
                break;
            case TooltipScene.ReaderDisapproved:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                await AnalyzeTextsLayoutTests.SettleReaderAsync(Workspace, Window);
                var walikula = Workspace.Context.SelectionReads.Summary!.SourcePositions.Single(position =>
                    position.Word.Form == "walikula");
                Assert.True(await AnalyzeTextsLayoutTests.Panel(Window).FocusOccurrenceAsync(walikula.Location.Anchor));
                await AnalyzeTextsLayoutTests.SettleReaderAsync(Workspace, Window);
                break;
            case TooltipScene.ReaderStaged:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                await AnalyzeTextsLayoutTests.SettleReaderAsync(Workspace, Window);
                await StageChakula();
                PageScreenshots.Settle(Window);
                break;
            case TooltipScene.TextPicker:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                await AnalyzeTextsLayoutTests.SettleReaderAsync(Workspace, Window);
                var picker = Visible<ComboBox>().First(box => AutomationProperties.GetName(box) == "Text to read");
                picker.IsDropDownOpen = true;
                await Until(() => picker.IsDropDownOpen && Visible<ComboBoxItem>().Any(), "the texts to read");
                break;
            case TooltipScene.WordCard:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                await AnalyzeTextsLayoutTests.SettleReaderAsync(Workspace, Window);
                await InText.OpenTokenCardAsync(Token("alikula"));
                await Until(() => Visible<WordCard>().Any(), "the word card");
                break;
            case TooltipScene.WordCardWithoutOccurrence:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                await AnalyzeTextsLayoutTests.SettleReaderAsync(Workspace, Window);
                var assessedWord = Workspace.Assess.Result?.Words.FirstOrDefault()
                    ?? throw new InvalidOperationException("The sample must contain an assessed word.");
                var otherWordformId = CanonicalId.FromGuid(
                    Guid.Parse("f00dbabe-cafe-4dad-9dad-bbbbbbbbbbbb")).Value;
                InText.SelectWord(assessedWord.Word, otherWordformId);
                await InText.SelectionRefresh;
                if (InText.SelectedToken is not { Occurrence: null })
                    throw new InvalidOperationException("The sample word must open without a chosen-text occurrence.");
                await Until(() => Visible<WordCard>().Any(),
                    "the word card without a chosen-text occurrence");
                break;
            case TooltipScene.Lists or TooltipScene.ListsStaged:
                Show(WorkspacePage.Words);
                break;
            case TooltipScene.TryAWordEarlierTiming:
            case TooltipScene.TryAWordRecordedDetails:
            case TooltipScene.TryAWordRepeatedRecords:
                Show(WorkspacePage.Progress);
                break;
            case TooltipScene.Timing:
            case TooltipScene.Statistics:
            case TooltipScene.StatisticsObjects:
                Show(WorkspacePage.Diagnostics);
                break;
            case TooltipScene.Warnings:
                Workspace.Context.Diagnostics = true;
                Show(WorkspacePage.Diagnostics);
                await Until(() => Visible<HyperlinkButton>().Any(button =>
                    button.Classes.Contains("warningObjectLink")), "a recorded finding's FieldWorks link");
                break;
            case TooltipScene.Handoff:
                SettingsTestAccess.Model(Window).ShowHandoffByCopyingFiles = true;
                Show(WorkspacePage.Settings);
                break;
            case TooltipScene.FrameStatus:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                await AnalyzeTextsLayoutTests.SettleReaderAsync(Workspace, Window);
                await StageChakula();
                Workspace.Context.Diagnostics = true;
                Show(WorkspacePage.Diagnostics);
                await Until(() => Visible<CopyableTextBlock>().Any(control =>
                        AutomationProperties.GetAutomationId(control) == AutomationIds.SidebarSelectionSummary),
                    "the status selection summary");
                break;
            case TooltipScene.FrameNarrowTopBar:
                Window.Width = (double)Window.FindResource("Component.TopBar.NarrowWidth")! - 60;
                Show(WorkspacePage.Progress);
                await Until(() => Visible<MenuItem>().Any(item =>
                    AutomationProperties.GetAutomationId(item) == AutomationIds.MenuButton), "the narrow top bar's menu button");
                break;
            case TooltipScene.FrameCompactStatus:
                Window.Width = (double)Window.FindResource("Component.StatusBar.CompactWidth")! - 1;
                Show(WorkspacePage.Progress);
                await Until(() => Visible<Button>().Any(button => button.Classes.Contains("statusCompact") &&
                    AutomationProperties.GetName(button) == "Report a problem"), "the compact status line");
                break;
            case TooltipScene.FrameNotice:
                Show(WorkspacePage.Progress);
                var refusal = new Refusal("baseline.capture-refused", FailureReason.Refused,
                    "FieldWorks has not saved a current Baseline.");
                var noticeSentence = WindowRefusal.From(refusal).Sentence;
                if (Visible<CopyableTextBlock>().Any(control => control.Name == "NoticeSummary" &&
                    ToolTip.GetTip(control) as string == noticeSentence))
                    break;
                _client.CaptureBaselineRefusesWith(refusal);
                var refresh = Visible<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Refresh the project");
                HeadlessClick.Click(Window, refresh, "Refresh the project");
                await Until(() => Visible<CopyableTextBlock>().Any(control => control.Name == "NoticeSummary" &&
                    ToolTip.GetTip(control) as string == noticeSentence), "the notice summary");
                break;
            case TooltipScene.FrameKeyboardStatus:
                Show(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
                await AnalyzeTextsLayoutTests.SettleReaderAsync(Workspace, Window);
                var inText = Workspace.PageModel<TextsPageModel>().ResultsInText;
                inText.SearchText = string.Empty;
                var sungura = AnalyzeTextsLayoutTests.StripOf(AnalyzeTextsLayoutTests.Panel(Window), "Sungura");
                Assert.True(sungura.Focus());
                Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                var status = Window.FindInWindowOrWorkspace<TextBlock>("KeyboardShortcutStatusLine")!;
                await Until(() => status.IsEffectivelyVisible && status.Text == SIL.Motif.App.MotifAppStrings.OccurrencesAlreadyApprovedStatus &&
                    ToolTip.GetTip(status) as string == status.Text,
                    "the keyboard shortcut status message");
                break;
            case TooltipScene.ParseProgress:
                _parseProgressRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _parseProgressTask = Workspace.Assess.Trace.ParseProgress.TrackAsync(async () =>
                {
                    await _parseProgressRelease.Task;
                    return true;
                });
                Workspace.Assess.Trace.ParseProgress.Report(
                    new AssessmentProgress(AssessmentStage.Parsing, 1, 3, "Parsing"));
                await Until(() => Visible<Button>().Any(button =>
                    AutomationProperties.GetName(button) == "See parse progress"), "parse progress status");
                HeadlessClick.Click(Window, ParseProgressStatusButton, "parse progress status");
                await Until(() => ParseProgressStatusButton.Flyout?.IsOpen == true,
                    "the parse progress flyout");
                await Until(() => Visible<Button>().Any(button =>
                    AutomationProperties.GetName(button) == "Cancel parsing all words"),
                    "the parse progress Cancel button");
                break;
        }
    }

    /// <summary>Undoes what reaching <paramref name="scene"/> opened, so the next scene starts from the page alone.</summary>
    public async Task Leave(TooltipScene scene)
    {
        switch (scene)
        {
            case TooltipScene.ReaderDisapproved:
                InText.SelectedText = InText.Texts.First(text => text.Title == "Hadithi ya sungura");
                break;
            case TooltipScene.AnalyzeWordList:
                Show(WorkspacePage.Texts);
                break;

            case TooltipScene.FrameKeyboardStatus:
                Window.ShowKeyboardStatus(null);
                break;
            case TooltipScene.OpenRecent:
                foreach (var name in new[] { "OpenRecentMenuItem", "FileMenu" }) Window.FindInWindowOrWorkspace<MenuItem>(name)!.IsSubMenuOpen = false;
                break;
            case TooltipScene.CollapsedSidebar: Window.Width = Width; break;
            case TooltipScene.TextPicker: Visible<ComboBox>().First(box => AutomationProperties.GetName(box) == "Text to read").IsDropDownOpen = false; break;
            case TooltipScene.WordCard: InText.CloseTokenCard(); break;
            case TooltipScene.WordCardWithoutOccurrence: InText.CloseTokenCard(); break;
            case TooltipScene.ParseProgress:
                ParseProgressStatusButton.Flyout?.Hide();
                _parseProgressRelease!.SetResult(true);
                await _parseProgressTask!;
                break;
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
        Workspace.CurrentPage = page;
        PageScreenshots.Settle(Window);
    }

    private async Task OpenMenu(MenuItem item, string what)
    {
        HeadlessClick.Click(Window, item, what);
        await Until(() => item.IsSubMenuOpen, what);
    }

    private async Task OpenMenu(Button button, string what)
    {
        var menu = button.Flyout ?? throw new InvalidOperationException($"{what} has no menu.");
        HeadlessClick.Click(Window, button, what);
        await Until(() => menu.IsOpen, what);
    }

    private ResultsTokenViewModel Token(string form) =>
        AnalyzeTextsLayoutTests.RealizedLines(InText).SelectMany(line => line.Tokens).First(token => token.Form == form);

    private sealed class TooltipDetailsPane : IWorkspacePaneProvider
    {
        public Control InspectorContent { get; } = new Border();
    }

    // The staged scenes, which come last, show a pending change; it is chakula's, as the state captures stage it.
    private async Task StageChakula()
    {
        var chakula = Token("chakula");
        if (chakula.HasStagedChanges) return;
        var add = chakula.Marking.SetChoices.Single(choice => choice.Label == "Add as Approved");
        await chakula.StageMarkingChoiceForTokenCommand!.ExecuteAsync(chakula.BindMarkingChoice(add));
        await Until(() => Token("chakula").HasStagedChanges, "the staged change on chakula");
    }

    private Task Until(Func<bool> done, string what) => Wait.UntilAsync(done,
        $"The window never showed {what}.", between: () => PageScreenshots.Settle(Window));
}
