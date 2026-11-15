using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Presses the real keys at the Passage over the sample project and checks what is staged and announced.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class PassageKeepKeysTests
{
    private static string? Status(Window window) =>
        window.FindInWindowOrWorkspace<TextBlock>("KeyboardShortcutStatusLine")?.Text;

    private static string Staged(int count) => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.OccurrencesStagedStatusFormat, count);

    private static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window, ResultsInTextViewModel InText,
        FakeCommandClient Fake)> OpenAsync()
    {
        var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
        var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
        inText.CloseTokenCard();
        await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
        return (workspace, window, inText, Assert.IsType<FakeCommandClient>(workspace.Context.Commands));
    }

    private static ResultsTokenViewModel Token(ResultsInTextViewModel inText, string form) =>
        AnalyzeTextsLayoutTests.RealizedLines(inText).SelectMany(line => line.Tokens).Single(token => token.Form == form);

    [Fact]
    public void EnterAtAGuessedWordWithOneAnalysisStagesExactlyOneApproveAndAnnouncesIt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, inText, fake) = await OpenAsync();
            try
            {
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var mwalimu = Token(inText, "mwalimu");
                var anchor = mwalimu.Occurrence!;
                Assert.Equal(PassageWordState.Guessed, inText.PassageStateOf(anchor));
                Assert.True(AnalyzeTextsLayoutTests.StripOf(panel, "mwalimu").Focus());

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.True(await Wait.ForAsync(() => Status(window) == Staged(1), Wait.Default,
                    () => PageScreenshots.Settle(window)),
                    $"status='{Status(window)}' changes={workspace.Context.Changes.Items.Count}");

                var approve = Assert.Single(workspace.Context.Changes.Items, change => change.Occurrence == anchor);
                Assert.Equal(ChangeKinds.Approve, approve.Kind);
                Assert.Single(fake.PendingPutRequests);
                Assert.False(string.IsNullOrEmpty(approve.StoredAnalysisId));
                Assert.Empty(fake.ApplyPendingRequests);
                // The word shows staged, and a second Enter does not stage it twice.
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                Assert.True(Token(inText, "mwalimu").IsPassageStaged);
                Assert.Equal(MotifAppStrings.OccurrencesPlaceStaged, Token(inText, "mwalimu").PassageStateLabel);
                Assert.True(AnalyzeTextsLayoutTests.StripOf(panel, "mwalimu").Focus());
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.True(await Wait.ForAsync(() => Status(window) == MotifAppStrings.OccurrencesAlreadyStagedStatus,
                    Wait.Default, () => PageScreenshots.Settle(window)), $"status='{Status(window)}'");
                Assert.Single(workspace.Context.Changes.Items, change => change.Occurrence == anchor);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void EnterAtAParserOnlyGuessStagesTheParserReadingAtThatPlace()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, inText, fake) = await OpenAsync();
            try
            {
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var chakula = Token(inText, "chakula");
                var anchor = chakula.Occurrence!;
                Assert.Equal(PassageWordState.NeedsAnalysis, inText.PassageStateOf(anchor));
                Assert.True(AnalyzeTextsLayoutTests.StripOf(panel, "chakula").Focus());

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.True(await Wait.ForAsync(() => !string.IsNullOrEmpty(Status(window)), Wait.Default,
                    () => PageScreenshots.Settle(window)), "Enter must announce something.");
                var announced = Status(window);
                Assert.Equal(Staged(1), announced);
                var approve = Assert.Single(workspace.Context.Changes.Items, change => change.Occurrence == anchor);
                Assert.Equal(ChangeKinds.Approve, approve.Kind);
                Assert.Null(approve.StoredAnalysisId);
                Assert.NotNull(Assert.Single(fake.PendingPutRequests).Change.Reading);
                Assert.True(Token(inText, "chakula").IsPassageStaged);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void EnterAtAParserOnlyGuessStagesTheExactReadingIdentityTheComposerNeeds()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, inText, fake) = await OpenAsync();
            try
            {
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var chakula = Token(inText, "chakula");
                var anchor = chakula.Occurrence!;
                // The only parser reading the word offers is the one a keep must carry, with its position.
                var parser = Assert.Single(chakula.Readings, reading => !reading.IsDisapproved && reading.Analysis is not null);
                var cardTarget = chakula.CaptureActionTarget(null);
                var expectedContext = cardTarget.ExpectedContext;
                var assessmentId = cardTarget.AssessmentId;
                Assert.NotNull(expectedContext);
                Assert.False(string.IsNullOrEmpty(assessmentId));
                Assert.True(AnalyzeTextsLayoutTests.StripOf(panel, "chakula").Focus());

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.True(await Wait.ForAsync(() => Status(window) == Staged(1), Wait.Default,
                    () => PageScreenshots.Settle(window)), $"status='{Status(window)}'");

                var put = Assert.Single(fake.PendingPutRequests);
                Assert.Equal(ChangeKinds.Approve, put.Change.Kind);
                Assert.Equal(parser.Analysis, put.Change.Reading);
                Assert.Equal(parser.Index, put.Change.ReadingIndex);
                Assert.Equal(assessmentId, put.Change.AssessmentId);
                Assert.Equal(anchor, put.Change.Occurrence);
                Assert.Null(put.Change.StoredAnalysisId);
                Assert.Equal(expectedContext, put.ExpectedContext);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TabMovesToTheNextGapAndSkipsApprovedAndStagedWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, inText, fake) = await OpenAsync();
            try
            {
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                string? FocusedForm() => AnalyzeTextsLayoutTests.Strips(panel).Where(strip => strip.IsFocused)
                    .Select(strip => (ResultsInTextPanel.TokenOf(strip) as ResultsTokenViewModel)?.Form).SingleOrDefault();

                // The key must be marked handled before its gap is found, or focus traversal would also move it.
                var handledAtWindow = new List<bool>();
                window.AddHandler(InputElement.KeyDownEvent, (_, args) =>
                {
                    if (args.Key == Key.Tab) handledAtWindow.Add(args.Handled);
                }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

                // Sungura, alikula, watoto and hawajafika are approved: Tab goes straight to the first gap.
                Assert.True(AnalyzeTextsLayoutTests.StripOf(panel, "Sungura").Focus());
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.True(await Wait.ForAsync(() => FocusedForm() == "chakula", Wait.Default,
                    () => PageScreenshots.Settle(window)), $"focused='{FocusedForm()}'");
                Assert.Equal([true], handledAtWindow);

                // With chakula staged it is no longer a gap, so Tab from Sungura reaches mwalimu.
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.True(await Wait.ForAsync(() => Status(window) == Staged(1), Wait.Default,
                    () => PageScreenshots.Settle(window)), $"status='{Status(window)}'");
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                Assert.True(AnalyzeTextsLayoutTests.StripOf(panel, "Sungura").Focus());
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.True(await Wait.ForAsync(() => FocusedForm() == "mwalimu", Wait.Default,
                    () => PageScreenshots.Settle(window)), $"focused='{FocusedForm()}'");
                Assert.Single(workspace.Context.Changes.Items);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void EnterKeepsTheReadingChosenInTheWordPanel()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, inText, fake) = await OpenAsync();
            try
            {
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var mwalimu = Token(inText, "mwalimu");
                await inText.OpenTokenCardAsync(mwalimu);
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                var wordPanel = Assert.IsType<WordOccurrencesViewModel>(inText.OccurrencePanel);
                var chosen = Assert.Single(wordPanel.Readings);
                wordPanel.ChosenReading = chosen;
                var anchor = Token(inText, "mwalimu").Occurrence!;
                Assert.True(AnalyzeTextsLayoutTests.StripOf(panel, "mwalimu").Focus());

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.True(await Wait.ForAsync(() => Status(window) == Staged(1), Wait.Default,
                    () => PageScreenshots.Settle(window)), $"status='{Status(window)}'");

                var approve = Assert.Single(workspace.Context.Changes.Items, change => change.Occurrence == anchor);
                Assert.Equal(ChangeKinds.Approve, approve.Kind);
                Assert.Equal(chosen.Id, approve.StoredAnalysisId);
                Assert.Single(fake.PendingPutRequests);
                Assert.True(Token(inText, "mwalimu").IsPassageStaged);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    private sealed class ThrowingHost : SIL.Motif.App.Controls.WordPresentation.IWordPresentationHost
    {
        public Task<SIL.Motif.App.Controls.WordPresentation.WordCardReadResult> ReadCardAsync(
            SIL.Motif.App.Controls.WordPresentation.WordPresentationKey key, long evidenceRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<SIL.Motif.App.Controls.WordPresentation.WordActionResult> HandleAsync(
            SIL.Motif.App.Controls.WordPresentation.WordRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The store refused.");
    }

    [Fact]
    public void AnExceptionInTheKeepKeyIsAnnouncedAndNeverLost()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, inText, fake) = await OpenAsync();
            try
            {
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var strip = (SIL.Motif.App.Controls.WordPresentation.WordStripToken)AnalyzeTextsLayoutTests.StripOf(panel, "mwalimu");
                strip.Host = new ThrowingHost();
                Assert.True(strip.Focus());

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);

                var expected = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.PassageKeyFailedFormat, "The store refused.");
                Assert.True(await Wait.ForAsync(() => Status(window) == expected, Wait.Default,
                    () => PageScreenshots.Settle(window)), $"status='{Status(window)}'");
                Assert.Empty(workspace.Context.Changes.Items);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void AKeepRequestForAWordThatMovedIsRefusedWithASentenceNotSilence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, inText, fake) = await OpenAsync();
            try
            {
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var token = Token(inText, "mwalimu");
                foreach (var action in new[] { SIL.Motif.App.Controls.WordPresentation.WordAction.KeepAnalysis,
                    SIL.Motif.App.Controls.WordPresentation.WordAction.KeepAllLikeThis })
                {
                    var result = await panel.WordHost.HandleAsync(new SIL.Motif.App.Controls.WordPresentation.WordRequest(
                        token.PresentationKey, token.EvidenceRevision - 1, action), CancellationToken.None);
                    Assert.False(result.Handled);
                    Assert.Equal(MotifAppStrings.OccurrencesChangedStatus, result.StatusMessage);
                }
                Assert.Empty(workspace.Context.Changes.Items);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }
}
