using Avalonia.Media;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>The exact text a visit must show; every block showing it must fit its box without losing text, and with a direction must flow that way.</summary>
internal sealed record LayoutExpectation(string Text, FlowDirection? Direction = null);

/// <summary>A page inspected on its own, optionally with the Inspector revealed, and the text it must show.</summary>
internal sealed record LayoutVisit(
    WorkspacePage Page, string Scene, bool RevealDetails, IReadOnlyList<LayoutExpectation> Expect,
    Action<WorkspaceShellViewModel>? Select = null);

/// <summary>
/// What one quick-tier sweep shows: the same two words, texts and Proposals
/// as the plain sweep, with the strings that stress a layout swapped in. <paramref name="Tier"/> names the sweep's
/// own findings list.
/// </summary>
internal sealed record LayoutDataSet(
    string Tier, TextWordsResponse Words, AssessCommandResponse Assessment, Action<FakeCommandClient> Configure,
    Func<WorkspaceShellViewModel, Task> Arrange, IReadOnlyList<LayoutVisit>? Visits = null)
{
    /// <summary>Ticks every listed word, since a row's checked state changes its layout.</summary>
    internal static Task TickEveryRow(WorkspaceShellViewModel workspace)
    {
        var listed = workspace.PageModel<TextsPageModel>().Words.ProjectWords;
        Assert.NotEmpty(listed);
        foreach (var word in listed) word.IsChecked = true;
        return Task.CompletedTask;
    }

    private const string FirstForm = "hawajafika";
    private const string SecondForm = "kitabu";
    private const string ArabicFirst = WritingSystemTestData.Form;
    private const string ArabicSecond = "كِتَاب";

    internal static LayoutDataSet Plain() => new("quick", LayoutInvariantFixtures.QuickWords(),
        LayoutInvariantFixtures.QuickAssessment(), LayoutInvariantFixtures.ConfigurePopulated, TickEveryRow);

    /// <summary>Both words, one text title and the project's default vernacular are Arabic script.</summary>
    internal static LayoutDataSet RightToLeft()
    {
        var tag = SeededProject.RightToLeftTag;
        // Words with no tag of their own take the default vernacular.
        var arabic = WritingSystemTestData.Arabic with
        {
            IsDefault = true,
            FontFamily = WalkthroughFonts.DejaVuSansFamily,
            StyleFonts = new Dictionary<string, WritingSystemStyleFont>(StringComparer.Ordinal)
            {
                ["Normal"] = new(WalkthroughFonts.DejaVuSansFamily, ""),
                ["Paragraph"] = new(WalkthroughFonts.DejaVuSansFamily, ""),
            },
        };
        string Rename(string text) => text.Replace(FirstForm, ArabicFirst).Replace(SecondForm, ArabicSecond);
        bool IsArabicTitle(string title) => title == "Hadithi ya sungura";
        string Title(string title) => IsArabicTitle(title) ? WritingSystemTestData.TextTitle : title;
        string? TitleTag(string title) => IsArabicTitle(title) ? tag : null;
        TextToken Token(TextToken token) => token.Form is FirstForm or SecondForm
            ? token with { Text = Rename(token.Text), Form = Rename(token.Form!), TextWritingSystem = tag, FormWritingSystem = tag }
            : token;
        var words = LayoutInvariantFixtures.QuickWords();
        words = words with
        {
            Words = [.. words.Words.Select(word => word with
            {
                Form = Rename(word.Form), FormWritingSystem = tag,
                Occurrences = [.. word.Occurrences.Select(occurrence => occurrence with
                {
                    TextTitle = Title(occurrence.TextTitle), Sentence = Rename(occurrence.Sentence),
                    TextTitleWritingSystem = TitleTag(occurrence.TextTitle),
                })],
            })],
            Texts = [.. words.Texts.Select(text => text with
            {
                Title = Title(text.Title), TitleWritingSystem = TitleTag(text.Title),
                Lines = [.. text.Lines.Select(line => line with
                {
                    SentenceWritingSystem = tag, Tokens = [.. line.Tokens.Select(Token)],
                })],
            })],
        };
        var assessment = LayoutInvariantFixtures.QuickAssessment();
        assessment = assessment with
        {
            Words = [.. assessment.Words.Select(word => word with
            {
                Word = Rename(word.Word), WordWritingSystem = tag,
                Morphology = word.Morphology is { } evidence ? evidence with { Word = Rename(evidence.Word) } : null,
            })],
        };
        return new("quick-rtl", words, assessment, fake =>
        {
            LayoutInvariantFixtures.ConfigurePopulated(fake);
            fake.OverviewCompletesWith(SampleEvidence.Overview(assessment) with
            {
                WritingSystems = [WritingSystemTestData.English with { IsDefault = false }, arabic],
            });
            var snapshot = ReviewScreenshots.Blocked();
            fake.PendingChangesIs(snapshot with
            {
                Changes = [.. snapshot.Changes.Select(change => change.Word != SecondForm ? change : change with
                {
                    Word = ArabicSecond, DisplayReading = ArabicSecond, WordWritingSystem = tag,
                    Analyses = [new ReviewAnalysis(new ParserReading(
                    [
                        new ParserReadingMorph("كِ-", "7", "n", null, false, null) { FormWritingSystem = tag },
                        new ParserReadingMorph("تَاب", "book", "n", null, false, null) { FormWritingSystem = tag },
                    ]), ReadingGrade.Candidate, true, true)],
                })],
            });
        }, workspace =>
        {
            WalkthroughFonts.Register();
            workspace.Context.TextStyles.ReplaceContextIfChanged([WritingSystemTestData.English with { IsDefault = false }, arabic]);
            return TickEveryRow(workspace);
        }, [
            new(WorkspacePage.Words, "Words/populated", false,
                [new(ArabicFirst, FlowDirection.RightToLeft), new(ArabicSecond, FlowDirection.RightToLeft)]),
            new(WorkspacePage.Texts, "Texts/populated", false,
                [new(ArabicFirst, FlowDirection.RightToLeft)]),
        ]);
    }

    /// <summary>Long Proposal and Grammar Guide titles remain readable at narrow sizes.</summary>
    internal static LayoutDataSet Long()
    {
        const string name = "Sound change for the second-person plural subject prefix before vowel-initial verb stems in the northern dialect";
        const string guide = "Subject prefixes before vowel-initial verb stems in the northern dialect";
        return Plain() with
        {
            Tier = "quick-long",
            Visits =
            [
                new(WorkspacePage.Proposals, "Proposals/populated", false, [new(name)]),
                new(WorkspacePage.GrammarGuide, "GrammarGuide/populated", false, [new(guide)]),
            ],
            Configure = fake =>
            {
                ProposalsPageScreenshots.Configure(fake, name);
                fake.PendingChangesIs(ReviewScreenshots.Blocked());
                fake.GrammarGuideListHandler = (_, _) => Task.FromResult(CommandOutcome<GdrListResponse>.Success(
                    new(true, [new GdrSummary("gdr/1", "1", guide, "")])));
            },
            Arrange = TickEveryRow,
        };
    }
}
