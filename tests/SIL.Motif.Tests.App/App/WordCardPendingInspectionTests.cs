using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WritingSystemText = SIL.Motif.App.Controls.WritingSystemText;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WordCardPendingInspectionTests
{
    [Fact]
    public void PendingMorphsInspectTheirOwnIdentityAndKeepFormsAndGlossesCopyable()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var client = new FakeCommandClient();
            var selection = new SelectionViewModel(client);
            var context = new WorkspaceContext(selection, new AssessViewModel(client, selection),
                new ChangesViewModel(client), client, new NoFolderPicker(), new NoDragSource(),
                new BaselineViewModel(client));
            var named = new ParserReadingMorphViewModel(new ParserReadingMorph("wa-", "3PL", "prefix", null, false, null)
            {
                AllomorphId = "11111111-1111-1111-1111-111111111111",
                GrammaticalInfoId = "22222222-2222-2222-2222-222222222222",
                FormWritingSystem = "tr", GlossWritingSystem = "en",
            });
            var unnamed = new ParserReadingMorphViewModel(new ParserReadingMorph("guess", "", "", null, true, null)
            {
                FormWritingSystem = "tr", GlossWritingSystem = "en",
            });
            var card = new WordCard
            {
                Document = new WordCardDocument(new WordPresentationKey("pending:inspection"), 0,
                    [new WordCardPendingChange("wa-guess", true, true, true, [named, unnamed], false, [], [], false, context)]),
            };
            var window = new MotifTestWindow { Content = card, Width = 640, Height = 600 };
            InspectRequestedEventArgs? requested = null;
            var requests = 0;
            window.AddHandler(InspectLink.RequestedEvent, (_, args) => { requested = args; requests++; });
            try
            {
                window.Show();
                foreach (var (width, height) in new[] { (640, 600), (960, 780), (1920, 1080), (960, 600) })
                {
                    window.Width = width;
                    window.Height = height;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is { Length: > 0 } folder)
                    {
                        Directory.CreateDirectory(folder);
                        using var frame = window.CaptureRenderedFrame()!;
                        frame.Save(Path.Combine(folder, $"pending-inspection-{width}x{height}.png"), PngBitmapEncoderOptions.Default);
                    }
                }
                var rows = Assert.Single(card.GetVisualDescendants().OfType<ProgressiveItemsControl>(),
                    item => item.Name == "WordCardPendingMorphsItems");
                Assert.Equal(2, rows.Items.Count);
                foreach (var morph in new[] { named, unnamed })
                {
                    var texts = card.GetVisualDescendants().OfType<CopyableTextBlock>()
                        .Where(text => ReferenceEquals(text.DataContext, morph) && text.IsEffectivelyVisible).ToArray();
                    var form = Assert.Single(texts, text => text.Text == morph.Form);
                    var gloss = Assert.Single(texts, text => text.Text == morph.GlossOrPlaceholder);
                    Assert.Equal("tr", WritingSystemText.GetId(form));
                    Assert.Equal("en", WritingSystemText.GetId(gloss));
                    Assert.Equal("Normal", WritingSystemText.GetStyleName(form));
                    Assert.Equal("Normal", WritingSystemText.GetStyleName(gloss));
                    foreach (var text in new[] { form, gloss })
                    {
                        Assert.DoesNotContain(text.GetVisualAncestors(), ancestor => ancestor is Button);
                        text.SelectAll();
                        Assert.True(text.CanCopy);
                        Assert.Equal(text.Text, text.SelectedText);
                    }
                }
                Assert.Null(unnamed.InspectSubject);
                var link = Assert.Single(card.GetVisualDescendants().OfType<InspectLink>(), item => item.IsEffectivelyVisible);
                Assert.Same(named, link.DataContext);
                Assert.Equal(named.InspectSubject, link.Subject);
                Assert.Equal("Inspect wa-", AutomationProperties.GetName(link));
                HeadlessClick.Click(window, link, "Inspect wa-");
                Assert.Equal(1, requests);
                Assert.Equal(named.InspectSubject, requested!.Subject);
                Assert.Equal(named.Captured, requested.Captured);
                Assert.Same(link, requested.Origin);
                link.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(2, requests);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ManyMorphemesWrapInsideTheCardAndTheNotParsedSentenceShowsOnce()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var client = new FakeCommandClient();
            var selection = new SelectionViewModel(client);
            var context = new WorkspaceContext(selection, new AssessViewModel(client, selection),
                new ChangesViewModel(client), client, new NoFolderPicker(), new NoDragSource(),
                new BaselineViewModel(client));
            var morphs = new[] { "u-", "me-", "ja", "-a" }.Select((form, index) =>
                new ParserReadingMorphViewModel(new ParserReadingMorph(form, "SM.2SG/3/11/14 PRF", "prefix", null, false, null)
                {
                    AllomorphId = $"{index + 1}1111111-1111-1111-1111-111111111111",
                    GrammaticalInfoId = $"{index + 1}2222222-2222-2222-2222-222222222222",
                })).ToArray();
            var notParsed = "No parse of umejaa is on screen yet. Parse all words to see what PanGloss builds.";
            var card = new WordCard
            {
                Document = new WordCardDocument(new WordPresentationKey("pending:wrap"), 0,
                [
                    new WordCardPendingChange("umejaa", true, true, true, morphs, false, [], [], false, context),
                    new WordCardAnalysis(null, null, notParsed),
                ]),
            };
            var window = new MotifTestWindow { Content = card, Width = 320, Height = 600 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Single(card.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == notParsed && text.IsEffectivelyVisible);
                var cardBounds = new Rect(card.TranslatePoint(default, window)!.Value, card.Bounds.Size);
                foreach (var morph in morphs)
                {
                    var own = card.GetVisualDescendants().OfType<CopyableTextBlock>()
                        .Where(text => ReferenceEquals(text.DataContext, morph)).ToArray();
                    var form = Assert.Single(own, text => text.Text == morph.Form);
                    var gloss = Assert.Single(own, text => text.Text == morph.GlossOrPlaceholder);
                    var link = Assert.Single(card.GetVisualDescendants().OfType<InspectLink>(), item =>
                        ReferenceEquals(item.DataContext, morph));
                    Assert.True(link.IsEffectivelyVisible);
                    var column = form.GetVisualParent()!;
                    Assert.Same(column, gloss.GetVisualParent());
                    Assert.Same(column, link.GetVisualParent());
                    var columnBounds = new Rect(((Visual)column).TranslatePoint(default, window)!.Value,
                        ((Visual)column).Bounds.Size);
                    foreach (Control part in new Control[] { form, gloss, link })
                    {
                        var rect = new Rect(part.TranslatePoint(default, window)!.Value, part.Bounds.Size);
                        Assert.True(cardBounds.Contains(rect.TopLeft) && cardBounds.Contains(rect.BottomRight),
                            $"{morph.Form} part {part.GetType().Name} {rect} is outside the card {cardBounds}.");
                        Assert.True(columnBounds.Inflate(0.5).Contains(rect.TopLeft)
                            && columnBounds.Inflate(0.5).Contains(rect.BottomRight),
                            $"{morph.Form} part {part.GetType().Name} {rect} is outside its column {columnBounds}.");
                    }
                    Assert.DoesNotContain(gloss.TextLayout.TextLines, line => line.HasCollapsed);
                }
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) => Task.FromResult(DragDropEffects.None);
    }
}
