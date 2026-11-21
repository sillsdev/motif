using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class StoreHolderRefusalTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-holder-refusal-" + Guid.NewGuid().ToString("N"));

    public StoreHolderRefusalTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ABusyStoreRefusalNamesTheRunnerHoldingItAndHowToStopIt()
    {
        var project = Path.Combine(_root, "held.fwdata");
        File.WriteAllText(project, "<languageproject/>");
        var locator = new ProjectLocator(project, "held");
        var storePath = ProjectDatabaseCatalog.DatabasePathFor(locator);
        using var first = MotifDatabase.OpenOwned(storePath, locator, MotifSchema.CurrentSchema, new Version(1, 0));
        using var holderMark = StoreHolder.Publish(storePath, "/runner/root");
        using var connection = first.OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA user_version = {MotifSchema.CurrentSchema + 1};";
            command.ExecuteNonQuery();
        }

        var outcome = ProjectStoreReset.DeleteRefused(new ProjectStoreResetRequest(project), "1.0");

        var refusal = outcome.Refusal!;
        Assert.Equal(RefusalCodes.ProjectBusy, refusal.Code);
        Assert.Equal(Environment.ProcessId.ToString(), refusal.Facts["holderPid"]);
        Assert.Equal("/runner/root", refusal.Facts["holderRoot"]);
        Assert.Equal(RunnerIdentity.BuildId, refusal.Facts["holderBuild"]);
        Assert.Contains("motif worker stop --project", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("pid " + Environment.ProcessId, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABusyStoreRefusalWithNoRecordedRunnerIsUnchanged()
    {
        var project = Path.Combine(_root, "plain.fwdata");
        File.WriteAllText(project, "<languageproject/>");
        var locator = new ProjectLocator(project, "plain");
        var storePath = ProjectDatabaseCatalog.DatabasePathFor(locator);
        using var first = MotifDatabase.OpenOwned(storePath, locator, MotifSchema.CurrentSchema, new Version(1, 0));
        using var connection = first.OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA user_version = {MotifSchema.CurrentSchema + 1};";
            command.ExecuteNonQuery();
        }

        var outcome = ProjectStoreReset.DeleteRefused(new ProjectStoreResetRequest(project), "1.0");

        var refusal = outcome.Refusal!;
        Assert.Equal(RefusalCodes.ProjectBusy, refusal.Code);
        Assert.False(refusal.Facts.ContainsKey("holderPid"));
        Assert.Equal("The project store is in use.", refusal.Message);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
