using Avalonia.Headless;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.Compare;
using SIL.Motif.App.Preview;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

/// <summary>
/// A Discussion edited as FieldWorks Markdown (SIL.TextEditing ADR 0035) in a project built at run time from
/// <see cref="SeededGdrs"/> (ADR 0076): the window, the real stores and commands, and LibLCM in place of FieldWorks.
/// </summary>
[Trait("MotifTestLevel", "Integration")]
[Collection(LcmCacheTestCollection.Name)]
public sealed class SourceModeRealProjectTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ADiscussionEditedAsMarkdownStagesAppliesAndReadsBackWithItsFormattingAndLinks()
    {
        var path = ProjectWithGdrs();
        var root = RootFor(path);
        var record = Show(path, "GDR-1");
        var id = record.Record.Id;
        var starting = Text(record.Discussion[0]);
        string? markdown = null;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(root));
            var window = new GdrPreviewWindow([record.Record], shown => GdrQuery.Show(new GdrShowRequest(path, shown)).Value,
                new GdrEditStore(path, root, runner), () => Task.FromResult(true), projectName: Path.GetFileNameWithoutExtension(path));
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(id);
            await window.Loading;
            var field = window.Field!;
            field.Focus();

            // There and back with nothing changed is no edit.
            Assert.True(window.ToggleSourceMode());
            Assert.True(field.IsSourceMode);
            Assert.Equal("Editing as Markdown", window.StatusText);
            markdown = field.SourceView!.Editor.Document.PlainText();
            Assert.True(window.ToggleSourceMode(), markdown);
            Assert.False(field.IsSourceMode);
            Assert.Equal("Back to rich text", window.StatusText);
            Assert.Null(window.Session!.Pending);

            // Ctrl+Shift+M in the field, a bold word typed as Markdown, and the bar's button back.
            field.Focus();
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.M, Avalonia.Input.RawInputModifiers.Control | Avalonia.Input.RawInputModifiers.Shift);
            Assert.True(field.IsSourceMode);
            var view = field.SourceView!;
            view.Editor.SetCaret(new TextPosition(0, 0));
            window.KeyTextInput("**b** zz ");
            Assert.True(window.ToggleSourceMode(), view.Editor.Document.PlainText());
            Assert.NotNull(window.Session!.Pending);
            await Staged(path, id);
            for (var i = 0; i < 20; i++) await Task.Delay(25);
            Assert.Equal("Back to rich text", window.StatusText);
            Assert.True(await window.ApplyAsync(), window.StatusText);
        }, TimeSpan.FromMinutes(4));

        var applied = Show(path, "GDR-1").Discussion;
        Assert.Equal("b zz " + starting, Text(applied[0]));
        var bold = applied[0].Runs.Single(run => run.Text == "b");
        Assert.Equal(1, bold.Properties.Ints[TextPropertyNumbers.Bold].Value);
        Assert.Equal("Emphasis", applied[0].Runs.Single(run => run.Text == "y").Style);
        Assert.Equal(SeededGdrs.HyperlinkData, applied[0].Runs.Single(run => run.Text == "link").ObjData);
        Assert.Equal(SeededGdrs.ObjectData, applied[0].Runs.Single(run => run.Text == "￼").ObjData);
        Assert.Equal(record.Discussion[1].Key, applied[1].Key);
        Assert.True(applied[1].RightToLeft);
        Assert.Contains("https://example.invalid/x", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownWithAnErrorStaysInSourceModeAndAStaleDiscussionGoesBackToRichTextBeforeCompare()
    {
        var path = ProjectWithGdrs();
        var root = RootFor(path);
        var record = Show(path, "GDR-1");
        var id = record.Record.Id;
        var starting = Text(record.Discussion[0]);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(root));
            var compared = new List<CompareWindow>();
            var window = new GdrPreviewWindow([record.Record], shown => GdrQuery.Show(new GdrShowRequest(path, shown)).Value,
                new GdrEditStore(path, root, runner), () => Task.FromResult(true), projectName: Path.GetFileNameWithoutExtension(path),
                compare: compare =>
                {
                    compared.Add(compare);
                    Assert.True(compare.Finish());
                    return Task.FromResult(compare.Outcome);
                });
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(id);
            await window.Loading;
            var field = window.Field!;
            field.Focus();
            window.KeyTextInput("zz ");
            window.Session!.EndSession();
            await Staged(path, id);

            InFieldWorks(path, "GDR-1", (cache, text) =>
            {
                var first = (IStTxtPara)text.ParagraphsOS[0];
                var contents = first.Contents.GetBldr();
                contents.Replace(contents.Length, contents.Length, " fw", TsStringUtils.MakeProps(null, cache.DefaultAnalWs));
                first.Contents = contents.GetString();
            });
            Assert.False(await window.ApplyAsync());
            Assert.False(await window.ApplyAsync());
            Assert.True(window.IsStaleStripShown);

            // An error keeps the field in Markdown, and Compare and Apply wait until it is fixed or thrown away.
            Assert.True(window.ToggleSourceMode());
            var view = field.SourceView!;
            view.Editor.SetCaret(new TextPosition(0, 0));
            window.KeyTextInput("[t](/lexicon/nothing-here) ");
            Assert.False(window.ToggleSourceMode());
            Assert.True(field.IsSourceMode);
            Assert.False(await window.CompareAsync());
            Assert.Equal(GdrPreviewWindow.MarkdownFixFirstMessage, window.StatusText);
            Assert.False(await window.ApplyAsync());
            Assert.Equal(GdrPreviewWindow.MarkdownFixFirstMessage, window.StatusText);
            Assert.Empty(compared);
            Assert.True(field.IsSourceMode);

            // Fixed, Compare first goes back to rich text with the Markdown's edit, then compares.
            view.Editor.SetCaret(new TextPosition(0, 0));
            view.Editor.SetCaret(new TextPosition(0, "[t](/lexicon/nothing-here) ".Length), extend: true);
            window.KeyTextInput("mm ");
            Assert.True(await window.CompareAsync(), window.StatusText);
            Assert.False(field.IsSourceMode);
            await window.Loading;
            Assert.Single(compared);
            Assert.True(await window.ApplyAsync(), window.StatusText);
        }, TimeSpan.FromMinutes(4));

        Assert.Equal("mm zz " + starting + " fw", Text(Show(path, "GDR-1").Discussion[0]));
    }

    // The window stages on a thread of its own, so the test waits until the Draft holds the edit.
    private static async Task Staged(string path, string id)
    {
        var request = new TextParagraphsFieldRequest(path, id, GdrDiscussionPayload.Field);
        for (var waited = 0; waited < 200; waited++)
        {
            if (ProposalCommands.PendingTextParagraphs(request) is { Succeeded: true, Value.Paragraphs: not null }) return;
            await Task.Delay(50);
        }
        Assert.Fail("The edit never reached the pending changes.");
    }

    private static void InFieldWorks(string path, string abbreviation, Action<LcmCache, IStText> change)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var record = cache.ServiceLocator.GetInstance<ICmPossibilityRepository>().AllInstances()
            .Single(possibility => possibility.Abbreviation.AnalysisDefaultWritingSystem.Text == abbreviation);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => change(cache, record.DiscussionOA));
        new FwDataProjectLoader().Save(cache);
    }

    private static string Text(GdrParagraph paragraph) => string.Concat(paragraph.Runs.Select(run => run.Text));

    private static GdrShowResponse Show(string path, string id)
    {
        var shown = GdrQuery.Show(new GdrShowRequest(path, id));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        return shown.Value!;
    }

    private string ProjectWithGdrs()
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
        return path;
    }

    private static string RootFor(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "source-mode-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
