using Avalonia.Media;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
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
/// What one quick-tier sweep shows: the same two words, texts, Review changes, Proposals and Parsimony findings
/// as the plain sweep, with the strings that stress a layout swapped in. <paramref name="Tier"/> names the sweep's
/// own findings list.
/// </summary>
internal sealed record LayoutDataSet(
    string Tier, TextWordsResponse Words, AssessCommandResponse Assessment, Action<FakeCommandClient> Configure,
    Func<WorkspaceShellViewModel, Task> Arrange, IReadOnlyList<LayoutVisit>? Visits = null)
{
    private const string FirstForm = "hawajafika";
    private const string SecondForm = "kitabu";
    private const string ArabicFirst = WritingSystemTestData.Form;
    private const string ArabicSecond = "كِتَاب";

    internal static LayoutDataSet Plain() => new("quick", LayoutInvariantFixtures.QuickWords(),
        LayoutInvariantFixtures.QuickAssessment(), LayoutInvariantFixtures.ConfigurePopulated, _ => Task.CompletedTask);

    /// <summary>Both words, one text title, one Review change and the project's default vernacular are Arabic script.</summary>
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
                Lines = [.. text.Lines.Select(line => line with { Tokens = [.. line.Tokens.Select(Token)] })],
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
            var overview = workspace.PageModel<OverviewPageModel>();
            overview.Overview = overview.Overview! with { WritingSystems = [WritingSystemTestData.English with { IsDefault = false }, arabic] };
            return Task.CompletedTask;
        }, [
            new(WorkspacePage.Overview, "Overview/populated", false,
                [new(ArabicFirst, FlowDirection.RightToLeft), new(ArabicSecond, FlowDirection.RightToLeft)]),
            new(WorkspacePage.Review, "Review/populated", false, [new(ArabicSecond, FlowDirection.RightToLeft)]),
            new(WorkspacePage.Review, "Review/details-shown", true, [new("تَاب", FlowDirection.RightToLeft)], workspace =>
            {
                var review = workspace.PageModel<ReviewPageModel>();
                review.SelectEntry(Assert.Single(review.Entries, entry => entry.Change?.Word == ArabicSecond));
            }),
        ]);
    }

    /// <summary>One Proposal name of about a hundred and twenty characters and one Parsimony limitation of about three hundred.</summary>
    internal static LayoutDataSet Long()
    {
        const string name = "Sound change for the second-person plural subject prefix before vowel-initial verb stems in the northern dialect";
        const string advice = "This count covers only the prohibitions written in this grammar, so a restriction that the parser " +
            "enforces through a feature, a natural class or a rule ordering is not in it, and the paired parser check that would " +
            "show whether the named forms are still blocked has not been run for this project, which makes the share a floor.";
        return Plain() with
        {
            Tier = "quick-long",
            Visits =
            [
                new(WorkspacePage.Proposals, "Proposals/populated", false, [new(name)]),
                new(WorkspacePage.Parsimony, "Parsimony/details-shown", true, [new(advice)]),
            ],
            Configure = fake =>
            {
                ParsimonyStateScreenshots.Configure(fake, "listed-evidence", report => report with
                {
                    Findings = [.. report.Findings.Select(finding => finding with { Limitations = [advice] })],
                });
                ProposalsPageScreenshots.Configure(fake, name);
                fake.PendingChangesIs(ReviewScreenshots.Blocked());
            },
            Arrange = workspace =>
            {
                // The advice shows in the Inspector for a selected finding.
                var page = workspace.PageModel<ParsimonyPageModel>();
                Assert.True(page.SelectFinding(page.Rows.First(row => !row.IsHeader).Link!), page.SelectionMessage);
                return Task.CompletedTask;
            },
        };
    }
}
