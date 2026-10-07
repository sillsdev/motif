using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;
using WritingSystemText = SIL.Motif.App.Controls.WritingSystemText;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MorphemePanelTests(AvaloniaHeadlessFixture avalonia)
{
    private const string EntryLink = "silfw://localhost/link?database%3dp%26tool%3dlexiconEdit%26guid%3dx%26tag%3d";

    [Fact]
    public void PartsKeepSelectableTextWritingSystemsLinksAndInspectorBehavior()
    {
        avalonia.Invoke(() =>
        {
            var source = new ParserReadingMorph("كِتَاب", "book", "n", null, false, EntryLink)
            {
                FormWritingSystem = "ar",
                GlossWritingSystem = "en",
                CategoryWritingSystem = "qaa-x-category",
                Entry = "كتاب",
                AllomorphId = "form-kitab",
                GrammaticalInfoId = "msa-kitab",
            };
            var morph = new ParserReadingMorphViewModel(source);
            var panel = new MorphemePanel
            {
                Morphs = [morph],
                RevealLinks = true,
                CompactLinks = true,
                Separators = true,
            };
            panel.Measure(Size.Infinity);
            var parts = Assert.IsType<WrapPanel>(panel.Child);
            var part = Assert.Single(parts.Children.OfType<StackPanel>());
            Assert.Same(morph, part.Tag);
            Assert.True(part.Focusable);
            Assert.Equal("Inspect كِتَاب", AutomationProperties.GetName(part));
            Assert.Equal("Show كِتَاب in the inspector", ToolTip.GetTip(part));

            var form = Assert.IsType<CopyableTextBlock>(part.Children[0]);
            var gloss = Assert.IsType<CopyableTextBlock>(part.Children[1]);
            var category = Assert.IsType<CopyableTextBlock>(part.Children[2]);
            Assert.Equal("ar", WritingSystemText.GetId(form));
            Assert.Equal("en", WritingSystemText.GetId(gloss));
            Assert.Equal("qaa-x-category", WritingSystemText.GetId(category));
            Assert.Equal("Normal", WritingSystemText.GetStyleName(form));
            Assert.Equal("Normal", WritingSystemText.GetStyleName(gloss));
            Assert.Equal("Normal", WritingSystemText.GetStyleName(category));

            var link = Assert.IsType<HyperlinkButton>(part.Children[3]);
            Assert.Equal(new Uri(EntryLink), link.NavigateUri);
            Assert.Equal("↗", link.Content);
            Assert.Equal("Open كتاب in Lexicon Edit", AutomationProperties.GetName(link));
            Assert.Equal("Open كتاب in Lexicon Edit", ToolTip.GetTip(link));
            Assert.Contains("revealControl", link.Classes);
            Assert.Contains("revealLink", link.Classes);
            Assert.Contains("revealOnHover", link.Classes);

            var window = new Window { Content = panel, Width = 500, Height = 200 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var requested = new List<InspectorSubject>();
                window.AddHandler(InspectLink.RequestedEvent, (_, e) => requested.Add(e.Subject));
                part.RaiseEvent(new KeyEventArgs
                    { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = part });

                Assert.Equal(morph.InspectSubject, Assert.Single(requested));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void NamedMarkKeepsItsGlyphAndWordsSelectableWithOneAccessibleName()
    {
        avalonia.Invoke(() =>
        {
            var mark = new NamedMark { Mark = Mark.Same, Text = "uses the same reading" };

            Assert.Equal("Same: uses the same reading", AutomationProperties.GetName(mark));
            Assert.Single(mark.Children.OfType<MarkGlyph>());
            var words = Assert.Single(mark.Children.OfType<CopyableTextBlock>());
            Assert.Equal("uses the same reading", words.Text);
            Assert.Contains("namedMarkText", words.Classes);
        });
    }

    [Fact]
    public void OverflowNamesTheProducerAndPreservesTheCompleteReading()
    {
        avalonia.Invoke(() =>
        {
            var morphs = Enumerable.Range(0, 40)
                .Select(index => new ParserReadingMorphViewModel(
                    new ParserReadingMorph($"m{index}", $"g{index}", "n", null, false, null)))
                .ToArray();
            var request = new MorphemeOverflowRequest("word-42", "assessment-17", 9, MorphologyProducer.PanGloss);
            var panel = new MorphemePanel
            {
                Morphs = morphs,
                MaxVisibleMorphs = 30,
                OverflowRequest = request,
            };
            var window = new Window { Content = panel, Width = 320, Height = 900 };
            var requested = new List<MorphemeOverflowRequestedEventArgs>();
            window.AddHandler(MorphemePanel.OverflowRequestedEvent, (_, e) => requested.Add(e));

            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.Same(morphs, panel.Morphs);
                var parts = Assert.IsType<WrapPanel>(panel.Child);
                Assert.Equal(morphs.Take(20).Cast<object?>().ToArray(),
                    parts.Children.OfType<StackPanel>().Select(part => part.Tag).Cast<object?>().ToArray());
                var firstFooter = OverflowButton(parts);
                Assert.Equal("Show 10 more", firstFooter.Content);
                Assert.Equal("Show all 40 PanGloss morphemes", AutomationProperties.GetName(firstFooter));
                Assert.Equal("Show all PanGloss morphemes", ToolTip.GetTip(firstFooter));

                var next = Assert.Single(parts.Children.OfType<Button>(), button => Equals(button.Tag, true));
                Assert.Equal("Show 10 more", next.Content);
                Click(window, next);
                Assert.Equal(morphs.Skip(20).Take(10).Cast<object?>().ToArray(),
                    parts.Children.OfType<StackPanel>().Select(part => part.Tag).Cast<object?>().ToArray());
                Assert.NotSame(firstFooter, OverflowButton(parts));
                Assert.Contains(parts.Children.OfType<Button>(), button => Equals(button.Tag, false) && button.Content?.ToString() == "Previous 20");

                var previous = Assert.Single(parts.Children.OfType<Button>(), button => Equals(button.Tag, false));
                Click(window, previous);
                Assert.Equal(morphs.Take(20).Cast<object?>().ToArray(),
                    parts.Children.OfType<StackPanel>().Select(part => part.Tag).Cast<object?>().ToArray());
                var footer = OverflowButton(parts);
                Click(window, footer);
                var requestEvent = Assert.Single(requested);
                Assert.Same(request, requestEvent.Request);
                Assert.Same(footer, requestEvent.Origin);
                Assert.Equal("word-42", requestEvent.Request.WordIdentity);
                Assert.Equal("assessment-17", requestEvent.Request.EvidenceIdentity);
                Assert.Equal(9, requestEvent.Request.EvidenceRevision);
                Assert.Equal(MorphologyProducer.PanGloss, requestEvent.Request.Producer);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Button OverflowButton(WrapPanel parts) =>
        Assert.Single(parts.Children.OfType<Button>(), button =>
            AutomationProperties.GetName(button) == "Show all 40 PanGloss morphemes");

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The control is not in the window.");
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    [Fact]
    public void InspectableMorphemesAcceptGapClicksAndKeyboardButIgnoreNestedLinks()
    {
        avalonia.Invoke(() =>
        {
            var source = new ParserReadingMorph("كِتَاب", "book", "n", null, false, EntryLink)
            {
                Entry = "كتاب",
                AllomorphId = "form-kitab",
                GrammaticalInfoId = "msa-kitab",
            };
            var morph = new ParserReadingMorphViewModel(source);
            var panel = new MorphemePanel { Morphs = [morph] };
            var window = new Window { Content = panel, Width = 320, Height = 120 };
            var requests = new List<InspectRequestedEventArgs>();
            window.AddHandler(InspectLink.RequestedEvent, (_, e) => requests.Add(e));
            try
            {
                window.Show();
                window.UpdateLayout();
                var part = Assert.Single(Assert.IsType<WrapPanel>(panel.Child).Children.OfType<StackPanel>());
                part.Width = 180;
                window.UpdateLayout();

                var gap = At(part, new Point(part.Bounds.Width - 2, part.Bounds.Height / 2), window);
                window.MouseMove(gap);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(part.IsPointerOver);
                Assert.True(PartBackgroundMatches("Intent.Surface.Hover", part));

                window.MouseDown(gap, MouseButton.Left);
                window.MouseUp(gap, MouseButton.Left);
                Assert.Equal(morph.InspectSubject, Assert.Single(requests).Subject);

                requests.Clear();
                Assert.True(part.Focus(NavigationMethod.Tab));
                part.RaiseEvent(new KeyEventArgs
                    { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = part });
                Assert.Equal(morph.InspectSubject, Assert.Single(requests).Subject);

                requests.Clear();
                var link = Assert.Single(part.Children.OfType<HyperlinkButton>());
                using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
                var releaseProperties = new PointerPointProperties(
                    RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
                link.RaiseEvent(new PointerReleasedEventArgs(
                    link, pointer, window, new Point(link.Bounds.Width / 2, link.Bounds.Height / 2),
                    1, releaseProperties, KeyModifiers.None, MouseButton.Left));
                Assert.Empty(requests);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Point At(Control control, Point localPoint, Window window) =>
        control.TranslatePoint(localPoint, window) ?? throw new InvalidOperationException("The morpheme is not in the window.");

    private static bool PartBackgroundMatches(string key, Control control)
    {
        Assert.True(Application.Current!.TryGetResource(key, control.ActualThemeVariant, out var expected));
        return expected is ISolidColorBrush expectedBrush &&
            control.GetValue(Panel.BackgroundProperty) is ISolidColorBrush actualBrush &&
            expectedBrush.Color == actualBrush.Color && expectedBrush.Opacity.Equals(actualBrush.Opacity);
    }

}
