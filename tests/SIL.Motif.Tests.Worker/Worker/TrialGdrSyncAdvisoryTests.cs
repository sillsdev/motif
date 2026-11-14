using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class TrialGdrSyncAdvisoryTests
{
    private static GdrCheckResponse Check() => new(true, true, [], [], new GdrSyncCounts(0, 0, 0, 0, 0, []));

    [Fact]
    public void ACheckThatThrowsIsLoggedAndLeavesNoResult()
    {
        var original = Console.Error;
        using var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            Assert.Null(TrialJobHandler.GdrSyncAdvisory("job-1", () => throw new InvalidOperationException("boom")));
        }
        finally { Console.SetError(original); }
        Assert.Contains("Trial 'job-1' could not run the advisory GDR sync check", captured.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ACheckThatSucceedsIsReturned() => Assert.NotNull(TrialJobHandler.GdrSyncAdvisory("job-1", Check));
}
