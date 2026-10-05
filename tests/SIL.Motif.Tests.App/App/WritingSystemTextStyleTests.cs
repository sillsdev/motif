using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WritingSystemTextStyleTests
{
    [Fact]
    public void ResolvesStyleFontFeaturesDirectionAndPointSizeWithoutTheInterfaceFont()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var resolver = new WritingSystemTextStyleResolver();
            resolver.SetWritingSystems([WritingSystemTestData.Arabic]);

            var control = new TextBlock { Text = WritingSystemTestData.Form };
            resolver.Apply(control, SeededProject.RightToLeftTag, "Paragraph");

            var resolved = resolver.Resolve(SeededProject.RightToLeftTag, "Paragraph");
            Assert.Equal(20 * 96d / 72d, control.FontSize);
            Assert.Equal(FlowDirection.RightToLeft, control.FlowDirection);
            Assert.Equal(2, control.FontFeatures!.Count);
            Assert.Equal(["smcp", "cv01"], control.FontFeatures.Select(feature => feature.Tag.ToString()));
            Assert.False(resolved.RequestedFontInstalled);
            Assert.Same(resolved, resolver.Resolve(SeededProject.RightToLeftTag, "Paragraph"));
            Assert.DoesNotContain("Andika", control.FontFamily!.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(18 * 96d / 72d, resolver.Resolve(SeededProject.RightToLeftTag, "Unlisted").FontSize);
            Assert.Equal(10 * 96d / 72d, resolver.Resolve(null).FontSize);
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
            var resolver = new WritingSystemTextStyleResolver();
            resolver.SetWritingSystems([system]);
            var style = resolver.Resolve(system.Id);

            Assert.True(style.RequestedFontInstalled);
            Assert.Equal("DejaVu Sans", new FontFamily(family).Name);
            Assert.Same(style, resolver.Resolve(system.Id));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void NamesTheShippedFallbackOncePerWritingSystemWhenTheRequestedFontIsMissing()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            Walkthrough.WalkthroughFonts.Register();
            var resolver = new WritingSystemTextStyleResolver();
            resolver.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
            resolver.SetWritingSystems([WritingSystemTestData.Arabic, WritingSystemTestData.Arabic with
            {
                Id = "ar-x-other",
                Name = "Other Arabic",
            }]);
            var resolved = resolver.Resolve(SeededProject.RightToLeftTag, "Normal");
            Assert.False(resolved.RequestedFontInstalled);
            Assert.Equal(SeededProject.RightToLeftTag, resolved.WritingSystemId);
            Assert.Equal(SeededProject.MissingFont, resolved.RequestedFontFamily);

            var first = new TextBlock { Text = WritingSystemTestData.Form };
            var second = new TextBlock { Text = WritingSystemTestData.Form };
            var third = new TextBlock { Text = WritingSystemTestData.Form };
            var window = new Window { Content = new StackPanel { Children = { first, second, third } } };
            window.Show();
            resolver.Apply(first, SeededProject.RightToLeftTag, "Normal");
            Assert.Equal(WritingSystemTestData.Form, first.Text);
            Assert.True(resolver.MissingFontNotices.Count == 1,
                $"No notice after applying {first.Text} for {resolved.WritingSystemId} with {resolved.RequestedFontFamily}.");
            resolver.Apply(second, SeededProject.RightToLeftTag, "Paragraph");
            resolver.Apply(third, "ar-x-other", "Normal");

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
            var resolver = new WritingSystemTextStyleResolver();
            resolver.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
            resolver.SetWritingSystems([WritingSystemTestData.Arabic]);
            var text = new TextBlock { Text = WritingSystemTestData.Form };
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
            Assert.Single(resolver.MissingFontNotices);
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
