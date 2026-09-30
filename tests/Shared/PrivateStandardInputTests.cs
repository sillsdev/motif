using Xunit;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Proves this test process reads the standard input file the test script made for it alone.</summary>
public sealed class PrivateStandardInputTests
{
    [TestScriptFact]
    public void ThisProcessReadsTheStandardInputFileTheTestScriptMadeForIt() =>
        Assert.Equal(TestScriptStandardInput.ExpectedToken, TestScriptStandardInput.ReadIfFile()?.Trim());
}
