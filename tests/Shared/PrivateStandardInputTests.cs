using Xunit;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Proves this test process reads the private standard input supplied by the test script.</summary>
public sealed class PrivateStandardInputTests
{
    [TestScriptFact]
    public async Task ThisProcessReadsThePrivateStandardInputTheTestScriptMadeForIt()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var input = await TestScriptStandardInput.ReadPrivateInputAsync(cancellation.Token);
        Assert.Equal(TestScriptStandardInput.ExpectedToken, input?.Trim());
    }
}
