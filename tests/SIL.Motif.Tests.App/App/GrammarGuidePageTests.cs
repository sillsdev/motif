using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using static SIL.Motif.Tests.App.GrammarGuideTestSupport;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class GrammarGuidePageTests
{
    private static async Task<(GrammarGuidePageModel Model, GrammarGuidePage Page, MotifTestWindow Window, WorkspaceContext Context)> OpenAsync(
        FakeCommandClient fake, int width = 1100)
    {
        var context = WorkspaceContextTests.NewContext(fake);
        var model = new GrammarGuidePageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        var page = new GrammarGuidePage(model);
        var window = new MotifTestWindow { Width = width, Height = 700, Content = page };
        window.Show();
        PageScreenshots.Settle(window);
        return (model, page, window, context);
    }

    [Fact]
    public void PartsComeFromTheRecordsOwnHierarchyAndSectionsAreFiledUnderThem()
    {
        var outline = GrammarGuideSection.Outline(Hierarchy);

        Assert.Equal(["gdr/part-1", "gdr/part-2"], outline.Select(part => part.Id));
        Assert.Equal(["gdr/sec-a", "gdr/sec-b"], outline[0].Children.Select(section => section.Id));
        Assert.Empty(outline[1].Children);
        Assert.All(outline, part => Assert.True(part.IsPart));
        Assert.All(outline[0].Children, section => Assert.False(section.IsPart));
        Assert.Equal("1.1  Noun classes", outline[0].Children[0].Label);
    }

    [Fact]
    public void ARecordWhoseParentIsMissingOrLoopsIsStillShownAsAPart()
    {
        var orphan = new GdrSummary("gdr/o", "9", "Orphan", "", "gdr/absent");
        var one = new GdrSummary("gdr/l1", "7", "Loop one", "", "gdr/l2");
        var two = new GdrSummary("gdr/l2", "8", "Loop two", "", "gdr/l1");

        var outline = GrammarGuideSection.Outline([orphan, one, two]);

        Assert.Contains(outline, part => part.Id == orphan.Id);
        Assert.Equal(3, outline.SelectMany(part => part.Flatten()).Count());
    }

    [Fact]
    public void TheOutlineShowsPartsWithTheirSectionsAndSelectingOneOpensItsDiscussion()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = Fake(Hierarchy);
            var (model, page, window, context) = await OpenAsync(fake);
            try
            {
                var tree = page.FindControl<TreeView>("GrammarGuideRecords")!;
                Assert.Equal(2, tree.ItemCount);
                Assert.Equal(MotifAppStrings.GuideSectionsName, Avalonia.Automation.AutomationProperties.GetName(tree));

                model.SelectedSection = model.Parts[0].Children[0];
                PageScreenshots.Settle(window);

                Assert.Equal(SectionA.Id, model.SelectedRecord!.Id);
                Assert.Equal(SectionA.Id, Assert.Single(fake.GrammarGuideShowRequests).Id);
                Assert.Equal(SectionA.Id, page.Editor.ShownId);
                Assert.Equal("1.1  Noun classes", model.SelectedTitle);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "1.1  Noun classes");
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ASectionsHeadingsComeFromItsStoredHeadingParagraphs()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = Fake(Hierarchy, record => Show(record,
                Paragraph("Heading 1", "h1", Plain("Classes")),
                Paragraph("Normal", "n1", Plain("Body text.")),
                Paragraph("Heading 2", "h2", Plain("Class 1")),
                Paragraph("Heading 2", "h3", Plain("  "))));
            var (model, page, window, context) = await OpenAsync(fake);
            try
            {
                model.SelectedSection = model.Parts[0];
                PageScreenshots.Settle(window);

                Assert.Equal([(1, "Classes"), (2, "Class 1")], model.Headings.Select(heading => (heading.Level, heading.Text)));
                Assert.False(model.Headings[0].IsSubheading);
                Assert.True(model.Headings[1].IsSubheading);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == MotifAppStrings.GuideOnThisPage);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Class 1");
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ASectionIsReadOnlyUntilEditAndDoneReturnsItToReading()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (model, page, window, context) = await OpenAsync(Fake(Hierarchy));
            try
            {
                model.SelectedSection = model.Parts[0];
                PageScreenshots.Settle(window);
                var toggle = page.FindControl<Avalonia.Controls.Primitives.ToggleButton>("EditToggle")!;
                Assert.Equal(MotifAppStrings.GuideEdit, toggle.Content);
                Assert.True(page.Editor.Field!.IsReadOnly);

                toggle.IsChecked = true;
                Assert.Equal(MotifAppStrings.GuideDone, toggle.Content);
                Assert.False(page.Editor.Field!.IsReadOnly);

                toggle.IsChecked = false;
                Assert.Equal(MotifAppStrings.GuideEdit, toggle.Content);
                Assert.True(page.Editor.Field!.IsReadOnly);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AProjectWithNoRecordsSaysSoInsteadOfShowingAnEmptyOutline()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (model, page, window, context) = await OpenAsync(Fake([]));
            try
            {
                Assert.Equal(MotifAppStrings.GuideNoRecords, model.EmptyText);
                Assert.False(page.FindControl<TreeView>("GrammarGuideRecords")!.IsEffectivelyVisible);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == MotifAppStrings.GuideNoRecords && text.IsEffectivelyVisible);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ARefusedListShowsWhyAndNothingElse()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.GrammarGuideListHandler = (_, _) => Task.FromResult(CommandOutcome<GdrListResponse>.Refused(
                new Refusal("guide.unreadable", FailureReason.Busy, "The project is locked.")));
            var (model, _, window, context) = await OpenAsync(fake);
            try
            {
                Assert.True(model.HasNotice);
                Assert.Contains("The project is locked.", model.Notice);
                Assert.Empty(model.Parts);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AListStillNamedGrammarDesignRecordsOpensReadOnlyWithASentenceSayingToRenameIt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = Fake(Hierarchy);
            fake.GrammarGuideListHandler = (_, _) => Task.FromResult(CommandOutcome<GdrListResponse>.Success(
                new(true, Hierarchy) { UsesOldListName = true }));
            var (model, page, window, context) = await OpenAsync(fake);
            try
            {
                model.SelectedSection = model.Parts[0].Children[0];
                PageScreenshots.Settle(window);

                Assert.NotEmpty(model.Parts);
                Assert.Equal(SectionA.Id, page.Editor.ShownId);
                Assert.False(model.CanEdit);
                Assert.False(page.FindControl<ToggleButton>("EditToggle")!.IsEffectivelyVisible);
                Assert.True(page.Editor.Field!.IsReadOnly);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == MotifAppStrings.GuideListOldName && text.IsEffectivelyVisible);
                Assert.Contains("FieldWorks (Lists)", MotifAppStrings.GuideListOldName, StringComparison.Ordinal);
                Assert.Contains("Grammar Design Records", MotifAppStrings.GuideListOldName, StringComparison.Ordinal);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AListNamedGrammarGuideShowsNoRenameSentenceAndCanBeEdited()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (model, page, window, context) = await OpenAsync(Fake(Hierarchy));
            try
            {
                model.SelectedSection = model.Parts[0].Children[0];
                PageScreenshots.Settle(window);

                Assert.True(model.CanEdit);
                Assert.False(model.HasListNotice);
                Assert.True(page.FindControl<ToggleButton>("EditToggle")!.IsEffectivelyVisible);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TwoListsThatCouldBeTheGuideAreRefusedWithASentenceNamingBothNames()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.GrammarGuideListHandler = (_, _) => Task.FromResult(CommandOutcome<GdrListResponse>.Refused(
                new Refusal("guide.ambiguous", FailureReason.Refused, "2 possibility lists are named 'Grammar Guide' or 'Grammar Design Records'.")));
            var (model, _, window, context) = await OpenAsync(fake);
            try
            {
                Assert.Equal(MotifAppStrings.GuideListAmbiguous, model.Notice);
                Assert.Contains("Grammar Guide", model.Notice, StringComparison.Ordinal);
                Assert.Contains("Grammar Design Records", model.Notice, StringComparison.Ordinal);
                Assert.Empty(model.Parts);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ANarrowWindowStacksTheOutlineAboveTheSection()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (model, page, window, context) = await OpenAsync(Fake(Hierarchy), width: 560);
            try
            {
                model.SelectedSection = model.Parts[0];
                PageScreenshots.Settle(window);
                var outline = page.FindControl<Border>("Outline")!;
                var reading = page.FindControl<ScrollViewer>("Reading")!;

                Assert.True(outline.Bounds.Bottom <= reading.Bounds.Top + 1);
                Assert.True(outline.Bounds.Width >= reading.Bounds.Width - 1);

                window.Width = 1200;
                PageScreenshots.Settle(window);

                Assert.True(outline.Bounds.Right <= reading.Bounds.Left + 1);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }
}
