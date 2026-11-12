using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using SIL.Motif.App.Preview;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;
using Xunit;

namespace SIL.Motif.Tests.App;

// A Discussion shown and edited as FieldWorks Markdown (SIL.TextEditing ADR 0035).
public sealed partial class GdrPreviewWindowTests
{
    private static GdrShowResponse WithLinks(GdrSummary record) => Canned(record) with
    {
        Discussion =
        [
            new GdrParagraph("Normal", false, "en", Props(style: "Normal"),
            [
                new GdrRun("See ", "en", null, Props()),
                new GdrRun("GDR-9", "en", null, Props(), GdrReader.LinkData(ProjectName, Linked.Id),
                    new GdrLinkTarget(GdrLinkState.Resolved, "Grammar Design Record", "title-9", "")),
                new GdrRun(" and ", "en", null, Props()),
                new GdrRun("gone", "en", null, Props(), GdrReader.LinkData(ProjectName, CanonicalIdOf("0d3c0f4e-2b1d-4c8e-9f70-1d2e3f405162")),
                    new GdrLinkTarget(GdrLinkState.Broken, null, null, "")),
                new GdrRun(".", "en", null, Props()),
            ], "para-1"),
            Canned(record).Discussion[1],
        ],
    };

    private static string CanonicalIdOf(string guid) => SIL.Motif.Contract.Ids.CanonicalId.FromGuid(new Guid(guid)).Value;

    private static string Markdown(GdrPreviewWindow window) => window.Field!.SourceView!.Editor.Document.PlainText();

    private static void Settle(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(done());
    }

    [Fact]
    public void TheMarkdownButtonShowsWhatTheProjectHoldsAndBackWithNothingChangedIsNoEdit()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Staging(store, WithLinks);
            using var closer = window.CloseOnExit();
            var field = window.Field!;
            var before = field.Editor.Document.Paragraphs.ToArray();

            var toggle = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Markdown"));
            toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(field.IsSourceMode);
            Assert.Equal("Editing as Markdown", window.StatusText);
            Assert.Equal(AutomationLiveSetting.Polite,
                AutomationProperties.GetLiveSetting(window.GetLogicalDescendants().OfType<TextBlock>()
                    .Single(block => block.Text == "Editing as Markdown" && block.FindLogicalAncestorOfType<RichTextField>() is null)));

