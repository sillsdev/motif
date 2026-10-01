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
        var repositoryRoot = Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));
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
}
