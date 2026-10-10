using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class TrialStandingWordsTests
{
    [Fact]
    public void EachStandingHasTheWindowsSentence()
    {
        Assert.Equal("Not tried yet. Try it to see what it does to the numbers.",
            TrialStandingWords.For(TrialStanding.NotTried).Sentence);
        Assert.Equal("Trying…", TrialStandingWords.For(TrialStanding.Trying).Sentence);
        Assert.Equal("The last try did not finish. Try again.", TrialStandingWords.For(TrialStanding.Failed).Sentence);
        Assert.Equal("Current", TrialStandingWords.For(TrialStanding.Current).Sentence);
        Assert.Equal("FieldWorks changed since this was tried. Try again.",
            TrialStandingWords.For(TrialStanding.NeedsAnotherTry(TrialStaleReason.BaselineMoved)).Sentence);
        Assert.Equal("This Proposal changed since it was tried. Try again.",
            TrialStandingWords.For(TrialStanding.NeedsAnotherTry(TrialStaleReason.ProposalChanged)).Sentence);
    }

    [Fact]
    public void BothStaleReasonsShareOneStatusAndOnlyAnUntriedProposalIsTriedForTheFirstTime()
    {
        Assert.Equal("Needs another try",
            TrialStandingWords.For(TrialStanding.NeedsAnotherTry(TrialStaleReason.BaselineMoved)).Status);
        Assert.Equal("Needs another try",
            TrialStandingWords.For(TrialStanding.NeedsAnotherTry(TrialStaleReason.ProposalChanged)).Status);
        Assert.Equal("Try it", TrialStandingWords.For(TrialStanding.NotTried).Action);
        Assert.All(new[] { TrialStanding.Trying, TrialStanding.Failed, TrialStanding.Current },
            standing => Assert.Equal("Try again", TrialStandingWords.For(standing).Action));
    }

    [Fact]
    public void NoStandingSentenceUsesAWordTheWindowKeepsOffScreen()
    {
        var standings = Enum.GetValues<TrialStandingKind>().SelectMany(kind => kind == TrialStandingKind.NeedsAnotherTry
            ? Enum.GetValues<TrialStaleReason>().Select(TrialStanding.NeedsAnotherTry)
            : [new TrialStanding(kind)]);
        foreach (var words in standings.Select(TrialStandingWords.For))
            foreach (var banned in new[] { "Trial", "Draft", "Baseline", "revision", "Preflight" })
                Assert.DoesNotContain(banned, words.Status + " " + words.Sentence + " " + words.Action,
                    StringComparison.OrdinalIgnoreCase);
    }
}
