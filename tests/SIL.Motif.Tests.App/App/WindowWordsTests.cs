using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
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

    // A file name the CLI writes, and a refusal's code shown to say where a sentence came from, are not prose.
    [GeneratedRegex(@"[\w.-]+\.(json|py|md)\b|⟨[^⟩]*⟩")]
    private static partial Regex FileName();

    // A Guide page's cross-link target is a code; only its link text is read.
    [GeneratedRegex(@"\]\((?:cmd|term|shot):[^)]*\)")]
    private static partial Regex LinkTarget();

    [GeneratedRegex(@"\]\(term:([^)]*)\)")]
    private static partial Regex TermLink();

    [Fact]
    public void EveryPageAndTabOfTheWindowUsesWindowWords()
    {
        var shown = new List<string>();
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
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
        var guide = Path.Combine(RepoPaths.FindRepoRoot(), "help", "en", "guide");
        var agents = Path.Combine(guide, "agents") + Path.DirectorySeparatorChar;
        var pages = Directory.EnumerateFiles(guide, "*.md", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(agents, StringComparison.Ordinal))
            .Select(path => (Name: Path.GetRelativePath(guide, path), Lines: File.ReadAllLines(path)))
            .ToArray();
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        // A term a Guide page links to opens in the window too, so its title and description are scanned with it.
        var terms = pages.SelectMany(page => page.Lines).SelectMany(line => TermLink().Matches(line))
            .Select(link => Uri.UnescapeDataString(link.Groups[1].Value)).Distinct()
            .Select(code => catalog.Find(HelpEntryKind.Term, code)
                ?? throw new InvalidOperationException($"The Guide links to a missing term '{code}'."))
            .SelectMany(term => new[] { $"{term.Title} ⟨term {term.Code}⟩", $"{term.Description} ⟨term {term.Code}⟩" });
        var shown = pages.SelectMany(page => page.Lines
                .Select(line => $"{page.Name}: {LinkTarget().Replace(line, "]")}"))
            .Concat(terms);

        AssertWindowWords(shown);
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

    private static void AssertWindowWords(IEnumerable<string> shown)
    {
        var retired = shown.Where(text => RetiredWord().IsMatch(FileName().Replace(text, string.Empty)))
            .Distinct().ToArray();
        Assert.True(retired.Length == 0, "Retired words in the window:" + Environment.NewLine +
            string.Join(Environment.NewLine, retired));
    }

    // Hidden text is scanned too: every page holds states the sample data does not reach.
    private static IEnumerable<string> Rendered(Window window)
    {
        var texts = new List<string?>();
        foreach (var control in window.GetLogicalDescendants().OfType<Control>())
        {
            texts.Add(AutomationProperties.GetName(control));
            texts.Add(AutomationProperties.GetHelpText(control));
            texts.Add(ToolTip.GetTip(control) as string);
            switch (control)
            {
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
