using SIL.LCModel;
using SIL.Motif.Host.LcmUtils;
using Xunit;

namespace SIL.Motif.Tests.Host;

public sealed class HeldProjectRetryTests
{
    private const string Path = "/tmp/never-opened/Proj/Proj.fwdata";

    private static LcmInitializationException Violation() => new(
        "The process cannot access the file 'Proj.fwdata' because it is being used by another process.");

    [Fact]
    public void ASharingViolationThatClearsBeforeTheProbeIsRetriedAndThenOpens()
    {
        var attempts = 0;
        var sleeps = 0;
        var opened = FwDataProjectLoader.RetryWhileHeld(Path, () =>
        {
            if (++attempts < 3) throw Violation();
            return "cache";
        }, _ => sleeps++);

        Assert.Equal("cache", opened);
        Assert.Equal(3, attempts);
        Assert.Equal(2, sleeps);
    }

    [Fact]
    public void ASharingViolationThatLastsIsTheInUseRefusalNeverAnUnreadableProject()
    {
        var attempts = 0;
        var slept = 0;

        Assert.Throws<ProjectFileHeldException>(() => FwDataProjectLoader.RetryWhileHeld<string>(
            Path, () => { attempts++; throw Violation(); }, ms => slept += ms));

        Assert.InRange(attempts, 5, 10);
        Assert.True(slept <= 1000, $"waited {slept} ms");
    }

    [Fact]
    public void AnUnrelatedInitializationFailureIsNotRetried()
    {
        var attempts = 0;

        Assert.Throws<LcmInitializationException>(() => FwDataProjectLoader.RetryWhileHeld<string>(
            Path, () => { attempts++; throw new LcmInitializationException("The file is damaged."); }, _ => { }));

        Assert.Equal(1, attempts);
    }
}
