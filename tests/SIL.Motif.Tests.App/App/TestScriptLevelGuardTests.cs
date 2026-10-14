using Xunit;
using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Host.Store;

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
    public void ReleasePathwaysSetsItsPrivateRootBeforeSeedingProjects()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "release-pathways.ps1"));
        var rootCreation = script.IndexOf("$releaseRoot = Join-Path", StringComparison.Ordinal);
        var workerRoot = script.IndexOf("$env:MOTIF_WORKER_ROOT = Join-Path $releaseRoot", StringComparison.Ordinal);
        var firstSeed = script.IndexOf("Seed-Selection $build.ProjectPath", StringComparison.Ordinal);

        Assert.True(rootCreation >= 0);
        Assert.True(workerRoot >= 0);
        Assert.True(firstSeed >= 0);
        Assert.True(rootCreation < workerRoot);
        Assert.True(workerRoot < firstSeed);
    }

    [Fact]
    public void ReleasePathwayChildrenInheritThePrivateRootBeforeAnyFixtureIsSeeded()
    {
        var root = Path.Combine(Path.GetTempPath(), "MotifReleaseIsolation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "release-pathways.ps1"));
            var beforeSeeding = source[..source.IndexOf("$projectPaths =", StringComparison.Ordinal)];
            var script = Path.Combine(root, "probe.ps1");
            File.WriteAllText(script, beforeSeeding + """
                try {
                    $pwsh = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
                    $child = Invoke-Process -Executable $pwsh -Arguments @('-NoProfile', '-Command', '$env:MOTIF_WORKER_ROOT')
                    $project = Join-Path $work 'probe.fwdata'
                    Set-Content -LiteralPath $project -Value ''
                    [void] (Invoke-Process -Executable $script:CliPath -Arguments @('overview', '--project', $project, '--json') -AllowFailure)
                    @{ ChildRoot = $child.Output.Trim(); ExpectedRoot = Join-Path $releaseRoot 'launch-worker'; Project = $project } |
                        ConvertTo-Json -Compress
                }
                finally { Set-Content -LiteralPath (Join-Path $work 'release-root.txt') -Value $releaseRoot }
                """);
            var start = new ProcessStartInfo("pwsh");
            foreach (var argument in new[] { "-NoProfile", "-File", script,
                         "-AppExecutable", script, "-CliExecutable", BuildOutput.Cli, "-SampleBuilder", script,
                         "-SampleSpec", script, "-WalkthroughDirectory", root, "-WorkDirectory", root })
                start.ArgumentList.Add(argument);
            var sentinel = Path.Combine(root, "parent-worker");
            start.Environment["MOTIF_WORKER_ROOT"] = sentinel;
            var result = ToolProcess.Run(start);
            Assert.True(result.ExitCode == 0, result.Error);
            using var json = JsonDocument.Parse(result.Output);
            var actual = json.RootElement.GetProperty("ChildRoot").GetString();
            Assert.Equal(json.RootElement.GetProperty("ExpectedRoot").GetString(), actual);
            Assert.NotEqual(sentinel, actual);
            Assert.Contains("MotifReleaseCheck-", actual!);
            Assert.True(File.Exists(Path.Combine(actual!, "motif.db")));
            using var machine = MachineDatabase.Open(actual!);
            var recorded = Assert.Single(new KnownProjectRegistry(machine).List());
            Assert.Equal(json.RootElement.GetProperty("Project").GetString(), recorded.FullFwDataPath);
            Assert.False(File.Exists(Path.Combine(sentinel, "motif.db")));
        }
        finally
        {
            var rootFile = Path.Combine(root, "release-root.txt");
            if (File.Exists(rootFile))
            {
                var releaseRoot = File.ReadAllText(rootFile).Trim();
                Assert.StartsWith(Path.Combine(Path.GetTempPath(), "MotifReleaseCheck-"), releaseRoot);
                Directory.Delete(releaseRoot, recursive: true);
            }
            Directory.Delete(root, recursive: true);
        }
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
