using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class ParsimonyEnvironmentAndClassMeasureTests
{
    [Fact]
    public void EnvironmentAndNaturalClassMeasuresAreExecutable()
    {
        Assert.True(MeasureRunner.Supports("R-env-broad"));
        Assert.True(MeasureRunner.Supports("R-nc-excess"));
    }
}
