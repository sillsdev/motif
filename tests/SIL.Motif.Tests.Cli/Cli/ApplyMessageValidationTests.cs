using System.Security.Cryptography;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class ApplyMessageValidationTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-apply-message-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void InvalidMessageIsRefusedBeforeApplyCreatesProjectSidecarsOrBackups()
    {
        string projectPath;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            projectPath = cache.ProjectId.Path;
        }

        var directory = Path.GetDirectoryName(projectPath)!;
        var filesBefore = Directory.GetFiles(directory).Order(StringComparer.Ordinal).ToArray();
        var projectHashBefore = SHA256.HashData(File.ReadAllBytes(projectPath));

        var result = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", "proposal/not-read",
            Attribution: new ApplyAttribution(Message: new string('x', 129))));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.ApplyMessageInvalid, result.Refusal!.Code);
        Assert.Equal(filesBefore, Directory.GetFiles(directory).Order(StringComparer.Ordinal));
        Assert.Equal(projectHashBefore, SHA256.HashData(File.ReadAllBytes(projectPath)));
    }

    [Fact]
    public async Task CliRefusesInvalidMessageBeforeRecordingOrTouchingTheNamedProject()
    {
        string projectPath;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            projectPath = cache.ProjectId.Path;
        }

        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var filesBefore = Directory.GetFiles(projectDirectory).Order(StringComparer.Ordinal).ToArray();
        var projectHashBefore = SHA256.HashData(File.ReadAllBytes(projectPath));
        var result = await CliProcess.RunAsync(_root, null, true, "apply", "proposal/not-read",
            "--project", projectPath, "--message", new string('x', 129), "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(RefusalCodes.ApplyMessageInvalid,
            ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!.Code);
        Assert.Equal(filesBefore, Directory.GetFiles(projectDirectory).Order(StringComparer.Ordinal));
        Assert.Equal(projectHashBefore, SHA256.HashData(File.ReadAllBytes(projectPath)));
        using var machine = MachineDatabase.Open(_root);
        Assert.DoesNotContain(new KnownProjectRegistry(machine).List(), project =>
            project.FullFwDataPath == Path.GetFullPath(projectPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
