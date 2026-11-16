using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Guide;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using static SIL.Motif.Tests.App.GrammarGuideTestSupport;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class GrammarGuideGeneratedBlocksTests
{
    private static readonly Guid Noun = new("6a3c0f4e-2b1d-4c8e-9f70-1d2e3f405162");
    private static readonly Guid Deleted = new("7b4d1a5f-3c2e-4d9f-8a81-2e3f4a516273");

    private sealed class RecordingLauncher : IUriLauncher
    {
        public List<Uri> Launched { get; } = [];

        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Launched.Add(uri);
            return Task.FromResult(true);
        }
    }

    private static GdrShowResponse WithLinks() => Show(PartOne,
        Paragraph("Normal", "p1",
            Plain("See "),
            Link("noun", ObjectLink(Noun), new GdrLinkTarget(GdrLinkState.Resolved, "Part of speech", "Noun", "")),
            Plain(" and "),
            Link("noun again", ObjectLink(Noun), new GdrLinkTarget(GdrLinkState.Resolved, "Part of speech", "Noun", "")),
            Plain(" and "),
            Link("old rule", ObjectLink(Deleted), new GdrLinkTarget(GdrLinkState.Broken, null, null, ""))));

    [Fact]
    public void NamesAreListedOnceEachWithTheirKindAndMissingTargetsAreShownAsMissing()
    {
        var names = GuideGrammarName.From(WithLinks());

        Assert.Equal(["Noun", "old rule"], names.Select(name => name.Name));
        Assert.True(names[0].IsResolved);
        Assert.Equal("Part of speech", names[0].StateText);
        Assert.True(names[0].CanOpen);
        Assert.True(names[1].IsMissing);
        Assert.Equal(MotifAppStrings.GuideLinkMissing, names[1].StateText);
        Assert.False(names[1].CanOpen);
    }

    [Fact]
    public void LinksToOtherSectionsAndExternalTargetsAreNotGrammarObjects()
    {
        var other = Link("Agreement", GdrReader.LinkData("project-1", SIL.Motif.Contract.Ids.CanonicalId.FromGuid(new Guid("8c5e2b60-4d3f-4eaf-9b92-3f4a5b627384")).Value),
            new GdrLinkTarget(GdrLinkState.Resolved, "Grammar design record", "Agreement", ""));
        var outside = Link("site", "\u0004https://example.org/", new GdrLinkTarget(GdrLinkState.External, null, "https://example.org/", ""));
        var shown = Show(PartOne, Paragraph("Normal", "p1", Plain("x"), other, outside));

        var names = GuideGrammarName.From(shown);

        Assert.DoesNotContain(names, name => name.Name == "Agreement");
        Assert.All(names, name => Assert.False(name.CanOpen));
    }

    [Fact]
    public void ThePageListsInTheGrammarWithOpenInFieldWorksAndLaunchesTheLink()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var launcher = new RecordingLauncher();
            var fake = Fake(Hierarchy, _ => WithLinks());
            var context = WorkspaceContextTests.NewContext(fake, uriLauncher: launcher);
            var model = new GrammarGuidePageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new GrammarGuidePage(model);
            var window = new MotifTestWindow { Width = 1100, Height = 700, Content = page };
            try
            {
                window.Show();
                model.SelectedSection = model.Parts[0];
                PageScreenshots.Settle(window);

                var names = page.GetVisualDescendants().OfType<GuideGrammarNames>().Single();
                Assert.True(names.IsEffectivelyVisible);
                var open = names.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
                var button = Assert.Single(open);
                Assert.Equal(string.Format(MotifAppStrings.GuideOpenNameFormat, "Noun"), Avalonia.Automation.AutomationProperties.GetName(button));
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(50);
                PageScreenshots.Settle(window);

                var uri = Assert.Single(launcher.Launched);
                Assert.Equal("silfw", uri.Scheme);
                Assert.Contains(Noun.ToString("D"), uri.OriginalString, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(names.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == MotifAppStrings.GuideLinkMissing);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ASectionWithNoLinksHidesInTheGrammarAltogether()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var context = WorkspaceContextTests.NewContext(Fake(Hierarchy));
            var model = new GrammarGuidePageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new GrammarGuidePage(model);
            var window = new MotifTestWindow { Width = 1100, Height = 700, Content = page };
            try
            {
                window.Show();
                model.SelectedSection = model.Parts[0];
                PageScreenshots.Settle(window);

                Assert.False(model.HasGrammarNames);
                Assert.False(page.GetVisualDescendants().OfType<GuideGrammarNames>().Single().IsEffectivelyVisible);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void WhileNoReviewedSourceSuppliesThemNoExamplesCountsHistoryOrDisagreementsAreShownOrInvented()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var context = WorkspaceContextTests.NewContext(Fake(Hierarchy, _ => WithLinks()));
            var model = new GrammarGuidePageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new GrammarGuidePage(model);
            var window = new MotifTestWindow { Width = 1100, Height = 700, Content = page };
            try
            {
                window.Show();
                model.SelectedSection = model.Parts[0];
                PageScreenshots.Settle(window);

                Assert.False(model.Examples.IsVisible);
                Assert.Empty(model.Examples.Examples);
                Assert.Null(model.Examples.ApprovedCount);
                Assert.False(page.GetVisualDescendants().OfType<GuideExamples>().Single().IsEffectivelyVisible);
                var shownText = page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible)
                    .Select(text => text.Text ?? "").ToArray();
                Assert.DoesNotContain(shownText, text => text.Contains(MotifAppStrings.GuideExamplesHeading, StringComparison.Ordinal));
                Assert.DoesNotContain(shownText, text => text.Contains("coming soon", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(shownText, text => text.Contains("disagree", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(shownText, text => text.Contains("approved", StringComparison.OrdinalIgnoreCase));
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ExamplesAreShownOnlyWhenASourceSuppliesThemAndHiddenAgainWhenTheSectionChanges()
    {
        var examples = new GrammarGuideExamplesViewModel();
        Assert.False(examples.IsVisible);

        examples.Supply([new GuideExample("Text 1, line 2", "alikula", "Noun class 1")], approvedCount: 4);

        Assert.True(examples.IsVisible);
        Assert.Equal(4, examples.ApprovedCount);

        examples.Supply([], approvedCount: 4);

        Assert.False(examples.IsVisible);
        Assert.Null(examples.ApprovedCount);
    }
}
