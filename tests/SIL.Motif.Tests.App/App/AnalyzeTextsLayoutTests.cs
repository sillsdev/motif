using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using ContractWordRow = SIL.Motif.Contract.Responses.WordRow;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Renders Analyze texts over the sample project and checks the layout claims Round 4 makes for it: the word
/// card is a raised surface opened under its own line, the controls share one row, the text shows before the
/// first parse, and each word strip carries the Word, FieldWorks and PanGloss lines with one action and one Fix.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class AnalyzeTextsLayoutTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    [Fact]
    public void KeyboardReaderWalkthroughStagesOpinionsThenShowsThemInReview()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                inText.CloseTokenCard();
                Settle(window);
                var panel = Panel(window);
                var sungura = inText.VisibleLines.SelectMany(line => line.Tokens).Single(token => token.Form == "Sungura");
                var watoto = inText.VisibleLines.SelectMany(line => line.Tokens).Single(token => token.Form == "watoto");
                var hawajafika = inText.VisibleLines.SelectMany(line => line.Tokens).Single(token => token.Form == "hawajafika");
                var mwalimu = inText.VisibleLines.SelectMany(line => line.Tokens).Single(token => token.Form == "mwalimu");

                Assert.True(StripOf(panel, "Sungura").Focus());
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                Settle(window);
                Assert.Same(inText.VisibleLines[0].Tokens.Single(token => token.Form == "alikula"), FocusedToken(panel));
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                Settle(window);
                Assert.Same(inText.VisibleLines[0].Tokens.Single(token => token.Form == "chakula"), FocusedToken(panel));
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                Settle(window);
                Assert.Same(watoto, FocusedToken(panel));
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                Settle(window);
                Assert.Same(hawajafika, FocusedToken(panel));

                await inText.OpenTokenCardAsync(hawajafika);
                Settle(window);
                Assert.True(OpenCard(window).Focus());
                window.KeyPress(Key.U, RawInputModifiers.None, PhysicalKey.None, null);
                await WaitForPendingChanges(workspace, window, 1);
                Assert.Equal(ChangeKinds.Candidate, Assert.Single(workspace.Context.Changes.Items).Kind);

                inText.CloseTokenCard();
                Settle(window);
                Assert.True(StripOf(panel, "mwalimu").Focus());
                window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.None, null);
                await WaitForPendingChanges(workspace, window, 2);
                Assert.Contains(workspace.Context.Changes.Items, change => change.Word == "mwalimu" && change.Kind == ChangeKinds.Approve);

                window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.None, null);
                Settle(window);
                Assert.Same(watoto, FocusedToken(panel));
                window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.None, null);
                Settle(window);
                Assert.Same(sungura, FocusedToken(panel));
                window.KeyPress(Key.D, RawInputModifiers.None, PhysicalKey.None, null);
                await WaitForPendingChanges(workspace, window, 3);

                Assert.Contains(workspace.Context.Changes.Items, change => change.Word == "Sungura" && change.Kind == ChangeKinds.Reject);
                Assert.Contains(workspace.Context.Changes.Items, change => change.Word == "hawajafika" &&
                    change.Kind == ChangeKinds.Candidate &&
                    change.AfterOpinionMark == SIL.Motif.App.Controls.OpinionMarkKind.Unknown);
                Assert.Empty(Assert.IsType<FakeCommandClient>(workspace.Context.Commands).ApplyPendingRequests);

                workspace.CurrentPage = WorkspacePage.Review;
                Settle(window);
                var review = Assert.Single(window.GetLogicalDescendants().OfType<ReviewPanel>());
                var reviewText = string.Join(" ", review.GetVisualDescendants().OfType<TextBlock>()
                    .Select(text => text.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
                Assert.Contains("Sungura", reviewText, StringComparison.Ordinal);
                Assert.Contains("mwalimu", reviewText, StringComparison.Ordinal);
                Assert.Contains("hawajafika", reviewText, StringComparison.Ordinal);
                Assert.Contains("Approved", reviewText, StringComparison.Ordinal);
                Assert.Contains("Unknown", reviewText, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void WordListAndListsRowsStageOpinionsFromTheKeyboard()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var page = workspace.PageModel<TextsPageModel>();
                page.Words.SearchText = "mwalimu";
                page.AnalyzeView = AnalyzeTextsView.WordList;
                Settle(window);
                var rows = window.GetVisualDescendants().OfType<SIL.Motif.App.Controls.WordPresentation.WordRow>().ToArray();
                Assert.True(rows.Any(row => row.Data?.Owner == WordListOwner.WordList && row.Data.Facts.Word == "mwalimu"),
                    $"Realized rows: {string.Join(", ", rows.Select(row => $"{row.Data?.Owner}:{row.Data?.Facts.Word ?? "<null>"}"))}");
                var unknownWord = Assert.Single(rows, row =>
                    row.Data?.Owner == WordListOwner.WordList && row.Data.Facts.Word == "mwalimu");
                unknownWord.FocusWord();
                window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.None, null);
                await WaitForPendingChanges(workspace, window, 1);
                Assert.Contains(workspace.Context.Changes.Items, change => change.Word == "mwalimu" &&
                    change.Kind == ChangeKinds.Approve);

                page.Tab = TextsTab.Lists;
                Settle(window);
                var approvedWord = Assert.Single(window.GetVisualDescendants().OfType<SIL.Motif.App.Controls.WordPresentation.WordRow>(), row =>
                    row.Data?.Owner == WordListOwner.Lists && row.Data.Facts.Word == "hawajafika");
                approvedWord.FocusWord();
                window.KeyPress(Key.U, RawInputModifiers.None, PhysicalKey.None, null);
                await WaitForPendingChanges(workspace, window, 2);
                Assert.Contains(workspace.Context.Changes.Items, change => change.Word == "hawajafika" &&
                    change.Kind == ChangeKinds.Candidate);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static ResultsTokenViewModel FocusedToken(ResultsInTextPanel panel) =>
        Assert.IsType<ResultsTokenViewModel>(Assert.Single(Strips(panel), strip => strip.IsFocused).Tag);

    private static async Task WaitForPendingChanges(WorkspaceShellViewModel workspace, Window window, int count)
    {
        for (var attempt = 0; attempt < 60 && workspace.Context.Changes.Items.Count < count; attempt++)
        {
            await Task.Delay(10);
            Settle(window);
        }
        Assert.Equal(count, workspace.Context.Changes.Items.Count);
    }

    [Fact]
    public void TheOpenWordCardHasARaisedSurfaceAndAShadowInBothThemes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    Settle(window);
                    var card = OpenCard(window);
                    var frame = CardFrame(card);
                    Assert.True(Application.Current.TryGetResource("Intent.Surface.Raised", variant, out var surface));
                    Assert.Same(surface, frame.Background);
                    Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(frame.Background).Color.A);
                    Assert.True(Application.Current.TryGetResource("Intent.Shadow.Raised", variant, out var shadow));
                    Assert.Equal(shadow, frame.BoxShadow);
                    Assert.True(frame.BoxShadow.Count > 0);
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void TheWordCardOpensUnderItsLineAndCoversNoWord()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                Settle(window);
                var panel = Panel(window);
                var card = OpenCard(window);
                var open = Assert.IsType<ResultsTokenViewModel>(card.DataContext);
                Assert.DoesNotContain(panel.GetLogicalDescendants().OfType<Popup>(), popup => popup.IsOpen);

                var cardBounds = BoundsIn(card, panel);
                Assert.True(cardBounds.Left >= -0.5 && cardBounds.Right <= panel.Bounds.Width + 0.5,
                    $"The opened word card spans {cardBounds.Left:0.#}–{cardBounds.Right:0.#} px of its " +
                    $"{panel.Bounds.Width:0.#} px panel with the app's default text size.");
                LayoutAssertions.AssertCurrent(card);
                var strips = Strips(panel).ToArray();
                var ownStrip = Assert.Single(strips, strip => ReferenceEquals(strip.Tag, open));
                Assert.True(cardBounds.Top >= BoundsIn(ownStrip, panel).Bottom,
                    $"The card starts at {cardBounds.Top}, above the bottom of its word at {BoundsIn(ownStrip, panel).Bottom}.");
                Assert.All(strips, strip => Assert.False(BoundsIn(strip, panel).Intersects(cardBounds),
                    $"The card covers {((ResultsTokenViewModel)strip.Tag!).Form}."));
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void AnalyzeTextsExplainsExactWarningsSeparatelyFromCandidatesAndUsesStoredTiming()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                configure: (fake, assessment) => ConfigureStoredExplanation(fake, assessment));
            try
            {
                workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
                workspace.CurrentPage = WorkspacePage.Texts;
                window.Height = 1500;
                Settle(window);
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                await inText.WarningEvidenceRefresh;
                Assert.Equal(1, inText.NamedInWarningCount);
                Assert.True(inText.HasWarningEvidence);
                var token = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
                    .Single(item => item.Form == "alikula");
                Assert.True(token.HasNamedWarning);
                Assert.Equal("anapenda", Assert.Single(token.MembershipCandidateWarnings).WordsLabel);
                Assert.Equal("watoto", Assert.Single(token.SpellingCandidateWarnings).WordsLabel);
                await inText.OpenTokenCardAsync(token);
                Settle(window);
                Assert.Contains(OpenCard(window).GetVisualDescendants().OfType<Button>(), button =>
                    Equals(button.Content, "Try a Word"));

                var panel = Panel(window);
                var warningFilter = Assert.Single(panel.GetVisualDescendants().OfType<FilterChip>(),
                    chip => chip.Label == "Named in a warning");
                Assert.Equal(1, warningFilter.Count);
                var warningStrip = Assert.Single(Strips(panel), strip =>
                    strip.Tag is ResultsTokenViewModel { Form: "alikula" });
                var warningIcon = Assert.Single(warningStrip.GetVisualDescendants().OfType<MarkGlyph>(),
                    glyph => glyph.Mark == Mark.Warning);
                Assert.IsType<PathIcon>(warningIcon.Child);
                var warningStatus = warningIcon.FindAncestorOfType<StackPanel>()!;
                Assert.Equal("Named in 1", Avalonia.Automation.AutomationProperties.GetName(warningStatus));
                Assert.Contains(warningStatus.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "1");
                var card = OpenCard(window);
                var cardLines = card.GetVisualDescendants().OfType<CopyableTextBlock>().ToArray();
                Assert.Contains(cardLines, line => line.Text == "What a warning names in this word");
                Assert.Contains(cardLines, line => line.Text == "A grammar warning names -a.");
                Assert.Contains(cardLines, line => line.Text == "Other words that use -a");
                Assert.DoesNotContain(cardLines, line => line.Text == "PanGloss exact warning message");
                Assert.DoesNotContain(cardLines, line => line.Text == "Named exactly");
                Assert.DoesNotContain(cardLines, line => line.Text == "exact");
                Assert.DoesNotContain(cardLines, line => line.Text == "Words that use members of the named resource");
                Assert.DoesNotContain(cardLines, line => line.Text == "Matched by spelling only");
                Assert.DoesNotContain(cardLines, line => line.Text?.EndsWith(" · membership") == true);
                Assert.DoesNotContain(cardLines, line => line.Text?.EndsWith(" · spelling") == true);
                var cardText = string.Join(" ", cardLines.Select(line => line.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
                Assert.Contains("Time by rule", cardText);
                Assert.Contains("Subject agreement", cardText);
                Assert.Contains("Not attributed", cardText);
                var whySection = Assert.Single(card.GetVisualDescendants().OfType<Border>(), border =>
                    border.Classes.Contains("wordPresentationCardSection") &&
                    border.GetVisualDescendants().OfType<CopyableTextBlock>()
                        .Any(line => line.Text == "What a warning names in this word"));
                var whyText = whySection.GetVisualDescendants().OfType<CopyableTextBlock>().Select(line => line.Text);
                Assert.DoesNotContain("Time by rule", whyText);
                Assert.DoesNotContain("Subject agreement", whyText);
                Assert.Contains("anapenda", cardText);
                Assert.Contains("anapenda", token.OtherWordsUsingMorpheme);
                Assert.Contains(token.WarningMarkedFieldWorksMorphs,
                    morph => morph.Form == "-a" && morph.IsNamedInWarning);
                Assert.InRange(token.TimingShares.Sum(row => row.Share) + token.NotAttributedShare!.Value,
                    0.999, 1.001);

                warningFilter.Command!.Execute(warningFilter.CommandParameter);
                Settle(window);
                Assert.Equal("NamedInWarning", inText.Filter.ToString());
                Assert.False(token.IsDimmed);
                Assert.All(inText.SelectedText!.Lines.SelectMany(line => line.Tokens)
                    .Where(item => item.IsWord && item.Form != "alikula"), item => Assert.True(item.IsDimmed));
                var openWarning = Assert.Single(OpenCard(window).GetVisualDescendants().OfType<Button>(),
                    button => button.Content?.ToString() == "Open the warning");
                Assert.NotNull(openWarning.Command);
                openWarning.Command.Execute(openWarning.CommandParameter);
                Assert.Equal(WorkspacePage.Warnings, workspace.CurrentPage);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void AnalyzeTextsAccountsForAllOfHawajafikaTiming()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                configure: (fake, assessment) => ConfigureStoredExplanation(fake, assessment));
            try
            {
                workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
                workspace.CurrentPage = WorkspacePage.Texts;
                window.Height = 1500;
                Settle(window);
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                await inText.WarningEvidenceRefresh;
                var token = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
                    .Single(item => item.Form == "hawajafika");
                await inText.OpenTokenCardAsync(token);
                Settle(window);

                Assert.InRange(token.TimingShares.Sum(row => row.Share) + token.NotAttributedShare!.Value,
                    0.999, 1.001);
                Assert.Contains(token.TimingShares, row => row.Name == "Other rules");
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    internal static void ConfigureStoredExplanation(FakeCommandClient fake, AssessCommandResponse assessment,
        string timedWord = "alikula")
    {
        var morpheme = assessment.Words.Single(word => word.Word == "alikula").StoredAnalyses
            .SelectMany(analysis => analysis.Morphs).Single(morph => morph.Form == "-a");
        var exactWord = WarningWord("alikula");
        var candidateWord = WarningWord("anapenda");
        var exactReach = new WarningReach(WarningWordsPath.Uses)
        {
            AllomorphIds = [morpheme.AllomorphId!],
            GrammaticalInfoIds = [morpheme.GrammaticalInfoId!],
        };
        var membershipReach = new WarningReach(WarningWordsPath.Membership)
        {
            MembershipAllomorphIds = [morpheme.AllomorphId!],
        };
        var spellingReach = new WarningReach(WarningWordsPath.Spelling) { Spellings = ["a"] };
        var warnings = new[]
        {
            SampleWarning("exact", string.Empty, WarningWordsMatch.Identity,
                [exactWord], new GrammarWarningPart("-a", GrammarWarningPartRole.Object,
                    morpheme.AllomorphId, "MoForm") { Reach = exactReach },
                membershipCandidates: [candidateWord], spellingCandidates: [WarningWord("watoto")]),
            SampleWarning("membership", "PanGloss membership warning message", WarningWordsMatch.Membership,
                [candidateWord], new GrammarWarningPart("slot", GrammarWarningPartRole.Object) { Reach = membershipReach }),
            SampleWarning("spelling", "PanGloss spelling warning message", WarningWordsMatch.Spelling,
                [candidateWord], new GrammarWarningPart("letter a", GrammarWarningPartRole.Object) { Reach = spellingReach }),
        };
        fake.StoredGrammarCheckIs(new GrammarCheckResponse(warnings, HasBaseline: true));
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            StoredTiming(request.ExplicitWords?.SingleOrDefault() ?? timedWord))));
    }

    private static TimingResponse StoredTiming(string word) => word == "hawajafika"
        ? new TimingResponse("assessment/one", "all", "rule", 1, 1, 1, [],
            [
                new TimingAggregateRow("rule/subject", "Subject agreement", 0.36, 0.36, 1)
                    { Kind = "morph_rule", IdentityQuality = "authored" },
                new TimingAggregateRow("rule/past", "Past tense", 0.19, 0.19, 1)
                    { Kind = "morph_rule", IdentityQuality = "authored" },
                new TimingAggregateRow("rule/harmony", "Vowel harmony", 0.15, 0.15, 1)
                    { Kind = "phon_rule", IdentityQuality = "authored" },
            ], [])
        {
            Words = [new TimingWordRow(word, 1, TimingCompletion.Finished) { ElapsedNs = 1_000_000 }],
            Attribution = new WordTimeAttribution(1, 1, 0.85, 0.15, 0.15, 0, false),
        }
        : new TimingResponse("assessment/one", "all", "rule", 1, 0.9, 0.9,
        [],
        [
            new TimingAggregateRow("rule/subject", "Subject agreement", 0.4, 0.4 / 0.9, 1)
                { Kind = "morph_rule", IdentityQuality = "authored" },
            new TimingAggregateRow("rule/past", "Past tense", 0.2, 0.2 / 0.9, 1)
                { Kind = "morph_rule", IdentityQuality = "authored" },
            new TimingAggregateRow("rule/harmony", "Vowel harmony", 0.1, 0.1 / 0.9, 1)
                { Kind = "phon_rule", IdentityQuality = "authored" },
        ], [])
    {
        Words = [new TimingWordRow(word, 1, TimingCompletion.Finished) { ElapsedNs = 900_000 }],
        Attribution = new WordTimeAttribution(1, 0.9, 0.7, 0.2, 0.2 / 0.9, 0, false),
        };

    private static ObjectUseWord WarningWord(string word) => new(new ContractWordRow(
        word, WordRowOutcome.Different, "Different", WordRowTone.Look));

    private static GrammarWarning SampleWarning(string code, string description, WarningWordsMatch match,
        IReadOnlyList<ObjectUseWord> words, GrammarWarningPart subject,
        IReadOnlyList<ObjectUseWord>? membershipCandidates = null,
        IReadOnlyList<ObjectUseWord>? spellingCandidates = null) => new(
        GrammarDiagnosticLevel.Warning, code, [subject],
        [new GrammarWarningPart(description, GrammarWarningPartRole.Text)], description)
    {
        Code = code,
        Group = code,
        Description = description,
        YourWords = new WarningWords(match, words, [])
        {
            Paths = [subject.Reach!.Path],
            MembershipCandidates = membershipCandidates ?? [],
            SpellingCandidates = spellingCandidates ?? [],
        },
    };

    [Fact]
    public void ArrowKeysMoveTheCardBetweenWordsAndEscapeClosesItBackOntoTheWord()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                var alikula = inText.SelectedToken!;
                inText.CloseTokenCard();
                Settle(window);
                await inText.OpenTokenCardAsync(alikula);
                Settle(window);
                var first = OpenCard(window);
                Assert.True(first.IsKeyboardFocusWithin, "Opening the card puts the keyboard in it.");

                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                await inText.ReadStateRefresh;
                Settle(window);
                Assert.Equal("chakula", inText.SelectedToken?.Form);
                var moved = OpenCard(window);
                Assert.Same(inText.SelectedToken, moved.DataContext);
                Assert.True(moved.IsKeyboardFocusWithin, "The keyboard follows the card to the next word.");

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                Settle(window);
                Assert.Null(inText.SelectedToken);
                Assert.DoesNotContain(Panel(window).GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("wordPresentationCardFrame"));
                var strip = Assert.Single(Strips(Panel(window)), candidate =>
                    candidate.Tag is ResultsTokenViewModel { Form: "chakula" });
                Assert.True(strip.IsFocused, "Escape returns the keyboard to the word whose card closed.");

                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                Settle(window);
                Assert.Null(inText.SelectedToken);
                Assert.True(StripOf(Panel(window), "watoto").IsFocused, "With no card open, arrows move between words.");
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void FilterChipsStayClearOfReaderMenusAtWindowWidthsAndZoom()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            var settings = Assert.IsType<SettingsViewModel>(Assert.IsType<SettingsPopupView>(
                Assert.IsType<Flyout>(window.FindControl<Button>("SettingsButton")!.Flyout).Content).DataContext);
            var originalZoom = settings.ZoomPercent;
            try
            {
                workspace.PageModel<TextsPageModel>().ResultsInText.CloseTokenCard();
                foreach (var (width, zoom) in new[]
                {
                    (1040, 100), (1240, 100), (1040, 125), (1240, 125), (1040, 150), (1240, 150),
                })
                {
                    settings.ZoomPercent = zoom;
                    window.Width = width;
                    Settle(window);
                    var panel = Panel(window);
                    var picker = Named<ComboBox>(panel, "Text to read");
                    var dropdowns = new[]
                    {
                        Named<Button>(panel, "Mark read or unread"),
                        Named<Button>(panel, "Select words for actions"),
                    };
                    var chips = panel.GetVisualDescendants().OfType<FilterChip>()
                        .Where(chip => chip.IsEffectivelyVisible).ToArray();
                    Assert.Equal(["All", "Unread", "Different", "Not in FieldWorks", "No parse", "Stopped"],
                        chips.Select(chip => chip.Label));
                    var chipRow = panel.FindControl<WrapPanel>("ResultsInTextFilterChips")!;
                    var rowBounds = BoundsIn(chipRow, panel);
                    Assert.All(dropdowns, dropdown => Assert.False(rowBounds.Intersects(BoundsIn(dropdown, panel)),
                        $"The filter row intersects '{AutomationProperties.GetName(dropdown)}' at {width}px/{zoom}% zoom: " +
                        $"{rowBounds} and {BoundsIn(dropdown, panel)}."));
                    foreach (var chip in chips)
                    foreach (var dropdown in dropdowns)
                        Assert.False(BoundsIn(chip, panel).Intersects(BoundsIn(dropdown, panel)),
                            $"Filter chip '{chip.Label}' intersects '{AutomationProperties.GetName(dropdown)}' " +
                            $"at {width}px/{zoom}% zoom: {BoundsIn(chip, panel)} and {BoundsIn(dropdown, panel)}.");

                    var picked = Assert.Single(picker.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.Text == "Hadithi ya sungura");
                    Assert.DoesNotContain(picked.TextLayout.TextLines, line => line.HasCollapsed);

                    var bounds = new Control[] { picker }.Concat(dropdowns).Concat(chips)
                        .Select(control => BoundsIn(control, panel)).ToArray();
                    var rowTop = bounds.Min(rect => rect.Top);
                    var rowBottom = bounds.Max(rect => rect.Bottom);
                    Assert.All(bounds, rect => Assert.InRange(rect.Left, 0, panel.Bounds.Width));
                    Assert.All(bounds, rect => Assert.True(rect.Right <= panel.Bounds.Width,
                        $"A control extends beyond the text panel at {width}px/{zoom}% zoom: {rect}."));
                    Assert.True(rowBottom - rowTop <= 96,
                        $"The control area is {rowBottom - rowTop} px deep at {width}px/{zoom}% zoom. " +
                        $"Toolbar {panel.FindControl<Grid>("ReaderToolbar")!.Bounds}; chip column " +
                        $"{panel.FindControl<Grid>("ReaderToolbar")!.ColumnDefinitions[2].ActualWidth:0.#} px; " +
                        $"chip row {Grid.GetRow(chipRow)}; controls: {string.Join("; ", bounds)}.");
                    var selectLabel = Assert.Single(dropdowns[1].GetVisualDescendants().OfType<AccessText>(),
                        text => text.Text == "Select ▾");
                    Assert.DoesNotContain(selectLabel.TextLayout.TextLines, line => line.HasCollapsed);
                    var firstWord = Strips(panel).Min(strip => BoundsIn(strip, panel).Top);
                    Assert.True(firstWord - rowBottom <= 48,
                        $"The text starts {firstWord - rowBottom} px below the controls at {width}px/{zoom}% zoom.");
                    Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Expander>(), expander =>
                        expander.IsEffectivelyVisible && Equals(expander.Header, "Actions by scope"));
                }
            }
            finally
            {
                settings.ZoomPercent = originalZoom;
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void WordCheckboxesWaitUntilTheReaderChoosesWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                bool AnyCheckboxShows() => Panel(window).GetVisualDescendants().OfType<CheckBox>().Any(box =>
                    box.IsEffectivelyVisible && box.DataContext is ResultsTokenViewModel);
                Assert.False(AnyCheckboxShows());

                inText.ChooseWordsCommand.Execute(null);
                Settle(window);
                Assert.True(AnyCheckboxShows());

                inText.ChooseWordsCommand.Execute(null);
                Settle(window);
                Assert.False(AnyCheckboxShows());
                inText.SelectAllWordsCommand.Execute(null);
                Settle(window);
                Assert.True(AnyCheckboxShows(), "Checked words keep their checkboxes so they can be cleared.");
                Assert.Equal($"{inText.AllCount} selected ▾", inText.SelectMenuLabel);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void BeforeTheFirstParseTheTextShowsUnderTheParsePrompt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts(parse: false);
            try
            {
                Assert.True(workspace.Context.NeedsAssessment);
                var page = Assert.Single(window.GetLogicalDescendants().OfType<TextsPage>());
                var prompt = Assert.Single(page.GetVisualDescendants().OfType<ParsePrompt>(),
                    candidate => candidate.IsEffectivelyVisible);
                var action = Assert.Single(prompt.GetVisualDescendants().OfType<Button>());
                Assert.Equal(workspace.Context.ParsePromptActionText, action.Content);

                var panel = Panel(window);
                var strips = Strips(panel).ToArray();
                Assert.Equal(["Sungura", "alikula", "chakula"],
                    strips.Take(3).Select(strip => ((ResultsTokenViewModel)strip.Tag!).Form));
                Assert.True(BoundsIn(prompt, page).Bottom <= BoundsIn(strips[0], page).Top,
                    "The prompt sits above the text.");
                var sungura = strips[0];
                Assert.Contains(sungura.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "hare" && text.IsEffectivelyVisible);
                Assert.Contains(sungura.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "Not parsed yet" && text.IsEffectivelyVisible);
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<FilterChip>(), chip => chip.IsEffectivelyVisible);
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(button) is
                        "Mark read or unread" or "Select words for actions");
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void EachLineHasAWordFieldWorksPanGlossGutterAlignedWithItsStrips()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                workspace.PageModel<TextsPageModel>().ResultsInText.CloseTokenCard();
                Settle(window);
                var panel = Panel(window);
                var sungura = StripOf(panel, "Sungura");
                var line = sungura.GetVisualAncestors().OfType<ContentPresenter>()
                    .First(presenter => presenter.DataContext is ResultsLineViewModel);
                foreach (var (label, part) in new[] { ("Word", "word"), ("FieldWorks", "fieldworks"), ("PanGloss", "pangloss") })
                {
                    var gutter = Assert.Single(line.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.Text == label && text.Classes.Contains("gutterLabel"));
                    var row = Part(sungura, part);
                    Assert.True(BoundsIn(gutter, panel).Top < BoundsIn(row, panel).Bottom &&
                        BoundsIn(row, panel).Top < BoundsIn(gutter, panel).Bottom,
                        $"{label} sits at {BoundsIn(gutter, panel).Top}, its row at {BoundsIn(row, panel).Top}.");
                }
                var lineBody = Assert.Single(line.GetVisualDescendants().OfType<SIL.Motif.App.Controls.RunningTextPanel>(),
                    runningText => runningText.Classes.Contains("resultsLineBody"));
                var strips = lineBody.GetVisualDescendants().OfType<WordStripToken>()
                    .Where(strip => strip.IsVisible).ToArray();
                Assert.NotEmpty(strips);
                var metrics = strips.Select(strip => strip.LineMetrics)
                    .Aggregate(default(RunningTextLineMetrics), static (current, next) => current.Max(next));
                Assert.True(metrics.Word > 0 && metrics.FieldWorks > 0 && metrics.PanGloss > 0);
                Assert.True(Assert.Single(line.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Word")
                    .MinHeight >= metrics.Word);
                Assert.True(Assert.Single(line.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "FieldWorks")
                    .MinHeight >= metrics.FieldWorks);
                Assert.True(Assert.Single(line.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "PanGloss")
                    .MinHeight >= metrics.PanGloss);
                foreach (var strip in strips)
                {
                    AssertNativeMetric(strip, "WordLine", strip.LineMetrics.Word);
                    AssertNativeMetric(strip, "FieldWorksLine", strip.LineMetrics.FieldWorks);
                    AssertNativeMetric(strip, "PanGlossLine", strip.LineMetrics.PanGloss);
                }
                Assert.True(Math.Abs(lineBody.Bounds.Width - line.Bounds.Width) <= 1,
                    $"The line uses {lineBody.Bounds.Width:0.#} of its {line.Bounds.Width:0.#} px available width.");
                Assert.Equal(11, Assert.Single(line.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "Word").FontSize);
                Assert.Equal(10d * 96 / 72, Assert.Single(Part(sungura, "pangloss").GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Classes.Contains("morphemePanelGloss")).FontSize);
                Assert.Contains(line.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "Sungura alikula chakula." && text.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void OpeningTheInspectorFoldsTheTextSelectionListButLeavesTheTextPickerVisible()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var selection = Assert.Single(window.GetVisualDescendants().OfType<SelectionPanel>());
                var picker = Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(), combo =>
                    AutomationProperties.GetName(combo) == "Text to read");
                Assert.True(selection.IsEffectivelyVisible);
                Assert.True(picker.IsEffectivelyVisible);

                workspace.Context.OpenInspector(InspectorSubject.Morpheme("allomorph", "grammatical-info")!);
                Settle(window);

                Assert.False(selection.IsEffectivelyVisible);
                Assert.True(picker.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void StripsReadAsRoundFour()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                workspace.PageModel<TextsPageModel>().ResultsInText.CloseTokenCard();
                Settle(window);
                var panel = Panel(window);
                Assert.Contains("wa- 2 toto child", VisibleText(Part(StripOf(panel, "watoto"), "pangloss")));
                Assert.Equal("∅ No parse", VisibleText(Part(StripOf(panel, "hawajafika"), "pangloss")));
                var stopped = Part(StripOf(panel, "mwalimu"), "pangloss");
                Assert.Equal("Stopped", VisibleText(stopped));
                Assert.Contains(stopped.GetVisualDescendants().OfType<MarkGlyph>(), glyph => glyph.Mark == Mark.Stopped);
                Assert.StartsWith("Nothing in FieldWorks", VisibleText(Part(StripOf(panel, "chakula"), "fieldworks")), StringComparison.Ordinal);
                Assert.Equal("ch- 7 akula food", VisibleText(Part(StripOf(panel, "chakula"), "pangloss")));
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "Different from FieldWorks");

                var rowsMinWidth = (double)Application.Current!.FindResource("Component.WordStrip.RowsMinWidth")!;
                foreach (var strip in Strips(panel))
                {
                    var form = ((ResultsTokenViewModel)strip.Tag!).Form;
                    Assert.DoesNotContain(strip.GetVisualDescendants().OfType<Expander>(), _ => true);
                    var marks = strip.GetVisualDescendants().OfType<Button>().Where(button =>
                        Avalonia.Automation.AutomationProperties.GetName(button) ==
                        SIL.Motif.App.Views.WordDispositionButtons.MarkButtonName).ToArray();
                    Assert.True(marks.Length == 1, $"{form} offers {marks.Length} opinion mark buttons.");
                    Assert.All(marks, mark =>
                    {
                        Assert.Single(mark.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
                        Assert.NotNull(mark.Flyout);
                    });
                    Assert.Equal(strip.BorderThickness.Top, strip.BorderThickness.Bottom);
                    Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(strip.BorderBrush).Color.A);

                    Assert.Empty(strip.GetVisualDescendants().OfType<HyperlinkButton>());
                    var rows = Assert.Single(strip.GetVisualDescendants().OfType<StackPanel>(),
                        panel => panel.Classes.Contains("stripStack"));
                    Assert.True(rows.Bounds.Width >= rowsMinWidth,
                        $"{form}'s rows are {rows.Bounds.Width} wide, under {rowsMinWidth}.");
                }

                var alikula = StripOf(panel, "alikula");
                var unread = Part(alikula, "unread");
                Assert.True(BoundsIn(unread, panel).Right <= BoundsIn(Part(alikula, "word"), panel).Left,
                    "The Unread dot comes before the word.");
                Assert.DoesNotContain(unread.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible);
                await workspace.PageModel<TextsPageModel>().ResultsInText.OpenTokenCardAsync(
                    (ResultsTokenViewModel)alikula.Tag!);
                Settle(window);
                var wordLink = Assert.Single(OpenCard(window).GetVisualDescendants().OfType<HyperlinkButton>(),
                    link => Avalonia.Automation.AutomationProperties.GetName(link) == "Open alikula in Word Analyses");
                Assert.Equal("Open alikula in Word Analyses", Avalonia.Automation.AutomationProperties.GetName(wordLink));
                Assert.Contains(wordLink.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Word Analyses ↗");
                Assert.Equal(0, wordLink.Opacity);
                Assert.True(wordLink.Focus(NavigationMethod.Tab));
                Settle(window);
                Assert.Equal(1, wordLink.Opacity);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void AStagedAddSaysWillAddAsApprovedAndCoversNoOtherWord()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                inText.CloseTokenCard();
                var chakula = inText.VisibleLines[0].Tokens.Single(token => token.Form == "chakula");
                var add = Assert.Single(chakula.Marking.FixChoices, choice => choice.Label == "Add as Approved");
                await chakula.StageMarkingChoiceForTokenCommand!.ExecuteAsync(add);
                Settle(window);

                var panel = Panel(window);
                var staged = Part(StripOf(panel, "chakula"), "staged");
                Assert.Contains("Will add as Approved", VisibleText(staged), StringComparison.Ordinal);
                var stagedBounds = BoundsIn(staged, panel);
                Assert.All(Strips(panel).Where(strip => !ReferenceEquals(strip.Tag, chakula)), strip =>
                    Assert.False(BoundsIn(strip, panel).Intersects(stagedBounds),
                        $"The staged note covers {((ResultsTokenViewModel)strip.Tag!).Form}."));
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void TheSampleTextsFitAboveTheFoldAt1240()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                workspace.PageModel<TextsPageModel>().ResultsInText.CloseTokenCard();
                Settle(window);
                var panel = Panel(window);
                var viewer = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(), candidate =>
                    candidate.IsEffectivelyVisible && candidate.Content is ItemsControl);
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                foreach (var text in inText.Texts)
                {
                    inText.SelectedText = text;
                    Settle(window);
                    var lastWord = text.Lines.Last().Tokens.Last(token => token.IsWord).Form;
                    var last = BoundsIn(StripOf(panel, lastWord), viewer);
                    Assert.True(last.Bottom <= viewer.Viewport.Height,
                        $"{text.Title} ends at {last.Bottom}, below the {viewer.Viewport.Height} px the reader shows.");
                }
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void SwitchingTextsReturnsTheReaderToTheFirstLine()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts(parse: false);
            try
            {
                var firstId = Guid.NewGuid();
                var secondId = Guid.NewGuid();
                TextLines Text(Guid id, string title) => new(id, title,
                    Enumerable.Range(1, 80).Select(number => new TextLine(number,
                        [new TextToken($"word{number}", $"word{number}", null, "unanalysed")])).ToArray());
                var fake = Assert.IsType<FakeCommandClient>(workspace.Context.Commands);
                fake.ListTextWordsCompletesWith(new TextWordsResponse([],
                    [Text(firstId, "First"), Text(secondId, "Second")], HasBaseline: true, OccurrenceCount: 160));
                var page = workspace.PageModel<TextsPageModel>();
                await page.Words.ReloadAsync();
                window.Height = 320;
                Settle(window);

                var panel = Panel(window);
                var viewer = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(), candidate =>
                    candidate.IsEffectivelyVisible && candidate.Content is ItemsControl);
                Assert.True(viewer.Extent.Height > viewer.Viewport.Height);
                viewer.Offset = new Vector(0, viewer.Extent.Height);
                Settle(window);
                Assert.True(viewer.Offset.Y > 0);

                page.ResultsInText.SelectedText = page.ResultsInText.Texts[1];
                Settle(window);

                Assert.Equal(0, viewer.Offset.Y);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void UnbrokenCombiningAndRightToLeftFormsRetainTextWithMeasuredBounds()
    {
        string[] forms = [new string('m', 180), "e\u0301lan", "עברית־מילים"];
        var textId = Guid.NewGuid();
        var line = new TextLine(1, forms.Select(form => new TextToken(
            form, form, null, "unanalysed")).ToArray());
        var words = new TextWordsResponse([], [new TextLines(textId, "Unicode layout", [line])], HasBaseline: true);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts(parse: false,
                configure: (fake, _) => fake.ListTextWordsCompletesWith(words));
            try
            {
                var panel = Panel(window);
                var viewer = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(), candidate =>
                    candidate.IsEffectivelyVisible && candidate.Content is ItemsControl);
                foreach (var form in forms)
                {
                    var strip = StripOf(panel, form);
                    Assert.Contains(form, VisibleText(strip), StringComparison.Ordinal);
                    var bounds = BoundsIn(strip, viewer);
                    Assert.True(bounds.Width > 0 && bounds.Height > 0,
                        $"'{form}' has an empty layout box.");
                    Assert.True(bounds.Left < viewer.Viewport.Width && bounds.Right > 0,
                        $"'{form}' has no horizontal overlap with the reader viewport.");
                }
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void RightToLeftParagraphUsesItsFieldWorksSizeAndGrowsItsWordRow()
    {
        Walkthrough.WalkthroughFonts.Register();
        var words = WritingSystemTestData.ArabicText();
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts(parse: false,
                configure: (fake, _) => fake.ListTextWordsCompletesWith(words));
            try
            {
                var resolver = workspace.Context.TextStyles;
                resolver.SetFallbackFamilies(["fonts:MotifWalkthrough#DejaVu Sans"]);
                resolver.ReplaceContext([WritingSystemTestData.Arabic]);
                await workspace.PageModel<TextsPageModel>().Words.SetProjectAsync(PageScreenshots.SampleProjectPath);
                Settle(window);

                var panel = Panel(window);
                Assert.Contains(workspace.PageModel<TextsPageModel>().ResultsInText.VisibleLines
                    .SelectMany(line => line.Tokens), token => token.Form == WritingSystemTestData.Form);
                var strip = StripOf(panel, WritingSystemTestData.Form);
                var form = Assert.Single(strip.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == WritingSystemTestData.Form && text.Classes.Contains("stripWord"));
                var lineBody = Assert.Single(strip.GetVisualAncestors()
                    .OfType<SIL.Motif.App.Controls.RunningTextPanel>(), candidate =>
                    candidate.Classes.Contains("resultsLineBody"));
                var gutter = Assert.Single(lineBody.GetVisualDescendants().OfType<StackPanel>(), candidate =>
                    candidate.Classes.Contains("gutter"));
                var gutterBounds = TransformedBoundsIn(gutter, panel);
                Assert.All(gutter.Children.OfType<TextBlock>(), label =>
                {
                    Assert.Equal(HorizontalAlignment.Left, label.HorizontalAlignment);
                    Assert.Equal(TextAlignment.Left, label.TextAlignment);
                    var labelBounds = TransformedBoundsIn(label, panel);
                    Assert.True(labelBounds.Left < gutterBounds.Left + gutterBounds.Width / 2,
                        $"RTL gutter label {label.Text} starts at {labelBounds.Left:0.#} in {gutterBounds}.");
                });
                var row = strip.FindControl<StackPanel>("WordLine")!;

                Assert.Equal(24, form.FontSize);
                Assert.Equal(FlowDirection.RightToLeft, form.FlowDirection);
                var sentence = Assert.Single(lineBody.GetVisualAncestors().OfType<ContentPresenter>()
                    .First(presenter => presenter.DataContext is ResultsLineViewModel)
                    .GetVisualDescendants().OfType<TextBlock>(), text =>
                    SIL.Motif.App.Controls.WritingSystemText.GetStyleName(text) == "Paragraph");
                Assert.Equal(20 * 96d / 72d, sentence.FontSize);
                var tokenLayout = lineBody.Children.OfType<ItemsControl>().Single();
                Assert.Equal(FlowDirection.LeftToRight, tokenLayout.FlowDirection);
                Assert.Equal(FlowDirection.LeftToRight, lineBody.FlowDirection);
                Assert.Equal(FlowDirection.RightToLeft, lineBody.TextDirection);
                var wordLayout = Assert.Single(tokenLayout.GetVisualDescendants().OfType<WrapPanel>(), wrap =>
                    wrap.GetVisualDescendants().OfType<WordStripToken>().Any());
                Assert.Equal(FlowDirection.RightToLeft, wordLayout.FlowDirection);
                Assert.True(row.Bounds.Height > 22, $"The 18-point word row is only {row.Bounds.Height:0.#} px high.");
                var stripBounds = TransformedBoundsIn(strip, panel);
                Assert.True(gutterBounds.Left >= stripBounds.Right,
                    $"body={TransformedBoundsIn(lineBody, panel)}; items={TransformedBoundsIn(tokenLayout, panel)}; " +
                    $"gutter={gutterBounds}; strip={stripBounds}; " +
                    $"flow={lineBody.FlowDirection}; id={SIL.Motif.App.Controls.WritingSystemText.GetFlowId(lineBody)}");
                LayoutAssertions.AssertCurrent(window);
                Assert.Single(resolver.MissingFontNotices);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void EachChipCountsTheStripsThatShowItsClass()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                workspace.PageModel<TextsPageModel>().ResultsInText.CloseTokenCard();
                Settle(window);
                var panel = Panel(window);
                var showing = new Dictionary<string, int>(StringComparer.Ordinal)
                { ["All"] = 0, ["Unread"] = 0, ["Different"] = 0, ["Not in FieldWorks"] = 0, ["No parse"] = 0, ["Stopped"] = 0 };
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                foreach (var text in inText.Texts)
                {
                    inText.SelectedText = text;
                    Settle(window);
                    var strips = Strips(panel).ToArray();
                    bool ShowsReading(Border strip) => Part(strip, "pangloss").GetVisualDescendants().OfType<TextBlock>()
                        .Any(text => text.Classes.Contains("morphemePanelForm") && text.IsEffectivelyVisible);
                    bool HoldsNothing(Border strip) =>
                        VisibleText(Part(strip, "fieldworks")).StartsWith("Nothing in FieldWorks", StringComparison.Ordinal);
                    showing["All"] += strips.Length;
                    showing["Unread"] += strips.Count(strip => HasPart(strip, "unread"));
                    showing["Different"] += strips.Count(strip => ShowsReading(strip) && !HoldsNothing(strip));
                    showing["Not in FieldWorks"] += strips.Count(strip => ShowsReading(strip) && HoldsNothing(strip));
                    showing["No parse"] += strips.Count(strip => VisibleText(Part(strip, "pangloss")) == "∅ No parse");
                    showing["Stopped"] += strips.Count(strip =>
                    {
                        var pangloss = Part(strip, "pangloss");
                        return VisibleText(pangloss) == "Stopped" &&
                            pangloss.GetVisualDescendants().OfType<MarkGlyph>().Any(glyph => glyph.Mark == Mark.Stopped);
                    });
                }

                var chips = panel.GetVisualDescendants().OfType<FilterChip>()
                    .Where(chip => chip.IsEffectivelyVisible).ToDictionary(chip => chip.Label!, chip => chip.Count);
                Assert.Equal(showing, chips);
                Assert.Contains(showing, pair => pair.Key != "All" && pair.Key != "Unread" && pair.Value > 0);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    internal static bool HasPart(Border strip, string part) =>
        strip.GetVisualDescendants().OfType<Control>().Any(control =>
            (Avalonia.Automation.AutomationProperties.GetAutomationId(control) ?? string.Empty)
                .EndsWith("-" + part, StringComparison.Ordinal) && control.IsEffectivelyVisible);

    internal static Border StripOf(ResultsInTextPanel panel, string form) =>
        Assert.Single(Strips(panel), strip => strip.Tag is ResultsTokenViewModel token && token.Form == form);

    internal static Control Part(Border strip, string part) =>
        Assert.Single(strip.GetVisualDescendants().OfType<Control>(), control =>
            (Avalonia.Automation.AutomationProperties.GetAutomationId(control) ?? string.Empty)
                .EndsWith("-" + part, StringComparison.Ordinal) && control.IsEffectivelyVisible);

    internal static string VisibleText(Control part) => string.Join(" ",
        part.GetVisualDescendants().Prepend(part).OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible &&
                !text.GetSelfAndVisualAncestors().OfType<Control>().Any(control => control.Opacity <= 0) &&
                !string.IsNullOrWhiteSpace(text.Text))
            .Select(text => text.Text));

    internal static T Named<T>(Visual root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control =>
            Avalonia.Automation.AutomationProperties.GetName(control) == name && control.IsEffectivelyVisible);

    internal static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenAnalyzeTexts(
        int width = 1240, bool parse = true,
        Action<FakeCommandClient, AssessCommandResponse>? configure = null)
    {
        var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: parse, configure: configure);
        window.Width = width;
        window.Height = 780;
        workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
        workspace.CurrentPage = WorkspacePage.Texts;
        Settle(window);
        return (workspace, window);
    }

    internal static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    internal static ResultsInTextPanel Panel(Window window) =>
        Assert.Single(window.GetLogicalDescendants().OfType<ResultsInTextPanel>());

    internal static WordCard OpenCard(Window window) =>
        Assert.Single(Panel(window).GetVisualDescendants().OfType<WordCard>(), card => card.IsEffectivelyVisible);

    internal static Border CardFrame(WordCard card) =>
        Assert.Single(card.GetVisualDescendants().OfType<Border>(), border =>
            border.Classes.Contains("wordPresentationCardFrame"));

    internal static IEnumerable<Border> Strips(ResultsInTextPanel panel) =>
        panel.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Name == "WordStrip" && border.IsEffectivelyVisible);

    internal static Rect BoundsIn(Visual visual, Visual relativeTo)
    {
        var origin = visual.TranslatePoint(new Point(0, 0), relativeTo) ??
            throw new InvalidOperationException("The visual is not under the panel.");
        return new Rect(origin, visual.Bounds.Size);
    }

    private static void AssertNativeMetric(WordStripToken strip, string rowName, double metric)
    {
        var row = strip.FindControl<Control>(rowName)!;
        var nativeHeight = MaxNativeTextHeight(row);
        Assert.True(metric >= nativeHeight,
            $"{strip.Data?.Form} {rowName}: metric={metric}, native={nativeHeight}, row={row.DesiredSize.Height}.");
    }

    private static double MaxNativeTextHeight(Control row) => row.GetVisualDescendants().OfType<TextBlock>()
        .Select(block => block.TextLayout.Height).DefaultIfEmpty(0).Max();

    private static Rect TransformedBoundsIn(Visual visual, Visual relativeTo)
    {
        var corners = new[]
        {
            new Point(0, 0),
            new Point(visual.Bounds.Width, 0),
            new Point(0, visual.Bounds.Height),
            new Point(visual.Bounds.Width, visual.Bounds.Height),
        }.Select(point => visual.TranslatePoint(point, relativeTo) ??
            throw new InvalidOperationException("The visual is not under the panel.")).ToArray();
        var left = corners.Min(point => point.X);
        var top = corners.Min(point => point.Y);
        return new Rect(left, top, corners.Max(point => point.X) - left, corners.Max(point => point.Y) - top);
    }
}
