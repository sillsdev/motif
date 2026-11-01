using System.Text;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Commands;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Jobs;

/// <summary>A Trial is queued with one spelling of each word, the spelling an Assess and a Selection store.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class TrialQueuedWordsTests
{
    private const string Composed = "பொன்";
    private readonly string _fwDataPath;

    public TrialQueuedWordsTests(PristineProjectFixture pristine)
    {
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
    }

    [Fact]
    public void ARequestListingBothSpellingsOfAWordQueuesOneNfdWordAndTheReviewNumbersAreComplete()
    {
        var decomposed = Composed.Normalize(NormalizationForm.FormD);
        var queued = Queue("both-spellings", [Composed, decomposed]);

        var words = Assert.IsAssignableFrom<IReadOnlyList<string>>(queued.Words);
        Assert.Equal([decomposed], words);
        var trial = ReviewNumbersComparabilityTests.Record("trial", [CorrectnessFixture.Word(decomposed, true)]);
        Assert.True(ReviewNumbersCommand.Summarize(null, trial, words.Count).EvidenceComplete);
    }

    [Fact]
    public void AComposedWordIsQueuedDecomposedAndOrderIsKept()
    {
        var queued = Queue("composed", ["cat", Composed, "cat"]);

        Assert.Equal(["cat", Composed.Normalize(NormalizationForm.FormD)], queued.Words);
    }

    private TrialJobInput Queue(string draftName, IReadOnlyList<string> words)
    {
        var created = ProposalCommands.New(new NewDraftRequest(_fwDataPath, "1.0", draftName, null));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(_fwDataPath, "1.0", draftName,
            SIL.Motif.Contract.Ids.CanonicalId.Mint().Value, "en", "a gloss")).Succeeded);
        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(_fwDataPath, "1.0",
            created.Value!.ProposalId, Words: words));
        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return TrialJobInput.Parse(new JobRepository(database).Get(queued.Value!.JobId)!.InputJson);
    }
}
