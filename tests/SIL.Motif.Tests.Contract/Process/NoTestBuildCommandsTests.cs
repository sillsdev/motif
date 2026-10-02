using System.Text.RegularExpressions;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Contract.Process;

public sealed class NoTestBuildCommandsTests
{
    private static readonly Regex DotnetStart = new(
        "(?:new\\s+ProcessStartInfo\\s*\\(\\s*@?\"dotnet(?:\\.exe)?\"\\s*[,)]|Process\\.Start\\s*\\(\\s*@?\"dotnet(?:\\.exe)?\"\\s*,)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CommandToken = new(
        "[@?\"'](?<verb>build|publish|run)(?=[\\s\"'])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void TestSourcesDoNotStartDotnetBuildPublishOrRun()
    {
        var repositoryRoot = RepositoryRoot();
        var testsRoot = Path.Combine(repositoryRoot, "tests");
        var violations = Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("NoTestBuildCommandsTests.cs", StringComparison.Ordinal))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => FindForbiddenCommands(File.ReadAllText(path))
                .Select(verb => Path.GetRelativePath(repositoryRoot, path) + ": dotnet " + verb))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void BuildGateDoesNotPrepareTestArtifacts()
    {
        var buildScript = File.ReadAllText(Path.Combine(RepositoryRoot(), "build.ps1"));

        Assert.DoesNotContain("Prepare-TestArtifacts.ps1", buildScript, StringComparison.Ordinal);
    }

