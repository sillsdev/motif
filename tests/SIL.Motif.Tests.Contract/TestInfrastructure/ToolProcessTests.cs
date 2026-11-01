using System.Diagnostics;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.TestInfrastructure;

public sealed class ToolProcessTests
{
    // git's name for the empty blob: what `hash-object --stdin` prints for a standard input with nothing in it.
    private const string EmptyBlob = "e69de29bb2d1d6434b8b29ae775ad8c2e48c5391";

    [Fact]
    public void AToolReadsAnEmptyStandardInputOfItsOwnRatherThanTheTestProcesss()
    {
        var start = new ProcessStartInfo("git");
        start.ArgumentList.Add("hash-object");
        start.ArgumentList.Add("--stdin");

        var result = ToolProcess.Run(start);

        Assert.True(result.ExitCode == 0, $"git hash-object exited {result.ExitCode}: {result.Error}");
        Assert.Equal(EmptyBlob, result.Output.Trim());
    }

    [Fact]
    public void GitActsOnItsOwnWorkingDirectoryNotOnARepositoryNamedByTheInheritedEnvironment()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-toolprocess-" + Guid.NewGuid().ToString("N"));
        var decoy = Path.Combine(root, "decoy");
        var own = Path.Combine(root, "own");
        Directory.CreateDirectory(decoy);
        Directory.CreateDirectory(own);
        try
        {
            Assert.Equal(0, ToolProcess.Run(Git(decoy, "init", "-q")).ExitCode);
            var decoyHead = File.ReadAllText(Path.Combine(decoy, ".git", "HEAD"));
            var decoyEntries = Directory.GetFileSystemEntries(Path.Combine(decoy, ".git"), "*", SearchOption.AllDirectories).Length;

            // What an exported GIT_DIR, GIT_WORK_TREE or GIT_INDEX_FILE looks like to a child: already in its environment.
            var start = Git(own, "init", "-q");
            foreach (var name in new[] { "GIT_DIR", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY" })
                start.Environment[name] = Path.Combine(decoy, ".git");
            start.Environment["GIT_WORK_TREE"] = decoy;
            start.Environment["GIT_INDEX_FILE"] = Path.Combine(decoy, ".git", "index");
            var result = ToolProcess.Run(start);

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.True(Directory.Exists(Path.Combine(own, ".git")), "git did not act on its own working directory");
            Assert.Equal(decoyHead, File.ReadAllText(Path.Combine(decoy, ".git", "HEAD")));
            Assert.Equal(decoyEntries, Directory.GetFileSystemEntries(Path.Combine(decoy, ".git"), "*", SearchOption.AllDirectories).Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AnEnvironmentTheCallerPassesToRunIsAppliedAfterTheGitScrub()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-toolprocess-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(0, ToolProcess.Run(Git(root, "init", "-q")).ExitCode);
            var alternate = Path.Combine(root, "alternate.index");
            var start = Git(root, "read-tree", "--empty");
            start.Environment["GIT_INDEX_FILE"] = Path.Combine(root, "inherited.index");
            var result = ToolProcess.Run(start, new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = alternate });

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.True(File.Exists(alternate));
            Assert.False(File.Exists(Path.Combine(root, "inherited.index")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ANonGitChildSuchAsPowerShellSeesNoRepositoryTargetingVariable()
    {
        var names = ToolProcess.RepositoryTargeting;
        var start = new ProcessStartInfo("pwsh");
        foreach (var argument in new[] { "-NoProfile", "-Command", "Get-ChildItem Env: | Where-Object Name -like 'GIT_*' | ForEach-Object Name" })
            start.ArgumentList.Add(argument);
        foreach (var name in names) start.Environment[name] = Path.GetTempPath();

        var result = ToolProcess.Run(start);

        Assert.True(result.ExitCode == 0, result.Error);
        foreach (var name in names) Assert.DoesNotContain(name, result.Output.Split('\n').Select(l => l.Trim()));
    }

    private static ProcessStartInfo Git(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
}
