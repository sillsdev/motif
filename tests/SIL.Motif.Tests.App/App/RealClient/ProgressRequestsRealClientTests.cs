using SIL.Motif.Commands.Requests;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class ProgressRequestsRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task TheOpenRequestCountIncludesRecordsBeyondTheBoundedListAndExcludesWithdrawnOnes()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        var callsBeforeRead = project.Invocations().ToArray();
        var createdIds = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            var created = await project.Client.CreateRequestAsync(new CreateRequestRequest(
                project.FwDataPath, MotifProductVersion.CurrentText, "word", "word", "word"), CancellationToken.None);
            Assert.True(created.Succeeded, created.Refusal?.Message);
            createdIds.Add(created.Value!.Request.RequestId);
        }
        var read = await project.Client.ReadProgressOpenRequestCountAsync(project.FwDataPath, CancellationToken.None);
        Assert.True(read.Succeeded, read.Refusal?.Message);
        Assert.Equal(3, read.Value!.OpenCount);
        var withdrawn = await project.Client.WithdrawRequestAsync(new WithdrawRequestRequest(
            project.FwDataPath, MotifProductVersion.CurrentText, createdIds[0]), CancellationToken.None);
        Assert.True(withdrawn.Succeeded, withdrawn.Refusal?.Message);
        read = await project.Client.ReadProgressOpenRequestCountAsync(project.FwDataPath, CancellationToken.None);
        Assert.True(read.Succeeded, read.Refusal?.Message);
        Assert.Equal(2, read.Value!.OpenCount);
        Assert.Equal(callsBeforeRead, project.Invocations());
    }
}
