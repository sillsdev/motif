using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using Xunit;

namespace SIL.Motif.Tests.Jobs;

public sealed class TrialWordInputTests
{
    [Theory]
    [InlineData(" ")]
    [InlineData(" padded ")]
    public void ATrialRejectsWordsTheParserWouldDropOrAlter(string word)
    {
        var outcome = JobCommands.EnqueueTrial(new EnqueueTrialRequest(
            "absent.fwdata", "1.0", "draft/one", Words: [word]));

        Assert.Equal("job.invalid-words", outcome.Refusal?.Code);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(" padded ")]
    public void AdmittedTrialRejectsInvalidWordsBeforeAccessingProject(string word)
    {
        var outcome = JobCommands.EnqueueTrialAdmitted(new EnqueueTrialRequest(
            "absent.fwdata", "1.0", "draft/one", Words: [word]), null, null);
        Assert.Equal("job.invalid-words", outcome.Refusal?.Code);
    }
}
