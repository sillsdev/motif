using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the owner's ruling that the window speaks a linguist's words: one action, "Parse all words", and
/// FieldWorks' Approved, Disapproved and Unknown. Assessment stays the CLI's word (ADR 0015); the window, its
/// refusals and the Help Guide pages never show it, nor the words ADR 0049 retired.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed partial class WindowWordsTests
{
    [GeneratedRegex(@"\b(assess\w*|candidates?|reject(s|ed)?|violations?|cannot happen|can[’']?t happen)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex RetiredWord();

    // An opinion standing alone, after a separator or before an arrow, is a label and so capitalised.
    [GeneratedRegex(@"(^|[·→(]\s*)(approved|disapproved|candidate)\b|·\s*unknown\b|\b(approved|disapproved|unknown)\s*→")]
    private static partial Regex LowerCaseOpinion();

    // A FieldWorks class name such as PhEnvironment, in any casing; the window names the kind instead.
    [GeneratedRegex(@"\b(?:lex|mo|ph|fs|cm)(?:entry|sense|form|stem|infl|deriv|unclassified|compound|adhoc|phoneme|bdry" +
        @"|natural|environment|regular|metathesis|feature|complex|closed|sym)\w*", RegexOptions.IgnoreCase)]
    private static partial Regex FieldWorksClassName();

    // A file name the CLI writes, and a refusal's code shown to say where a sentence came from, are not prose.
    [GeneratedRegex(@"[\w.-]+\.(json|py|md)\b|⟨[^⟩]*⟩")]
    private static partial Regex FileName();

    // A Guide page's cross-link target is a code; only its link text is read.
    [GeneratedRegex(@"\]\((?:cmd|guide|term|shot):[^)]*\)")]
    private static partial Regex LinkTarget();

    [GeneratedRegex(@"\]\(term:([^)]*)\)")]
    private static partial Regex TermLink();

    [Fact]
    public void EveryPageAndTabOfTheWindowUsesWindowWords()
    {
        var shown = new List<string>();
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            // Other known projects, so Open recent has entries for its nested menu to show.
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure: (fake, _) =>
                fake.KnownProjectsListIs([.. new[] { "Sample", "Kiswahili", "Mbugwe" }.Select(name => new KnownProjectSummary(
                    $@"C:\Users\linguist\FieldWorks\Projects\{name}\{name}.fwdata", DateTimeOffset.UtcNow.AddDays(-2)))]));
            try
            {
                window.Width = 1240;
                window.Height = 780;
                foreach (var (page, tab) in EveryView())
                {
                    workspace.PageModel<TextsPageModel>().Tab = tab;
                    workspace.CurrentPage = page;
                    PageScreenshots.Settle(window);
                    shown.AddRange(Rendered(window).Select(text => $"{page}/{tab}: {text}"));
                    shown.AddRange(Opened(window).Select(text => $"{page}/{tab} opened: {text}"));
                }
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));

        AssertWindowWords(shown);
    }

    [Fact]
    public void EveryPageBeforeTheFirstParseUsesWindowWords()
    {
        var shown = new List<string>();
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                foreach (var (page, tab) in EveryView())
                {
                    workspace.PageModel<TextsPageModel>().Tab = tab;
                    workspace.CurrentPage = page;
                    PageScreenshots.Settle(window);
                    shown.AddRange(Rendered(window).Select(text => $"{page}/{tab}: {text}"));
                }
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));

        AssertWindowWords(shown);
    }

    [Fact]
    public void EveryStepOfSetupUsesWindowWordsWhenFirstRunAndWhenEditing()
    {
        var shown = new List<string>();
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, leaveSetupOpen: true);
            try
            {
                var setup = workspace.Context.Setup!;
                PageScreenshots.Settle(window);
                Assert.True(setup.IsOpen, "first-run setup did not open");
                foreach (var text in workspace.Selection.Texts) text.IsChecked = true;
                shown.AddRange(Steps(window, setup, "first run"));
                await setup.FinishCommand.ExecuteAsync(null);
                setup.OpenForConfiguration();
                Assert.True(setup.IsEditingExistingSelection, setup.ShownRefusal?.Sentence ?? "setup did not save");
                shown.AddRange(Steps(window, setup, "editing"));
                Assert.Equal("This will be the project default. Parse all words will use it next time.",
                    setup.FinishDescription);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));

        AssertWindowWords(shown);
    }

    // Messages the real commands write, so the filter is tried on what reaches the window, not on a made-up line.
    [Theory]
    [InlineData("The Assessor returned no measurement collection.")]
    [InlineData("The Assessor did not return exactly one measurement for every required kind.")]
    [InlineData("The chosen reading is absent from the Assessment.")]
    public void EveryRefusalTheWindowShowsUsesWindowWords(string message)
    {
        const string fact = "assessmentId: assessment/one";
        var refusals = typeof(RefusalCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Select(code => WindowRefusal.From(new Refusal(code, FailureReason.Refused, message,
                new Dictionary<string, string> { ["assessmentId"] = "assessment/one" })))
            .ToArray();

        Assert.All(refusals, refusal => Assert.Contains(fact, refusal.Details ?? string.Empty, StringComparison.Ordinal));
        AssertWindowWords(refusals.SelectMany(refusal => new[]
        {
            $"{refusal.Sentence} ⟨{refusal.Code}⟩",
            $"{refusal.Details?.Replace(fact, string.Empty, StringComparison.Ordinal)} ⟨{refusal.Code}⟩",
        }));
    }

    [Fact]
    public void EveryKindOfChangeIsListedInWindowWords()
    {
        var kinds = typeof(ChangeKinds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(
            ["Approve", "Disapprove", "Make Unknown", "Incorrect spelling", "Add as Unknown", "Remove analysis"],
            kinds.Select(ChangeKinds.LabelOf));
    }

    [Fact]
    public void EveryHelpGuidePageAWindowReaderOpensUsesWindowWords()
    {
        // The window reads the Guide through the catalog, so the test does too; agents/ pages carry the CLI's words.
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var pages = catalog.Entries
            .Where(entry => entry.Kind == HelpEntryKind.Guide && !entry.Code.StartsWith("agents/", StringComparison.Ordinal))
            .Select(entry => (entry.Code, Lines: (entry.HelpPage ?? string.Empty).Split('\n')))
            .ToArray();
        Assert.Contains(pages, page => page.Code == "assessment");
        Assert.Contains(pages, page => page.Code.StartsWith("learn/", StringComparison.Ordinal));
        // A term a Guide page links to opens in the window too, so its title and description are scanned with it.
        var terms = pages.SelectMany(page => page.Lines).SelectMany(line => TermLink().Matches(line))
            .Select(link => Uri.UnescapeDataString(link.Groups[1].Value)).Distinct()
            .Select(code => catalog.Find(HelpEntryKind.Term, code)
                ?? throw new InvalidOperationException($"The Guide links to a missing term '{code}'."))
            .SelectMany(term => new[] { $"{term.Title} ⟨term {term.Code}⟩", $"{term.Description} ⟨term {term.Code}⟩" });
        var shown = pages.SelectMany(page => page.Lines
                .Select(line => $"{LinkTarget().Replace(line, "]")} ⟨guide {page.Code}⟩"))
            .Concat(terms);

        AssertWindowWords(shown);
    }

    [Fact]
    public void TheWordsReaderReadsRichTooltipsAndMenuEntries()
    {
        var read = new List<string>();
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var owner = new Button { Content = "Apply" };
            ToolTip.SetTip(owner, new StackPanel { Children = { new TextBlock { Text = "Why it waits" }, new TextBlock { Text = "What to do" } } });
            var menu = new MenuItem { Header = "Open the entry" };
            read.AddRange(Rendered(new StackPanel { Children = { owner, new Menu { Items = { menu } } } }));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));

        Assert.Contains("Why it waits", read);
        Assert.Contains("What to do", read);
        Assert.Contains("Open the entry", read);
    }

    private static IEnumerable<(WorkspacePage Page, TextsTab Tab)> EveryView() =>
    [
        .. Enum.GetValues<WorkspacePage>().Where(page => page != WorkspacePage.Texts)
            .Select(page => (page, TextsTab.Matrix)),
        .. Enum.GetValues<TextsTab>().Select(tab => (WorkspacePage.Texts, tab)),
    ];

    private static IEnumerable<string> Steps(MainWindow window, SetupViewModel setup, string mode)
    {
        var shown = new List<string>();
        for (var step = 0; step < 4; step++)
        {
            PageScreenshots.Settle(window);
            shown.AddRange(Rendered(window).Select(text => $"setup {mode} step {step + 1}: {text}"));
            shown.Add($"setup {mode}: {setup.StepLimitEstimateText}");
            if (setup.NextCommand.CanExecute(null)) setup.NextCommand.Execute(null);
        }
        return shown;
    }

    internal static void AssertWindowWords(IEnumerable<string> shown)
    {
        var retired = shown.Where(text => FileName().Replace(text, string.Empty) is var prose &&
                (RetiredWord().IsMatch(prose) || LowerCaseOpinion().IsMatch(prose) || FieldWorksClassName().IsMatch(prose)))
            .Distinct().ToArray();
        Assert.True(retired.Length == 0, "Retired words in the window:" + Environment.NewLine +
            string.Join(Environment.NewLine, retired));
    }

    // Collapsed sections only build their rows when opened, and a menu's entries only exist while it is open.
    private static IEnumerable<string> Opened(MainWindow window)
    {
        var shown = new List<string>();
        var opened = new List<Expander>();
        for (var round = 0; round < 4; round++)
        {
            var closed = window.GetVisualDescendants().OfType<Expander>()
                .Where(expander => expander.IsEffectivelyVisible && !expander.IsExpanded).ToList();
            if (closed.Count == 0) break;
            foreach (var expander in closed) expander.IsExpanded = true;
            opened.AddRange(closed);
            PageScreenshots.Settle(window);
        }
        shown.AddRange(Rendered(window));
        var menus = window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled && button.Flyout is not null).ToList();
        foreach (var button in menus) shown.AddRange(OpenedMenu(window, button, depth: 0));
        foreach (var expander in opened) expander.IsExpanded = false;
        PageScreenshots.Settle(window);
        return shown;
    }

    // A menu entry can open a menu of its own, such as Open recent; its entries exist only while both are open.
    private static List<string> OpenedMenu(MainWindow window, Button button, int depth)
    {
        var shown = new List<string>();
        button.Flyout!.ShowAt(button);
        PageScreenshots.Settle(window);
        var presenters = Presenters(window).ToList();
        foreach (var presenter in presenters) shown.AddRange(Rendered(presenter));
        if (depth < 2)
            foreach (var nested in presenters.SelectMany(presenter => presenter.GetVisualDescendants().OfType<Button>())
                .Where(candidate => candidate.IsEffectivelyVisible && candidate.IsEffectivelyEnabled && candidate.Flyout is not null).ToList())
                shown.AddRange(OpenedMenu(window, nested, depth + 1));
        button.Flyout.Hide();
        PageScreenshots.Settle(window);
        return shown;
    }

    private static IEnumerable<Control> Presenters(Control root) =>
        root.GetVisualDescendants().OfType<Control>().Where(control => control is FlyoutPresenter or MenuFlyoutPresenter);

    // Hidden text too, as the sample reaches few states; both trees, as a DataGrid's cells are only visual.
    private static IEnumerable<string> Rendered(Control root)
    {
        var texts = new List<string?>();
        var controls = root.GetSelfAndLogicalDescendants().OfType<Control>()
            .Union(root.GetSelfAndVisualDescendants().OfType<Control>());
        foreach (var control in controls)
        {
            texts.Add(AutomationProperties.GetName(control));
            texts.Add(AutomationProperties.GetHelpText(control));
            texts.Add(ToolTip.GetTip(control) as string);
            if (ToolTip.GetTip(control) is Control rich) texts.AddRange(Rendered(rich));
            switch (control)
            {
                case MenuItem { Header: string item }: texts.Add(item); break;
                case TextBlock block:
                    texts.Add(block.Text);
                    if (block.Inlines is { } inlines) texts.AddRange(inlines.OfType<Run>().Select(run => run.Text));
                    break;
                case HeaderedContentControl { Header: string header }: texts.Add(header); break;
                case ContentControl { Content: string content }: texts.Add(content); break;
                case TextBox box: texts.Add(box.PlaceholderText); break;
                case ComboBox combo: texts.Add(combo.PlaceholderText); break;
            }
        }
        return texts.Where(text => !string.IsNullOrWhiteSpace(text)).Cast<string>();
    }
}