            // The link look and the broken-link marker are the preview's, not the project's.
            var markdown = Markdown(window);
            Assert.Contains("GDR-9", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain(GdrDiscussion.BrokenMarker.Trim(), markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("underline", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("color", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(field.SourceView!.LanguageDiagnostics, finding => finding.Severity == DiagnosticSeverity.Error);

            // In Markdown the formatting is typed: only the toggle back stays on the bar.
            Assert.False(List(window, "Paragraph style").IsEnabled);
            Assert.False(List(window, "Character style").IsEnabled);
            Assert.True(toggle.IsEnabled);

            toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(field.IsSourceMode);
            Assert.Equal("Back to rich text", window.StatusText);
            Assert.True(List(window, "Paragraph style").IsEnabled);
            Assert.Equal(before, field.Editor.Document.Paragraphs);
            Assert.Null(window.Session!.Pending);
            Assert.Empty(store.Staged);
        });
    }

    [Fact]
    public void AnEditInMarkdownStagesAtOnceWithItsLinksAsTheyWere()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Staging(store, WithLinks);
            using var closer = window.CloseOnExit();
            var field = window.Field!;
            var link = field.Editor.Document.Paragraphs[0].Runs.Single(run => run.Text == "GDR-9");

            Assert.True(window.ToggleSourceMode());
            field.SourceView!.Editor.SetCaret(new TextPosition(0, 0));
            window.KeyTextInput("**Now** ");
            Assert.True(window.ToggleSourceMode());

            Settle(() => store.Staged.Count == 1);
            Assert.Equal("Back to rich text", window.StatusText);
            var paragraph = field.Editor.Document.Paragraphs[0];
            Assert.Equal("Now See GDR-9 and gone" + GdrDiscussion.BrokenMarker + ".", paragraph.Text);
            Assert.Equal("para-1", paragraph.Key);
            var shown = paragraph.Runs.Single(run => run.Text == "GDR-9");
            Assert.Equal(link.ObjData, shown.ObjData);
            Assert.True(shown.Props.ContentEquals(link.Props));
            var staged = StagedFirst(store);
            Assert.Equal("Now See GDR-9 and gone.", string.Concat(staged.Runs.Select(run => run.Text)));
            Assert.Equal(TextPropertyNumbers.ToggleOn, staged.Runs[0].Props!.Ints[TextPropertyNumbers.Bold].Value);
            Assert.False(staged.Runs.Single(run => run.Text == "GDR-9").Props!.Ints.ContainsKey(TextPropertyNumbers.Underline));
        });
    }

    [Fact]
    public void InMarkdownTheContextMenuIsAboutTheMarkdownAndTheHostsUndoAndFormattingWait()
    {
        _avalonia.Invoke(() =>
        {
            var window = Staging(new FakeStore());
            using var closer = window.CloseOnExit();
            var field = window.Field!;
            Select(window, 0, 0, 5);
            Assert.True(window.ToggleBold());
            Assert.Equal(1, window.Session!.UndoSteps);

            Assert.True(window.ToggleSourceMode());
            var view = field.SourceView!;
            ContextMenuRequestedEventArgs? asked = null;
            field.ContextMenuRequested += (_, e) => asked = e;
            view.Editor.SetCaret(new TextPosition(0, 2));
            Press(window, Key.F10, RawInputModifiers.Shift);
            Assert.NotNull(asked);
            Assert.Same(view, asked!.Field);
            var menu = PreviewSourceMode.Menu(view, asked.Info).Select(item => item.Header as string ?? "").ToArray();
            Assert.Equal(["Cut", "Copy", "Paste"], menu[^3..]);

            Assert.False(window.Session.Undo());
            Assert.False(window.ToggleBold());
            Assert.False(window.SetWritingSystem("ar"));
            Assert.Equal(1, window.Session.UndoSteps);

            field.DiscardSourceChanges();
            Assert.True(window.Session.Undo());
        });
    }

    [Fact]
    public void ALockedDiscussionCannotBeShownAsMarkdownAndApplyGoesBackToRichTextFirst()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore { PendingGate = new TaskCompletionSource(), ApplyGate = new TaskCompletionSource() };
            var window = Staging(store);
            using var closer = window.CloseOnExit();
            var field = window.Field!;
            Assert.True(field.IsReadOnly);
            Assert.False(window.ToggleSourceMode());
            store.PendingGate.SetResult();
            Completed(window.Loading);

            Assert.True(window.ToggleSourceMode());
            field.SourceView!.Editor.SetCaret(new TextPosition(0, 0));
            window.KeyTextInput("[t](/lexicon/nothing-here) ");
            var refused = window.ApplyAsync();
            Assert.True(refused.IsCompleted);
            Assert.False(refused.Result);
            Assert.Equal(GdrPreviewWindow.MarkdownFixFirstMessage, window.StatusText);
            Assert.True(field.IsSourceMode);
            Assert.Equal(0, store.Applies);

            var view = field.SourceView!;
            view.Editor.SetCaret(new TextPosition(0, 0));
            view.Editor.SetCaret(new TextPosition(0, "[t](/lexicon/nothing-here) ".Length), extend: true);
            window.KeyTextInput("zz ");
            var applying = window.ApplyAsync();
            Settle(() => store.Applies == 1);
            Assert.False(field.IsSourceMode);
            Assert.True(field.IsReadOnly);
            Assert.False(window.ToggleSourceMode());
            store.ApplyGate.SetResult();
            Assert.True(Completed(applying));
            Assert.StartsWith("zz ", StagedFirst(store).Runs[0].Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ALinksOwnUnderlineAndColourFromMarkdownAreKeptAndHalfTypedMarkdownLeavesNoTrace()
    {
        var discussion = GdrDiscussion.From(WithLinks(First));
        var objData = GdrReader.LinkData(ProjectName, Linked.Id);
        Paragraph Link(int? underline)
        {
            var paragraph = new Paragraph { DefaultWs = "en" };
            var run = new Run { Ws = "en", Text = "GDR-9", ObjData = objData };
            if (underline is { } value) run.Props.SetInt(TextPropertyNumbers.Underline, value, 0);
            paragraph.Runs.Add(run);
            return paragraph;
        }

        // Typed as Markdown: shown and staged as typed, not as the preview's link look.
        var typed = discussion.AsShown(Link(TextPropertyNumbers.UnderlineDouble));
        Assert.Equal(TextPropertyNumbers.UnderlineDouble, typed.Runs[0].Props.IntProps[TextPropertyNumbers.Underline].Value);
        Assert.False(typed.Runs[0].Props.IntProps.ContainsKey(TextPropertyNumbers.ForeColor));
        Assert.Equal(TextPropertyNumbers.UnderlineDouble, discussion.AsProject(typed).Runs[0].Props.IntProps[TextPropertyNumbers.Underline].Value);
        Assert.Equal((0, TextPropertyNumbers.UnderlineDouble), discussion.ProjectRuns(typed)[0].Props!.Ints[TextPropertyNumbers.Underline]);

        // That reading recorded nothing: the link without a look of its own is shown as a link and stored with none.
        var plain = discussion.AsShown(Link(null));
        Assert.Equal(TextPropertyNumbers.UnderlineSingle, plain.Runs[0].Props.IntProps[TextPropertyNumbers.Underline].Value);
        Assert.False(discussion.AsProject(plain).Runs[0].Props.IntProps.ContainsKey(TextPropertyNumbers.Underline));
        Assert.False(discussion.ProjectRuns(plain)[0].Props!.Ints.ContainsKey(TextPropertyNumbers.ForeColor));
    }

    [Fact]
    public void ALinkTypedWithAGuidThePreviewCannotCheckIsTakenAsWrittenAndNotMarkedBroken()
    {
        var discussion = GdrDiscussion.From(Canned(First));
        var form = new DiscussionSourceForm(discussion, ProjectName);
        var guid = new Guid("0d3c0f4e-2b1d-4c8e-9f70-1d2e3f405163");
        var read = form.Read($"x [t](/objects/{guid:D})", singleParagraph: false);
        Assert.False(read.HasErrors, string.Join("; ", read.Diagnostics.Select(finding => finding.Message)));
        var link = read.Paragraphs[0].Runs.Single(run => run.Text == "t");
        Assert.Contains(guid.ToString("D"), link.ObjData!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(read.Paragraphs[0].Runs, GdrDiscussion.IsMarker);
        // A path with no tool of its own, such as /objects/, gives FieldWorks a link that names no tool to open it in.
        Assert.Equal($"\u0004silfw://localhost/link?database%3dthis%24%26guid%3d{guid:D}%26tag%3d", link.ObjData);
    }

    [Fact]
    public void ARecordWhoseMarkdownCannotGoBackStaysShownAndAWindowWithItStaysOpen()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Staging(store);
            using var closer = window.CloseOnExit();
            var field = window.Field!;
            var shown = window.Records[0].Id;
            Assert.True(window.ToggleSourceMode());
            field.SourceView!.Editor.SetCaret(new TextPosition(0, 0));
            window.KeyTextInput("[t](/lexicon/nothing-here) ");

            window.Show(Linked.Id);
            Assert.Same(field, window.Field);
            Assert.Equal(GdrPreviewWindow.MarkdownFixFirstMessage, window.StatusText);
            window.Close();
            Assert.True(window.IsVisible);
            Assert.True(field.IsSourceMode);

            // Fixed, the switch takes the Markdown back to rich text and stages it on the way.
            var view = field.SourceView!;
            view.Editor.SetCaret(new TextPosition(0, 0));
            view.Editor.SetCaret(new TextPosition(0, "[t](/lexicon/nothing-here) ".Length), extend: true);
            window.KeyTextInput("zz ");
            window.Show(Linked.Id);
            Assert.False(field.IsSourceMode);
            Assert.NotSame(field, window.Field);
            Settle(() => store.Staged.Count == 1);
            Assert.Equal(shown, store.Staged[0].RecordId);
        });
    }

    [Fact]
    public void ALongDiscussionWithAParagraphAddedAtTheTopAndTheLastEditedKeepsEveryKeyOnItsOwnText()
    {
        // 101 by 102 paragraphs is past the pair-by-pair budget: the keys hold only through the fingerprint.
        const int count = 101;
        GdrShowResponse Long(GdrSummary record) => Canned(record) with
        {
            Discussion = Enumerable.Range(0, count).Select(i => new GdrParagraph("Normal", false, "en", Props(style: "Normal"),
                [new GdrRun($"Paragraph {i}.", "en", null, Props())], $"para-{i}")).ToArray(),
        };
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Staging(store, Long);
            using var closer = window.CloseOnExit();
            var field = window.Field!;
            Assert.True(count * (count + 1) > DocumentEditor.PairwiseComparisonBudget);

            Assert.True(window.ToggleSourceMode());
            var view = field.SourceView!.Editor;
            var lines = view.Document.Paragraphs.Select(paragraph => paragraph.Text).ToList();
            var separated = lines.Count > 1 && lines[1].Length == 0;
            var last = lines.FindLastIndex(line => line.Length > 0);
            lines[last] += " Edited.";
            lines.InsertRange(0, separated ? ["Added.", ""] : ["Added."]);
            Assert.True(view.ReplaceParagraphs(lines.Select(line => new Paragraph { Runs = { new Run { Ws = view.Document.Paragraphs[0].DefaultWs, Text = line } } }).ToArray(),
                (a, b) => a.Text == b.Text, "edit"));
            Assert.True(window.ToggleSourceMode());
            Assert.False(field.IsSourceMode);

            var paragraphs = field.Editor.Document.Paragraphs;
            Assert.Equal(count + 1, paragraphs.Count);
            Assert.Equal("Added.", paragraphs[0].Text);
            for (var i = 0; i < count; i++)
            {
                Assert.Equal($"para-{i}", paragraphs[i + 1].Key);
                Assert.StartsWith($"Paragraph {i}.", paragraphs[i + 1].Text, StringComparison.Ordinal);
            }
            Assert.Equal($"Paragraph {count - 1}. Edited.", paragraphs[count].Text);
        });
    }

    [Fact]
    public void OnlyThePreviewWindowsGiveAFieldASourceForm()
    {
        // Ctrl+Shift+M works in a field with a source form, so outside the preview gate no field has one.
        var app = Path.Combine(SIL.Motif.Generator.RepoPaths.FindRepoRoot(), "src", "SIL.Motif.App");
        var assigns = new System.Text.RegularExpressions.Regex(@"\bSourceForm\s*=(?!=)");
        var setting = Directory.GetFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(file => assigns.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(app, file).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Preview/GdrPreviewWindow.cs", "Preview/SenseDefinitionPreviewWindow.cs"], setting);
    }
}
