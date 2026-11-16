using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Guide;
using SIL.Motif.Runner.Operations;
using Avalonia.Headless;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.TextEditing;
using Xunit;
using static SIL.Motif.Tests.App.GrammarGuideTestSupport;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class GrammarGuideEditingTests
{
    private static async Task<(GrammarGuidePageModel Model, GrammarGuidePage Page, MotifTestWindow Window, WorkspaceContext Context, FakeGdrEditStore Store)>
        EditAsync(FakeCommandClient fake)
    {
        var store = new FakeGdrEditStore();
        fake.GrammarGuideEditStore = store;
        var context = WorkspaceContextTests.NewContext(fake);
        var model = new GrammarGuidePageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        var page = new GrammarGuidePage(model);
        var window = new MotifTestWindow { Width = 1100, Height = 700, Content = page };
        window.Show();
        model.SelectedSection = model.Parts[0];
        PageScreenshots.Settle(window);
        page.FindControl<Avalonia.Controls.Primitives.ToggleButton>("EditToggle")!.IsChecked = true;
        return (model, page, window, context, store);
    }

    private static string EditorText(GuideSectionEditor editor) =>
        string.Concat(editor.Field!.Editor.Document.Paragraphs.SelectMany(paragraph => paragraph.Runs.Select(run => run.Text)));

    private static async Task SettleAsync(MotifTestWindow window)
    {
        await Task.Delay(50);
        PageScreenshots.Settle(window);
    }

    [Fact]
    public void TypingGoesToTheEditStoreAndNeverToTheProjectUntilApplied()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, page, window, context, store) = await EditAsync(Fake(Hierarchy));
            try
            {
                page.Editor.Field!.Focus();
                window.KeyTextInput("abc");
                page.Editor.Session!.EndSession();
                await SettleAsync(window);

                var staged = Assert.IsType<TextParagraphsPayload>(store.Staged(PartOne.Id));
                Assert.Contains("abc", string.Concat(staged.Changed.SelectMany(change => change.Runs.Select(run => run.Text))));
                Assert.Equal(0, store.Applies);

                Assert.True(await page.Editor.ApplyAsync());
                Assert.Equal(1, store.Applies);
                Assert.Equal(MotifAppStrings.GuideApplied, page.Editor.StatusText);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void UnsavedTypingSurvivesANewBaselineFromAnExternalChange()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var changedInFieldWorks = false;
            var fake = Fake(Hierarchy, record => Show(record, Paragraph("Normal", "p-" + record.Id,
                Plain(changedInFieldWorks ? "Changed in FieldWorks" : record.Title))));
            var (model, page, window, context, _) = await EditAsync(fake);
            try
            {
                var field = page.Editor.Field!;
                field.Focus();
                window.KeyTextInput("typed ");
                var before = EditorText(page.Editor);
                Assert.StartsWith("typed ", before);

                changedInFieldWorks = true;
                await model.BaselineCapturedAsync(CancellationToken.None);
                await SettleAsync(window);

                Assert.Same(field, page.Editor.Field);
                Assert.Equal(before, EditorText(page.Editor));
                Assert.Equal(PartOne.Id, model.SelectedRecord!.Id);
                Assert.Equal(PartOne.Id, page.Editor.ShownId);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ASectionNobodyEditedIsReadAgainAfterANewBaselineSoItShowsTheProjectAsItNowIs()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var changedInFieldWorks = false;
            var fake = Fake(Hierarchy, record => Show(record, Paragraph("Normal", "p-" + record.Id,
                Plain(changedInFieldWorks ? "Changed in FieldWorks" : record.Title))));
            var (model, page, window, context, _) = await EditAsync(fake);
            try
            {
                Assert.Equal("Nouns", EditorText(page.Editor));

                changedInFieldWorks = true;
                await model.BaselineCapturedAsync(CancellationToken.None);
                await SettleAsync(window);

                Assert.Equal("Changed in FieldWorks", EditorText(page.Editor));
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void UndoTakesTypingBackAndAHeadingOfAnotherSectionLeavesTheEditInTheDraft()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (model, page, window, context, store) = await EditAsync(Fake(Hierarchy));
            try
            {
                var field = page.Editor.Field!;
                field.Focus();
                window.KeyTextInput("abc");
                window.KeyPress(Avalonia.Input.Key.Z, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                Assert.Equal("Nouns", EditorText(page.Editor));

                window.KeyTextInput("kept");
                model.SelectedSection = model.Parts[1];
                await SettleAsync(window);

                Assert.Equal(PartTwo.Id, page.Editor.ShownId);
                Assert.NotNull(store.Staged(PartOne.Id));
                Assert.Equal(0, store.Applies);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ClosingTheProjectDropsEverythingTheEditorHeld()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (model, page, window, context, _) = await EditAsync(Fake(Hierarchy));
            try
            {
                Assert.NotNull(page.Editor.Field);

                context.ClearProject();
                await SettleAsync(window);

                Assert.Null(page.Editor.Field);
                Assert.Null(page.Editor.ShownId);
                Assert.Empty(model.Parts);
                Assert.Equal(MotifAppStrings.GuideNoProject, model.EmptyText);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }
}
