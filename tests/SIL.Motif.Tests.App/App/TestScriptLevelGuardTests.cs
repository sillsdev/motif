using Xunit;

namespace SIL.Motif.Tests.App.App;

public sealed class TestScriptLevelGuardTests
{
    [Fact]
    public void DefaultGateNamesTheSubsetAndAllRunsEveryLevel()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "test.ps1"));

        Assert.Contains("[switch] $All", script, StringComparison.Ordinal);
        Assert.Contains("MOTIF_TEST_LEVELS", script, StringComparison.Ordinal);
        Assert.Contains("TESTINGPLATFORM_TELEMETRY_OPTOUT", script, StringComparison.Ordinal);
        Assert.Contains("Unit,Integration", script, StringComparison.Ordinal);
        Assert.Contains("default run is not a merge gate", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--no-build", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AllPreparesOnlyArtifactsUsedByTheSelectedTests()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "test.ps1"));

        Assert.Contains("tools/Prepare-TestArtifacts.ps1", script, StringComparison.Ordinal);
        Assert.Contains("Test-Path -LiteralPath $prepareTestArtifacts -PathType Leaf", script, StringComparison.Ordinal);
        Assert.Contains("Get-MotifTestArtifactRequirements", script, StringComparison.Ordinal);
        Assert.Contains("$artifactRequirements.PortableWorkerPackage", script, StringComparison.Ordinal);
        Assert.Contains("$artifactRequirements.ExplainedWordCardFixture", script, StringComparison.Ordinal);
        Assert.True(script.IndexOf("Prepare-TestArtifacts.ps1", StringComparison.Ordinal) <
                    script.IndexOf("dotnet test (", StringComparison.Ordinal));
    }

    [Fact]
    public void GateReportsPreparationWaitTestAndWholeRunDurations()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "test.ps1"));

        Assert.Contains("Write-PhaseDuration 'build gate'", script, StringComparison.Ordinal);
        Assert.Contains("Write-PhaseDuration 'offline restore'", script, StringComparison.Ordinal);
        Assert.Contains("Write-PhaseDuration 'test slot wait'", script, StringComparison.Ordinal);
        Assert.Contains("Write-PhaseDuration 'test artifact preparation'", script, StringComparison.Ordinal);
        Assert.Contains("Write-PhaseDuration 'test processes'", script, StringComparison.Ordinal);
        Assert.Contains("Write-PhaseDuration 'whole gate'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TestSummaryClassifiesSkippedResultsAndHandlesAnEmptyTrx()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "test.ps1"));

        foreach (var category in new[] { "platform", "capture", "harness", "parser-absent", "capability", "other" })
            Assert.Contains(category, script, StringComparison.Ordinal);
        Assert.Contains("SelectNodes(\"//*[local-name()='UnitTestResult']\")", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemSelectsOnlyThatLevelWithoutSelectingAll()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "test.ps1"));

        Assert.Contains("[switch] $System", script, StringComparison.Ordinal);
        Assert.Contains("elseif ($System) { 'System' }", script, StringComparison.Ordinal);
        Assert.Contains("-not $System", script, StringComparison.Ordinal);
        Assert.Contains("if ($All -and $System)", script, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Motif.sln"))) return directory.FullName;

        throw new DirectoryNotFoundException("Could not find Motif.sln above the test output directory.");
    }
}