    [Fact]
    public void TestGatePreparesArtifactsRequiredByTheSelectedTests()
    {
        var testScript = File.ReadAllText(Path.Combine(RepositoryRoot(), "test.ps1"));
        var preparation = Regex.Match(testScript,
            @"(?<body>\$phaseClocks\['test artifact preparation'\]\s*=\s*[\s\S]*?Write-PhaseDuration 'test artifact preparation')",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        Assert.True(preparation.Success, "test.ps1 must prepare selected artifacts before launching tests.");
        var body = preparation.Groups["body"].Value;
        Assert.Contains("if ($All -or $System)", body, StringComparison.Ordinal);
        Assert.Contains("$selectedProjectNames = @($testRuns | Select-Object -ExpandProperty Project -Unique)", body,
            StringComparison.Ordinal);
        Assert.Contains("Get-MotifTestArtifactRequirements -All:$All -System:$System", body,
            StringComparison.Ordinal);
        Assert.Contains("-SelectedProjects $selectedProjectNames -Filter $Filter", body, StringComparison.Ordinal);
        Assert.Contains("$artifactRequirements.PortableWorkerPackage", body, StringComparison.Ordinal);
        Assert.Contains("$artifactRequirements.ExplainedWordCardFixture", body, StringComparison.Ordinal);
        Assert.Contains("if ($preparePortable -or $prepareExplainedWordCard)", body, StringComparison.Ordinal);
        Assert.Contains("Prepare-TestArtifacts.ps1", body, StringComparison.Ordinal);
        Assert.Contains("if (-not (Test-Path -LiteralPath $prepareTestArtifacts -PathType Leaf))", body,
            StringComparison.Ordinal);
        Assert.Contains("throw \"Required test artifact preparation script is missing: $prepareTestArtifacts\"",
            body, StringComparison.Ordinal);
        Assert.Contains("PortableWorkerPackage = [bool] $preparePortable", body, StringComparison.Ordinal);
        Assert.Contains("ExplainedWordCardFixture = [bool] $prepareExplainedWordCard", body,
            StringComparison.Ordinal);
        Assert.Contains("& $prepareTestArtifacts @prepareParameters", body, StringComparison.Ordinal);
        Assert.Contains("[switch] $All", testScript, StringComparison.Ordinal);
        Assert.DoesNotContain("if ([string]::IsNullOrWhiteSpace($levels))", testScript, StringComparison.Ordinal);
        var planRegression = testScript.IndexOf("Test-MotifTestRunPlan.Tests.ps1", StringComparison.Ordinal);
        var preparationCompleted = testScript.IndexOf("Write-PhaseDuration 'test artifact preparation'",
            StringComparison.Ordinal);
        var missingPreparerFailure = body.IndexOf("throw \"Required test artifact preparation script is missing:",
            StringComparison.Ordinal);
        var preparerInvocation = body.IndexOf("& $prepareTestArtifacts @prepareParameters", StringComparison.Ordinal);
        var testLaunch = testScript.IndexOf("Write-Step (\"dotnet test (", StringComparison.Ordinal);
        Assert.True(planRegression >= 0 && planRegression < preparationCompleted,
            "The gate must check artifact-selection rules before preparation.");
        Assert.True(missingPreparerFailure >= 0 && preparerInvocation > missingPreparerFailure,
            "The gate must fail when required artifact preparation is unavailable.");
        Assert.True(preparationCompleted >= 0 && testLaunch > preparationCompleted,
            "The gate must complete artifact preparation before launching tests.");
    }

    [Fact]
    public void ArtifactDependentTestsAreSystemLevel()
    {
        var repositoryRoot = RepositoryRoot();
        var portablePackage = File.ReadAllText(Path.Combine(repositoryRoot, "tests", "SIL.Motif.Tests.Cli",
            "Integration", "PortableWorkerPackageTests.cs"));
        var explainedWordCard = File.ReadAllText(Path.Combine(repositoryRoot, "tests", "SIL.Motif.Tests.App",
            "App", "Walkthrough", "WalkthroughReplayTests.cs"));

        Assert.Contains("[Trait(\"MotifTestLevel\", \"System\")]", portablePackage, StringComparison.Ordinal);
        Assert.Contains("[Trait(\"MotifTestLevel\", \"System\")]", explainedWordCard, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingPreparedArtifactGuidanceNamesPreparationStep()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "motif-missing-prepared-" + Guid.NewGuid().ToString("N"));
        var directoryFailure = Assert.Throws<DirectoryNotFoundException>(() =>
            BuildOutput.RequirePreparedDirectory(missingPath, "Test directory"));
        var fileFailure = Assert.Throws<FileNotFoundException>(() =>
            BuildOutput.RequirePreparedFile(missingPath, "Test file"));

        Assert.Contains("Run ./tools/Prepare-TestArtifacts.ps1", directoryFailure.Message, StringComparison.Ordinal);
        Assert.Contains("Run ./tools/Prepare-TestArtifacts.ps1", fileFailure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("build", "new ProcessStartInfo(\"dotnet\"); start.ArgumentList.Add(\"build\");")]
    [InlineData("publish", "new ProcessStartInfo(\"dotnet\", \"publish app.csproj\");")]
    [InlineData("run", "Process.Start(\"dotnet\", \"run --project app.csproj\");")]
    public void GuardFindsEachForbiddenVerb(string verb, string source)
    {
        Assert.Equal(new[] { verb }, FindForbiddenCommands(source));
    }

    [Fact]
    public void DotnetTestProcessUsesOnlyTheTestVerb()
    {
        var start = DotnetTestProcess.CreateTestStartInfo("project.csproj", "FullyQualifiedName=Test");

        Assert.Equal("dotnet", start.FileName);
        Assert.Equal("test", start.ArgumentList[0]);
        Assert.Equal("project.csproj", start.ArgumentList[1]);
        Assert.False(start.Environment.ContainsKey("ICU_DATA"));
        foreach (var name in new[] { "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64" })
        {
            var forwarded = start.Environment.TryGetValue(name, out var value) ? value : null;
            Assert.Equal(Environment.GetEnvironmentVariable(name), forwarded);
        }
    }

    private static string[] FindForbiddenCommands(string source)
    {
        var violations = new List<string>();
        foreach (Match start in DotnetStart.Matches(source))
        {
            var length = Math.Min(1000, source.Length - start.Index);
            var launch = source.Substring(start.Index, length);
            violations.AddRange(CommandToken.Matches(launch)
                .Select(match => match.Groups["verb"].Value.ToLowerInvariant()));
        }
        return violations.ToArray();
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));
}
