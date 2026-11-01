using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

/// <summary>The tools that read one Proposal whose stored content no longer parses.</summary>
[Trait("MotifTestLevel", "Unit")]
public sealed class UnreadableProposalToolTests
{
    [Fact]
    public async Task ProducingTheDifferenceOfAnUnreadableProposalIsRefusedWithTheProposalCode()
    {
        using var project = UnreadableProposalProject.Create();

        var result = await CallAsync(project, "motif_difference_produce",
            ("proposal", project.ProposalId), ("text", Guid.NewGuid().ToString("D")));

        AssertRefused(result);
    }

    [Fact]
    public async Task TryingAWordInAnUnreadableProposalIsRefusedWithTheProposalCode()
    {
        using var project = UnreadableProposalProject.Create();

        var result = await CallAsync(project, "motif_try_word", ("word", "cat"), ("proposal", project.ProposalId));

        AssertRefused(result);
    }

    [Fact]
    public async Task TheProposalListNamesTheUnreadableDraftBesideItsId()
    {
        using var project = UnreadableProposalProject.Create();

        var result = await CallAsync(project, "motif_proposals");

        Assert.NotEqual(true, result.IsError);
        var item = result.StructuredContent!.Value.GetProperty("result").GetProperty("unreadable").EnumerateArray().Single();
        Assert.Equal(project.ProposalId, item.GetProperty("proposalId").GetString());
        Assert.Equal(project.DraftName, item.GetProperty("draftName").GetString());
    }

    private static void AssertRefused(ModelContextProtocol.Protocol.CallToolResult result)
    {
        Assert.True(result.IsError);
        var structured = result.StructuredContent!.Value;
        Assert.Equal(RefusalCodes.ProposalUnreadable, structured.GetProperty("code").GetString());
        Assert.Contains(UnreadableProposalProject.UnknownKind, structured.GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    private static async Task<ModelContextProtocol.Protocol.CallToolResult> CallAsync(
        UnreadableProposalProject project, string tool, params (string Name, object Value)[] values)
    {
        using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
            new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                new ProjectLocator(project.ProjectPath, "project")), project.ProjectPath, DateTimeOffset.UtcNow);
        var context = new ServerContext(project.ProjectPath, "1.0", new NoRunnerLauncher(
            new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null),
            ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
        var arguments = new Dictionary<string, JsonElement>
        {
            ["project"] = JsonSerializer.SerializeToElement(project.ProjectPath),
        };
        foreach (var (name, value) in values) arguments[name] = JsonSerializer.SerializeToElement(value);

        return await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin), tool, arguments,
            CancellationToken.None);
    }
}
