using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using ShapedTextRun = Avalonia.Media.TextFormatting.ShapedTextRun;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextStylesTests
{
    [Fact]
    public void ResolvesStyleFontFeaturesDirectionAndPointSizeWithoutTheInterfaceFont()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var resolver = new TextStyles();
            resolver.ReplaceContext([WritingSystemTestData.Arabic]);

            var control = new TextBlock { Text = WritingSystemTestData.Form };
            resolver.Apply(control, TextStyleRequest.Linguistic(SeededProject.RightToLeftTag, "Paragraph"));

            var resolved = resolver.Resolve(TextStyleRequest.Linguistic(SeededProject.RightToLeftTag, "Paragraph"));
            Assert.Equal(20 * 96d / 72d, control.FontSize);
            Assert.Equal(FlowDirection.RightToLeft, control.FlowDirection);
            Assert.Equal(2, control.FontFeatures!.Count);
            Assert.Equal(["smcp", "cv01"], control.FontFeatures.Select(feature => feature.Tag.ToString()));
            Assert.False(resolved.RequestedFontInstalled);
            Assert.Same(resolved, resolver.Resolve(TextStyleRequest.Linguistic(SeededProject.RightToLeftTag, "Paragraph")));
            Assert.DoesNotContain("Andika", control.FontFamily!.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(18 * 96d / 72d,
                resolver.Resolve(TextStyleRequest.Linguistic(SeededProject.RightToLeftTag, "Unlisted")).FontSize);
            Assert.Equal(10 * 96d / 72d, resolver.Resolve(TextStyleRequest.Linguistic(null)).FontSize);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void UsesTheBundledFontCollectionForWalkthroughWritingSystems()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            Walkthrough.WalkthroughFonts.Register();
            var family = Walkthrough.WalkthroughFonts.DejaVuSansFamily;
            var system = WritingSystemTestData.Arabic with
            {
                FontFamily = family,
                StyleFonts = new Dictionary<string, WritingSystemStyleFont>(StringComparer.Ordinal)
                {
                    ["Normal"] = new(family, ""),
                },
            };
            var resolver = new TextStyles();
            resolver.ReplaceContext([system]);
            var style = resolver.Resolve(TextStyleRequest.Linguistic(system.Id));

            var requestedFamily = new FontFamily(family);
            var matched = FontManager.Current.TryGetGlyphTypeface(new Typeface(requestedFamily), out var glyphTypeface);
            Assert.True(style.RequestedFontInstalled,
                $"Requested '{family}' resolved as '{requestedFamily.Name}'; glyph match={matched}, " +
                $"matched family='{glyphTypeface?.FamilyName ?? "none"}'.");
            Assert.Equal("DejaVu Sans", new FontFamily(family).Name);
            Assert.Same(style, resolver.Resolve(TextStyleRequest.Linguistic(system.Id)));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void NamesTheShippedFallbackOncePerWritingSystemWhenTheRequestedFontIsMissing()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            Walkthrough.WalkthroughFonts.Register();
            var resolver = new TextStyles();
            resolver.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
            resolver.ReplaceContext([WritingSystemTestData.Arabic, WritingSystemTestData.Arabic with
            {
                Id = "ar-x-other",
                Name = "Other Arabic",
            }]);
            var resolved = resolver.Resolve(TextStyleRequest.Linguistic(SeededProject.RightToLeftTag, "Normal"));
            Assert.False(resolved.RequestedFontInstalled);
            Assert.Equal(SeededProject.RightToLeftTag, resolved.WritingSystemId);
            Assert.Equal(SeededProject.MissingFont, resolved.RequestedFontFamily);

            var first = new WritingSystemTextBlock { Text = WritingSystemTestData.Form };
            var second = new WritingSystemTextBlock { Text = WritingSystemTestData.Form };
            var third = new WritingSystemTextBlock { Text = WritingSystemTestData.Form };
            var window = new Window { Content = new StackPanel { Children = { first, second, third } } };
            SIL.Motif.App.Controls.WritingSystemText.SetResolver(window, resolver);
            foreach (var text in new[] { first, second })
            {
                SIL.Motif.App.Controls.WritingSystemText.SetId(text, SeededProject.RightToLeftTag);
                SIL.Motif.App.Controls.WritingSystemText.SetStyleName(text, "Normal");
            }
            SIL.Motif.App.Controls.WritingSystemText.SetId(third, "ar-x-other");
            SIL.Motif.App.Controls.WritingSystemText.SetStyleName(third, "Normal");
            Assert.Empty(resolver.MissingFontNotices);
            window.Show();
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            RenderFrame(window);

            Assert.Equal(WritingSystemTestData.Form, first.Text);
            Assert.Equal([SeededProject.RightToLeftTag, "ar-x-other"],
                resolver.MissingFontNotices.Select(notice => notice.WritingSystemId));
            var notice = resolver.MissingFontNotices[0];
            Assert.Contains(SeededProject.MissingFont, notice.Message, StringComparison.Ordinal);
            Assert.Contains("isn't installed on this computer", notice.Message, StringComparison.Ordinal);
            Assert.Contains("Motif is using DejaVu Sans.", notice.Message, StringComparison.Ordinal);
            window.Close();
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void WaitsUntilLanguageTextIsVisibleBeforeShowingTheMissingFontNotice()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var resolver = new TextStyles();
            resolver.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
            resolver.ReplaceContext([WritingSystemTestData.Arabic]);
            var text = new WritingSystemTextBlock { Text = WritingSystemTestData.Form };
            var page = new StackPanel { IsVisible = false, Children = { text } };
            var window = new Window { Content = page };
            SIL.Motif.App.Controls.WritingSystemText.SetResolver(window, resolver);
            SIL.Motif.App.Controls.WritingSystemText.SetId(text, SeededProject.RightToLeftTag);
            SIL.Motif.App.Controls.WritingSystemText.SetStyleName(text, "Normal");
            window.Show();
            window.UpdateLayout();
            Assert.Empty(resolver.MissingFontNotices);

            page.IsVisible = true;
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            RenderFrame(window);
            Assert.Single(resolver.MissingFontNotices);
            window.Close();
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void WaitsForVisibleLanguageTextToBecomeNonemptyBeforeShowingTheMissingFontNotice()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            Walkthrough.WalkthroughFonts.Register();
            var styles = new TextStyles();
            styles.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
            styles.ReplaceContext([WritingSystemTestData.Arabic]);
            var text = new WritingSystemTextBlock { Text = string.Empty };
            var window = new Window { Content = text };
            SIL.Motif.App.Controls.WritingSystemText.SetResolver(window, styles);
            SIL.Motif.App.Controls.WritingSystemText.SetId(text, SeededProject.RightToLeftTag);
            SIL.Motif.App.Controls.WritingSystemText.SetStyleName(text, "Normal");
            window.Show();
            window.UpdateLayout();
            Assert.Empty(styles.MissingFontNotices);

            text.Text = WritingSystemTestData.Form;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            RenderFrame(window);
            Assert.Single(styles.MissingFontNotices);
            window.Close();
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void WaitsForOffscreenLanguageTextToEnterItsScrollViewport()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var styles = new TextStyles();
            styles.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
            styles.ReplaceContext([WritingSystemTestData.Arabic]);
            var text = new WritingSystemTextBlock { Text = WritingSystemTestData.Form };
            var content = new StackPanel
            {
                Children =
                {
                    new Border { Height = 100 },
                    text,
                },
            };
            var viewport = new ScrollViewer { Content = content, Height = 24, ClipToBounds = true };
            var window = new Window { Content = viewport, Width = 220, Height = 24 };
            SIL.Motif.App.Controls.WritingSystemText.SetResolver(window, styles);
            SIL.Motif.App.Controls.WritingSystemText.SetId(text, SeededProject.RightToLeftTag);
            SIL.Motif.App.Controls.WritingSystemText.SetStyleName(text, "Normal");
            try
            {
                window.Show();
                window.UpdateLayout();
                RenderFrame(window);
                Assert.True(text.IsEffectivelyVisible);
                Assert.NotNull(styles.GetLineMetrics(text));
                Assert.False(IsInVisibleViewport(text, styles));
                Assert.Empty(styles.MissingFontNotices);

                viewport.Offset = new Vector(0, 100);
                RenderFrame(window);

                Assert.True(IsInVisibleViewport(text, styles));
                Assert.Single(styles.MissingFontNotices);
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void TextInputPublishesMissingFontNoticeAfterItsVisualTreeRenders()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var styles = new TextStyles();
            styles.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
            styles.ReplaceContext([WritingSystemTestData.Arabic]);
            var text = new WritingSystemTextBox { Text = WritingSystemTestData.Form };
            var window = new Window { Content = text, Width = 320, Height = 80 };
            SIL.Motif.App.Controls.WritingSystemText.SetResolver(window, styles);
            SIL.Motif.App.Controls.WritingSystemText.SetId(text, SeededProject.RightToLeftTag);
            SIL.Motif.App.Controls.WritingSystemText.SetStyleName(text, "Normal");
            try
            {
                Assert.Empty(styles.MissingFontNotices);
                window.Show();
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                RenderFrame(window);

                Assert.True(text.RenderPassCount > 0);
                Assert.NotNull(styles.GetLineMetrics(text));
                Assert.Single(styles.MissingFontNotices);
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void DeferredEditorPresentationRejectsChangedTextBeforeReplacementDraw()
    {
        AssertDeferredEditorPresentationRejectsReplacementBeforeDraw((_, text, _) => text.Text = "replacement form",
            SeededProject.RightToLeftTag);
    }

    [Fact]
    public void DeferredEditorPresentationRejectsChangedIdBeforeReplacementDraw()
    {
        AssertDeferredEditorPresentationRejectsReplacementBeforeDraw((_, text, _) =>
            SIL.Motif.App.Controls.WritingSystemText.SetId(text, "ar-x-other"), "ar-x-other");
    }

    [Fact]
    public void DeferredEditorPresentationRejectsChangedRevisionBeforeReplacementDraw()
    {
        AssertDeferredEditorPresentationRejectsReplacementBeforeDraw((styles, _, _) =>
            styles.ReplaceContext([WritingSystemTestData.Arabic with
            {
                Name = "Revised Arabic",
            }]), SeededProject.RightToLeftTag);
    }

    [Fact]
    public void DeferredEditorPresentationRejectsDetachAndReattachBeforeReplacementDraw()
    {
        AssertDeferredEditorPresentationRejectsReplacementBeforeDraw((_, text, content) =>
        {
            content.Children.Remove(text);
            content.Children.Add(text);
        }, SeededProject.RightToLeftTag);
    }

    private static void AssertDeferredEditorPresentationRejectsReplacementBeforeDraw(
        Action<TextStyles, WritingSystemTextBox, StackPanel> changeContext, string expectedId)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var styles = new TextStyles();
            styles.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
            styles.ReplaceContext([WritingSystemTestData.Arabic, WritingSystemTestData.Arabic with
            {
                Id = "ar-x-other",
                Name = "Other Arabic",
            }]);
            var text = new WritingSystemTextBox { Text = WritingSystemTestData.Form };
            var content = new StackPanel { Children = { text } };
            var window = new Window { Content = content, Width = 320, Height = 80 };
            var deferredReports = new List<Action>();
            var deferredPublications = new List<Action>();
            text.PresentationReportScheduler = deferredReports.Add;
            styles.PresentationCheckScheduler = deferredPublications.Add;
            SIL.Motif.App.Controls.WritingSystemText.SetResolver(window, styles);
            SIL.Motif.App.Controls.WritingSystemText.SetId(text, SeededProject.RightToLeftTag);
            SIL.Motif.App.Controls.WritingSystemText.SetStyleName(text, "Normal");
            try
            {
                window.Show();
                Assert.Empty(styles.MissingFontNotices);
                RenderFrame(window);
                Assert.True(text.RenderPassCount > 0);
                Assert.NotNull(text.LastPresentationTicket);
                Assert.NotEmpty(deferredReports);
                Assert.Empty(styles.MissingFontNotices);
                var renderedPassCount = text.RenderPassCount;

                changeContext(styles, text, content);
                foreach (var report in deferredReports) report();
                deferredReports.Clear();
                while (deferredPublications.Count > 0)
                {
                    var publication = deferredPublications[0];
                    deferredPublications.RemoveAt(0);
                    publication();
                }

                Assert.Empty(styles.MissingFontNotices);
                Assert.Equal(renderedPassCount, text.RenderPassCount);

                text.PresentationReportScheduler = null;
                styles.PresentationCheckScheduler = null;
                text.InvalidateVisual();
                RenderFrame(window);
                Assert.Equal(expectedId, Assert.Single(styles.MissingFontNotices).WritingSystemId);
                Assert.True(text.RenderPassCount > renderedPassCount);
            }
            finally
            {
                text.PresentationReportScheduler = null;
                styles.PresentationCheckScheduler = null;
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void InitiallyEmptyTextInputWaitsForItsFirstNonemptyTextDraw()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var styles = new TextStyles();
            styles.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
            styles.ReplaceContext([WritingSystemTestData.Arabic]);
            var text = new WritingSystemTextBox { Text = string.Empty };
            var window = new Window { Content = text, Width = 320, Height = 80 };
            SIL.Motif.App.Controls.WritingSystemText.SetResolver(window, styles);
            SIL.Motif.App.Controls.WritingSystemText.SetId(text, SeededProject.RightToLeftTag);
            SIL.Motif.App.Controls.WritingSystemText.SetStyleName(text, "Normal");
            try
            {
                window.Show();
                RenderFrame(window);
                Assert.True(text.RenderPassCount > 0);
                Assert.Empty(styles.MissingFontNotices);

                text.Text = WritingSystemTestData.Form;
                Assert.Empty(styles.MissingFontNotices);

                RenderFrame(window);
                Assert.Single(styles.MissingFontNotices);
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void DeferredFontNoticesIgnoreRecycledAndDetachedText()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var styles = new TextStyles();
            styles.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
            styles.ReplaceContext([WritingSystemTestData.Arabic, WritingSystemTestData.Arabic with
            {
                Id = "ar-x-other",
                Name = "Other Arabic",
            }]);
            var text = new WritingSystemTextBlock { Text = WritingSystemTestData.Form, IsVisible = false };
            var content = new StackPanel { Children = { text } };
            var window = new Window { Content = content };
            SIL.Motif.App.Controls.WritingSystemText.SetResolver(window, styles);
            SIL.Motif.App.Controls.WritingSystemText.SetId(text, SeededProject.RightToLeftTag);
            SIL.Motif.App.Controls.WritingSystemText.SetStyleName(text, "Normal");
            window.Show();
            window.UpdateLayout();

            text.IsVisible = true;
            SIL.Motif.App.Controls.WritingSystemText.SetId(text, "ar-x-other");
            SIL.Motif.App.Controls.WritingSystemText.ReportTextPresented(text);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("ar-x-other", Assert.Single(styles.MissingFontNotices).WritingSystemId);

            styles.ReplaceContext([WritingSystemTestData.Arabic, WritingSystemTestData.Arabic with
            {
                Id = "ar-x-other",
                Name = "Other Arabic",
            }]);
            SIL.Motif.App.Controls.WritingSystemText.ReportTextPresented(text);
            content.Children.Remove(text);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Empty(styles.MissingFontNotices);
            window.Close();
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    private static bool IsInVisibleViewport(TextBlock text, TextStyles styles) =>
        styles.GetLineMetrics(text) is { } metrics &&
        SIL.Motif.App.Controls.WritingSystemText.IsInVisibleViewport(text, metrics.InkBounds);

    private static void RenderFrame(Window window)
    {
        window.UpdateLayout();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void AppliesContextRevisionsToAttachedTextAndWhenRecycledTextReturns()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            Walkthrough.WalkthroughFonts.Register();
            var styles = new TextStyles();
            styles.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
            styles.ReplaceContext([WritingSystemTestData.Arabic with
            {
                StyleSizes = new Dictionary<string, double>(StringComparer.Ordinal) { ["Normal"] = 12 },
            }]);
            var text = new WritingSystemTextBlock { Text = WritingSystemTestData.Form };
            var panel = new StackPanel { Children = { text } };
            var window = new Window { Content = panel };
            SIL.Motif.App.Controls.WritingSystemText.SetResolver(window, styles);
            SIL.Motif.App.Controls.WritingSystemText.SetId(text, SeededProject.RightToLeftTag);
            SIL.Motif.App.Controls.WritingSystemText.SetStyleName(text, "Normal");
            window.Show();
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(12 * 96d / 72d, text.FontSize);
            RenderFrame(window);
            Assert.Single(styles.MissingFontNotices);

            styles.ReplaceContext([WritingSystemTestData.Arabic with
            {
                StyleSizes = new Dictionary<string, double>(StringComparer.Ordinal) { ["Normal"] = 18 },
            }]);

            Assert.Equal(18 * 96d / 72d, text.FontSize);
            window.UpdateLayout();
            RenderFrame(window);
            Assert.Single(styles.MissingFontNotices);

            panel.Children.Remove(text);
            styles.ReplaceContext([WritingSystemTestData.Arabic with
            {
                StyleSizes = new Dictionary<string, double>(StringComparer.Ordinal) { ["Normal"] = 20 },
            }]);
            Assert.Empty(styles.MissingFontNotices);

            panel.Children.Add(text);
            window.UpdateLayout();
            RenderFrame(window);
            Assert.Equal(20 * 96d / 72d, text.FontSize);
            Assert.Single(styles.MissingFontNotices);
            window.Close();
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void InterfaceDirectionFollowsItsLocaleAndContextReplacementAdvancesTheRevision()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var styles = new TextStyles();
            styles.ReplaceContext([WritingSystemTestData.Arabic], "en");
            var firstRevision = styles.Revision;

            styles.ReplaceContext([], "ar");
            var resolved = styles.Resolve(TextStyleRequest.Interface());

            Assert.Equal(firstRevision + 1, styles.Revision);
            Assert.Equal(FlowDirection.RightToLeft, resolved.FlowDirection);
            Assert.Equal(TextStyles.InterfaceFontFamily, resolved.FontFamily);
            Assert.Equal(Assert.IsType<double>(Application.Current!.FindResource("Intent.Type.Body")), resolved.FontSize);
            Assert.Equal(["kern", "liga", "clig", "calt"],
                resolved.FontFeatures!.Select(feature => feature.Tag.ToString()));
            Assert.Equal(styles.Revision, resolved.Revision);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void InterfaceTypographyUsesTheBundledAndikaLayoutAndScriptFallback()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var styles = new TextStyles();
            styles.ReplaceContext([], "en");
            var request = TextStyleRequest.Interface("ar-EG");
            var style = styles.Resolve(request);
            Assert.Equal("Scheherazade New", Assert.Single(style.FontFallbacks));
            Assert.Equal(TextStyles.InterfaceFontFamily, style.FontFamily);

            var text = new TextBlock { Text = "Motif uses Andika." };
            styles.Apply(text, TextStyleRequest.Interface("en"));
            var window = new Window { Content = text, Width = 320, Height = 80 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var resolved = styles.Resolve(TextStyleRequest.Interface("en"));
                Assert.Equal(TextStyles.InterfaceFontFamily, text.FontFamily);
                Assert.Equal(resolved.FontSize, text.FontSize);
                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(text.FontFamily!), out var face));
                Assert.StartsWith("Andika", face!.FamilyName, StringComparison.OrdinalIgnoreCase);
                var metrics = styles.GetLineMetrics(text);
                Assert.NotNull(metrics);
                Assert.True(metrics.Value.Ascent > 0);
                Assert.True(metrics.Value.Descent > 0);
                var runs = text.TextLayout.TextLines.SelectMany(line => line.TextRuns)
                    .OfType<ShapedTextRun>().ToArray();
                Assert.NotEmpty(runs);
                Assert.All(runs, run => Assert.StartsWith("Andika", run.GlyphRun.GlyphTypeface.FamilyName,
                    StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void InterfaceAndProjectDirectionsFollowTheirOwnLanguages()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var styles = new TextStyles();
            var english = WritingSystemTestData.English;
            styles.ReplaceContext([english, WritingSystemTestData.Arabic], "en");

            Assert.Equal(FlowDirection.LeftToRight, styles.Resolve(TextStyleRequest.Interface()).FlowDirection);
            Assert.Equal(FlowDirection.LeftToRight,
                styles.Resolve(TextStyleRequest.Linguistic(english.Id)).FlowDirection);
            Assert.Equal(FlowDirection.RightToLeft,
                styles.Resolve(TextStyleRequest.Linguistic(WritingSystemTestData.Arabic.Id)).FlowDirection);

            Assert.Equal(FlowDirection.RightToLeft,
                styles.Resolve(TextStyleRequest.Interface("ar-EG")).FlowDirection);
            Assert.Equal(FlowDirection.LeftToRight,
                styles.Resolve(TextStyleRequest.Linguistic(english.Id)).FlowDirection);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void LineMetricsComeFromTheSelectableTextControlsRenderedLayout()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            Walkthrough.WalkthroughFonts.Register();
            var styles = new TextStyles();
            styles.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
            styles.ReplaceContext([WritingSystemTestData.Arabic]);
            var text = new CopyableTextBlock { Text = WritingSystemTestData.Form };
            styles.Apply(text, TextStyleRequest.Linguistic(SeededProject.RightToLeftTag, "Normal"));
            var window = new Window { Content = text };
            window.Show();
            window.UpdateLayout();

            var renderedLayout = text is TextBlock textBlock
                ? textBlock.TextLayout
                : Assert.Single(text.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>()).TextLayout;
            var metrics = styles.GetLineMetrics(text) ??
                throw new InvalidOperationException("No rendered line metrics were available.");

            Assert.True(metrics.Ascent > 0);
            Assert.True(metrics.Descent > 0);
            Assert.True(metrics.InkBounds.Height > 0);
            Assert.True(metrics.RequiredLineBox >= renderedLayout.Height);
            Assert.Contains(renderedLayout.TextLines, line => line.Extent > 0);
            window.Close();
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }
}

internal static class WritingSystemTestData
{
    public const string Form = "مَدْرَسَة";
    public const string TextTitle = "حِكَايَة الأَرْنَب";
    private static readonly Guid WordformGuid = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static string WordformId => CanonicalId.FromGuid(WordformGuid).Value;

    public static WritingSystemDisplay Arabic { get; } = new(
        SeededProject.RightToLeftTag, "Arabic", "Ar", WritingSystemKind.Vernacular, 1, false,
        SeededProject.MissingFont, SeededProject.FontFeatures, true,
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Normal"] = SeededProject.RightToLeftSizePoints,
            ["Paragraph"] = 20,
        })
    {
        StyleFonts = new Dictionary<string, WritingSystemStyleFont>(StringComparer.Ordinal)
        {
            ["Normal"] = new(SeededProject.MissingFont, SeededProject.FontFeatures + ",1051=1"),
            ["Paragraph"] = new(SeededProject.MissingFont, SeededProject.FontFeatures + ",1051=1"),
        },
    };

    public static WritingSystemDisplay English { get; } = new(
        "en", "English", "En", WritingSystemKind.Vernacular, 0, true,
        "Andika", string.Empty, false,
        new Dictionary<string, double>(StringComparer.Ordinal) { ["Normal"] = 10 });

    public static TextWordsResponse ArabicText()
    {
        var textId = Guid.Parse("11111111-0000-0000-0000-000000000001");
        var wordformId = WordformId;
        var occurrence = new WordOccurrence(textId, TextTitle, 1, Form, "unanalysed", null)
        {
            SentenceStyle = "Paragraph",
            TextTitleWritingSystem = SeededProject.RightToLeftTag,
            SentenceWritingSystem = SeededProject.RightToLeftTag,
        };
        var word = new TextWord(Form, WordformGuid.ToString("D"), [occurrence], [], [])
        {
            FormWritingSystem = SeededProject.RightToLeftTag,
        };
        var token = new TextToken(Form, Form, null, "unanalysed")
        {
            TextWritingSystem = SeededProject.RightToLeftTag,
            FormWritingSystem = SeededProject.RightToLeftTag,
            WordformId = WordformGuid,
        };
        var line = new TextLine(1, [token])
        {
            SentenceStyle = "Paragraph",
            SentenceWritingSystem = SeededProject.RightToLeftTag,
        };
        var text = new TextLines(textId, TextTitle, [line])
        {
            TitleWritingSystem = SeededProject.RightToLeftTag,
        };
        return new TextWordsResponse([word], [text], HasBaseline: true, OccurrenceCount: 1);
    }

    public static PendingChangesSnapshot ArabicPendingChange()
    {
        var reading = new ParserReading(
        [
            new ParserReadingMorph(Form, "school", "n", null, false, null)
            {
                FormWritingSystem = SeededProject.RightToLeftTag,
            },
        ]);
        var change = new PendingChange("rtl-change", WordformId, Form,
            ChangeKinds.Approve, null, Form, ["operation/rtl-change"])
        {
            WordWritingSystem = SeededProject.RightToLeftTag,
            Analyses = [new ReviewAnalysis(reading, ReadingGrade.NoOpinion, true, false)],
        };
        return new PendingChangesSnapshot("draft/rtl", "revision/rtl", [change],
            [new ChangeFit(change.ChangeId, true, [])]);
    }
}
