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
    public void OnlyThePreviewWindowsGiveAFieldASourceForm()
    {
        // Ctrl+Shift+M works in a field with a source form, so outside the preview gate no field has one.
        var app = Path.Combine(SIL.Motif.Generator.RepoPaths.FindRepoRoot(), "src", "SIL.Motif.App");
        var setting = Directory.GetFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("SourceForm =", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(app, file).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Preview/GdrPreviewWindow.cs", "Preview/SenseDefinitionPreviewWindow.cs"], setting);
    }
}
