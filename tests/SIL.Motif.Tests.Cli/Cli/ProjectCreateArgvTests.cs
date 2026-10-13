using System.Text.Json;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ProjectCreateArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-project-create-cli-" + Guid.NewGuid().ToString("N"));
    private string WorkerRoot => Path.Combine(_root, "worker");
    private string Projects => Path.Combine(_root, "projects");

    public ProjectCreateArgvTests()
    {
        Directory.CreateDirectory(WorkerRoot);
        Directory.CreateDirectory(Projects);
    }

    [Fact]
    public async Task CreationIsADeveloperCommand()
    {
        var result = await CliProcess.RunAsync(WorkerRoot, null, developerCommands: false,
            "project", "create", "--into", Projects, "--name", "Kiswahili", "--vernacular", "swh", "--json");

        Assert.NotEqual(0, result.ExitCode);
        using var refusal = JsonDocument.Parse(result.Error);
        Assert.Equal("command.not-in-release", refusal.RootElement.GetProperty("code").GetString());
        Assert.Empty(Directory.GetFileSystemEntries(Projects));
    }

    /// <summary>
    /// The JSON names the new file and its writing systems, and the project is recorded as a Known project so a
    /// later <c>--project</c> verb and the job runner find it.
    /// </summary>
    [Fact]
    public async Task CreationPrintsTheProjectAndRecordsItAsKnown()
    {
        var result = await CliProcess.RunAsync(WorkerRoot, null, developerCommands: true,
            "project", "create", "--into", Projects, "--name", "Kiswahili", "--vernacular", "swh",
            "--vernacular-name", "Kiswahili", "--word-forming", "'", "--json");

        Assert.True(result.ExitCode == 0, result.Error);
        using var response = JsonDocument.Parse(result.Output);
        var root = response.RootElement;
        var fwDataPath = root.GetProperty("fwDataPath").GetString()!;
        Assert.Equal(Path.Combine(Projects, "Kiswahili", "Kiswahili.fwdata"), fwDataPath);
        Assert.True(File.Exists(fwDataPath));
        Assert.Equal("swh", root.GetProperty("vernacular").GetString());
        Assert.Equal("en", root.GetProperty("analysis").GetString());
        Assert.Equal("'", root.GetProperty("wordFormingCharacters")[0].GetString());

        using var machine = MachineDatabase.Open(WorkerRoot);
        var known = Assert.Single(new KnownProjectRegistry(machine).List());
        Assert.Equal(fwDataPath, known.FullFwDataPath);
    }

    [Fact]
    public async Task AMissingRequiredFlagPrintsTheUsage()
    {
        var result = await CliProcess.RunAsync(WorkerRoot, null, developerCommands: true,
            "project", "create", "--into", Projects, "--name", "Kiswahili");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("project create --into <folder> --name <ProjectName> --vernacular <bcp47>", result.Error,
            StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(Projects));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
