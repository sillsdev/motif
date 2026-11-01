using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Trait("MotifTestLevel", "Unit")]
public sealed class ProposalDifferenceRenderingTests
{
    private static ProposalDifferenceSummary Summary()
    {
        var pin = new ProposalDifferenceSidePin("1.0.0", "sha256:p", "sha256:g", "selection", "sha256:s", "tokeniser", "1");
        return new("difference/1", "proposal/1", "job/1", "assessment/b", "assessment/a", "text/1", "Title", "baseline/1",
            new ProposalDifferencePolicy(), 2, 3, 0, 1, 0, 0, [], [], "parsimony", null, pin, pin,
            ["The parser differs: 1.0.0 against 1.0.1."], 2, [new(HumanAgreementLimitation.Capped, 1, 2)],
            "2026-10-31T00:00:00Z");
    }

    [Fact]
    public void TheRenderingShowsWarningsOutsideTextWordsAndLimitationCounts()
    {
        var response = new ProposalDifferenceResponse(Summary(), [], 0, null, true);

        var rendered = CommandTextRenderer.Render(CommandOutcome<ProposalDifferenceResponse>.Success(response), asJson: false);

        Assert.Contains("Warning: The parser differs: 1.0.0 against 1.0.1.", rendered.Output);
        Assert.Contains("2 measured words are not in this Text", rendered.Output);
        Assert.Contains("Limit capped: 1 word, 2 occurrences", rendered.Output);
        Assert.Contains("Showing 0 of 0 matching words", rendered.Output);
    }

    [Fact]
    public void ADecidedProposalsDifferenceSaysWordDetailIsNoLongerKept()
    {
        var response = new ProposalDifferenceResponse(Summary(), [], 0, null, false);

        var rendered = CommandTextRenderer.Render(CommandOutcome<ProposalDifferenceResponse>.Success(response), asJson: false);

        Assert.Contains("Word detail is no longer kept", rendered.Output);
        Assert.DoesNotContain("Showing", rendered.Output);
    }

    private static ProposalDifferenceSummary Cut(int words, int listed) => Summary() with
    {
        Words = words,
        WordsListed = listed,
        Buckets = [new(ProposalDifferenceBucket.Regression, words, words, listed)],
        Matrix = [new("no-parse", "approved-unique", words, words, listed)],
        Limitations = [new(HumanAgreementLimitation.Capped, words, words, listed)],
    };

    [Fact]
    public void AKeptListSaysHowManyWordsAreListedAndHowManyAreOnlyCounted()
    {
        var response = new ProposalDifferenceResponse(Cut(900, 100), [], 0, null, true);

        var output = CommandTextRenderer.Render(CommandOutcome<ProposalDifferenceResponse>.Success(response), asJson: false).Output;

        Assert.Contains("100 of 900 words are listed and 800 are counted but not listed", output);
        Assert.Contains("(100 listed, 800 counted but not listed)", output);
        Assert.Contains("best 100", output);
    }

    [Fact]
    public void OneWordThatIsOnlyCountedReadsInTheSingular()
    {
        var response = new ProposalDifferenceResponse(Cut(2, 1), [], 0, null, true);

        var output = CommandTextRenderer.Render(CommandOutcome<ProposalDifferenceResponse>.Success(response), asJson: false).Output;

        Assert.Contains("1 of 2 words are listed and 1 is counted but not listed", output);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void NoWordsAreSaidToBeListedWhenTheWordDetailIsGoneOrCannotBeRead(bool unreadable, bool _)
    {
        // Decided and deferred Proposals keep no words; a damaged Difference lists none either.
        var response = new ProposalDifferenceResponse(Cut(900, 100), [], 0, null, false, WordDetailUnreadable: unreadable);

        var output = CommandTextRenderer.Render(CommandOutcome<ProposalDifferenceResponse>.Success(response), asJson: false).Output;

        Assert.DoesNotContain("are listed", output);
        Assert.DoesNotContain("counted but not listed", output);
        Assert.DoesNotContain(" listed,", output);
    }

    [Fact]
    public void ASourceValidatedWordSaysWhichTierAndLevelItsDecisionsRestOnAndUnreadableRevisionsAreNamed()
    {
        var facts = new ProposalDifferenceCaseFacts(new ProposalDifferencePolicy(),
            Side(), Side(), true, [], [], [], [], [], []);
        var word = new ProposalDifferenceWord("sita", 1, "approved-unique", "no-approved-match", [],
            [ProposalDifferenceBucket.Regression], facts,
            [new("wordform/1", "wordform", "misspelled-typo"),
             new("analysis/1", "analysis", "source-approved", "Source validation", "approves")]);
        var response = new ProposalDifferenceResponse(Summary(), [word], 1, null, true, null,
            [new("proposal/9", "Stage sita", "The stored revision could not be read.")]);

        var rendered = CommandTextRenderer.Render(CommandOutcome<ProposalDifferenceResponse>.Success(response), asJson: false);

        Assert.Contains("sita: source-validated misspelled-typo for wordform wordform/1", rendered.Output);
        Assert.Contains("sita: source-validated source-approved for analysis analysis/1", rendered.Output);
        Assert.Contains("Not counted: staged decisions of Applied Proposal proposal/9 'Stage sita' could not be read",
            rendered.Output);
    }

    private static HumanAgreementFacts Side() =>
        new(HumanAgreementEvidenceAvailability.Available, HumanAgreementSearchCompletion.Complete, [],
            HumanAgreementApprovalState.ApprovedUnique, 1, 1, 1, [], [], [], [], [], [], [],
            HumanAgreementSpellingAcceptance.NotApplicable, []);
}
