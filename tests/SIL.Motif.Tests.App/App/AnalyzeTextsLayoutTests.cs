using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
public sealed class AnalyzeTextsLayoutTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

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
                    Assert.True(Application.Current.TryGetResource("Intent.Surface.Raised", variant, out var surface));
                    Assert.Same(surface, card.Background);
                    Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color.A);
                    Assert.True(Application.Current.TryGetResource("Intent.Shadow.Raised", variant, out var shadow));
                    Assert.Equal(shadow, card.BoxShadow);
                    Assert.True(card.BoxShadow.Count > 0);
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
                var panel = Panel(window);
                var card = OpenCard(window);
                var open = Assert.IsType<ResultsTokenViewModel>(card.DataContext);
                Assert.DoesNotContain(panel.GetLogicalDescendants().OfType<Popup>(), popup => popup.IsOpen);

                var cardBounds = BoundsIn(card, panel);
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

                var panel = Panel(window);
                var warningFilter = Assert.Single(panel.GetVisualDescendants().OfType<FilterChip>(),
                    chip => chip.Label == "Named in a warning");
                Assert.Equal(1, warningFilter.Count);
                Assert.Contains("⚠", VisibleText(Assert.Single(Strips(panel), strip =>
                    strip.Tag is ResultsTokenViewModel { Form: "alikula" })));
                var card = OpenCard(window);
                var cardLines = card.GetVisualDescendants().OfType<CopyableTextBlock>().ToArray();
                var exactMessage = Assert.Single(cardLines,
                    line => line.Text == "PanGloss exact warning message");
                Assert.Contains("wordCardHeading", exactMessage.Classes);
                var namedExactly = Assert.Single(cardLines, line => line.Text == "Named exactly");
                Assert.True(Array.IndexOf(cardLines, exactMessage) < Array.IndexOf(cardLines, namedExactly));
                Assert.DoesNotContain(cardLines, line => line.Text == "exact");
                Assert.Contains(cardLines, line => line.Text == "Membership candidates; not confirmed uses");
                Assert.Contains(cardLines, line => line.Text == "Spelling candidates; not confirmed uses");
                Assert.DoesNotContain(cardLines, line => line.Text?.EndsWith(" · membership") == true);
                Assert.DoesNotContain(cardLines, line => line.Text?.EndsWith(" · spelling") == true);
                var cardText = string.Join(" ", cardLines.Select(line => line.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
                Assert.Contains("Subject agreement", cardText);
                Assert.Contains("Not attributed", cardText);
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
            SampleWarning("exact", "PanGloss exact warning message", WarningWordsMatch.Identity,
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
                    border => border.Classes.Contains("wordCard"));
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
    public void TheTextPickerChipsMarkReadAndSelectShareOneRowAboveTheText()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                workspace.PageModel<TextsPageModel>().ResultsInText.CloseTokenCard();
                Settle(window);
                var panel = Panel(window);
                var controls = new List<Control>
                {
                    Named<ComboBox>(panel, "Text to read"),
                    Named<Button>(panel, "Mark read or unread"),
                    Named<Button>(panel, "Select words for actions"),
                };
                var chips = panel.GetVisualDescendants().OfType<FilterChip>()
                    .Where(chip => chip.IsEffectivelyVisible).ToArray();
                Assert.Equal(["All", "Unread", "Differs", "Not in FieldWorks", "No parse", "Stopped"],
                    chips.Select(chip => chip.Label));
                controls.AddRange(chips);

                var picked = Assert.Single(controls[0].GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "Hadithi ya sungura");
                Assert.DoesNotContain(picked.TextLayout.TextLines, line => line.HasCollapsed);

                var bounds = controls.Select(control => BoundsIn(control, panel)).ToArray();
                var rowTop = bounds.Min(rect => rect.Top);
                var rowBottom = bounds.Max(rect => rect.Bottom);
                Assert.True(bounds.Max(rect => rect.Top) < bounds.Min(rect => rect.Bottom),
                    "The controls wrap: " + string.Join(", ", controls.Zip(bounds, (control, rect) =>
                        $"{control.GetType().Name} at {rect}")));
                Assert.True(rowBottom - rowTop <= 40, $"The control row is {rowBottom - rowTop} px deep.");
                var firstWord = Strips(panel).Min(strip => BoundsIn(strip, panel).Top);
                Assert.True(firstWord - rowBottom <= 48, $"The text starts {firstWord - rowBottom} px below the controls.");
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Expander>(), expander =>
                    expander.IsEffectivelyVisible && Equals(expander.Header, "Actions by scope"));
            }
            finally
            {
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
                Assert.Equal("= same", VisibleText(Part(StripOf(panel, "anapenda"), "pangloss")));
                Assert.Equal("∅ No parse", VisibleText(Part(StripOf(panel, "hawajafika"), "pangloss")));
                Assert.Equal("Stopped at the step limit", VisibleText(Part(StripOf(panel, "mwalimu"), "pangloss")));
                Assert.StartsWith("Nothing in FieldWorks", VisibleText(Part(StripOf(panel, "chakula"), "fieldworks")), StringComparison.Ordinal);
                Assert.Equal("ch- 7 akula food", VisibleText(Part(StripOf(panel, "chakula"), "pangloss")));
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "Different from FieldWorks");

                var rowsMinWidth = (double)Application.Current!.FindResource("Component.WordStrip.RowsMinWidth")!;
                foreach (var strip in Strips(panel))
                {
                    var form = ((ResultsTokenViewModel)strip.Tag!).Form;
                    Assert.DoesNotContain(strip.GetVisualDescendants().OfType<Expander>(), _ => true);
                    var fixes = strip.GetVisualDescendants().OfType<Button>().Where(button =>
                        Avalonia.Automation.AutomationProperties.GetName(button) == "Fix actions from the word strip").ToArray();
                    Assert.True(fixes.Length <= 1, $"{form} offers {fixes.Length} Fix controls.");
                    Assert.All(fixes, fix =>
                    {
                        Assert.Equal("Fix ▾", fix.Content);
                        Assert.NotNull(fix.Flyout);
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
                    link => link.Content as string == "Word Analyses ↗");
                Assert.Equal("Open alikula in Word Analyses", Avalonia.Automation.AutomationProperties.GetName(wordLink));
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
    public void ThreeLinesOfTextFitAboveTheFoldAt1240()
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
                var kitabu = BoundsIn(StripOf(panel, "kitabu"), viewer);
                Assert.True(kitabu.Bottom <= viewer.Viewport.Height,
                    $"Line 3 ends at {kitabu.Bottom}, below the {viewer.Viewport.Height} px the reader shows.");
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
                var strips = Strips(panel).ToArray();
                bool ShowsReading(Border strip) => Part(strip, "pangloss").GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Classes.Contains("stripMorphForm") && text.IsEffectivelyVisible);
                bool HoldsNothing(Border strip) =>
                    VisibleText(Part(strip, "fieldworks")).StartsWith("Nothing in FieldWorks", StringComparison.Ordinal);
                var showing = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["All"] = strips.Length,
                    ["Unread"] = strips.Count(strip => HasPart(strip, "unread")),
                    ["Differs"] = strips.Count(strip => ShowsReading(strip) && !HoldsNothing(strip)),
                    ["Not in FieldWorks"] = strips.Count(strip => ShowsReading(strip) && HoldsNothing(strip)),
                    ["No parse"] = strips.Count(strip => VisibleText(Part(strip, "pangloss")) == "∅ No parse"),
                    ["Stopped"] = strips.Count(strip =>
                        VisibleText(Part(strip, "pangloss")) == "Stopped at the step limit"),
                };

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
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
            .Select(text => text.Text));

    internal static T Named<T>(Visual root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control =>
            Avalonia.Automation.AutomationProperties.GetName(control) == name && control.IsEffectivelyVisible);

    internal static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenAnalyzeTexts(
        int width = 1240, bool parse = true)
    {
        var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: parse);
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

    internal static Border OpenCard(Window window) =>
        Assert.Single(Panel(window).GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("wordCard") && border.IsEffectivelyVisible);

    internal static IEnumerable<Border> Strips(ResultsInTextPanel panel) =>
        panel.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Name == "WordStrip" && border.IsEffectivelyVisible);

    internal static Rect BoundsIn(Visual visual, Visual relativeTo)
    {
        var origin = visual.TranslatePoint(new Point(0, 0), relativeTo) ??
            throw new InvalidOperationException("The visual is not under the panel.");
        return new Rect(origin, visual.Bounds.Size);
    }
}
