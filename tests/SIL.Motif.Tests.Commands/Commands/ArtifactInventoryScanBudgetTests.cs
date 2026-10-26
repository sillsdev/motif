using System.Text.Json;
using SIL.Motif.Commands.Diagnostics;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Trait("MotifTestLevel", "Unit")]
public sealed class ArtifactInventoryScanBudgetTests : IDisposable
{
    private const int ScanBudget = 3;
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "motif-artifact-scan-budget-" + Guid.NewGuid().ToString("N"));
    private readonly string _manifest;

    public ArtifactInventoryScanBudgetTests()
    {
        Directory.CreateDirectory(_root);
        WorkspaceOwnership.Bootstrap(_root);
        var project = Path.Combine(_root, "project.fwdata");
        var store = Path.Combine(_root, "project.motif.db");
        File.WriteAllText(project, "synthetic project locator");
        using (MotifDatabase.OpenOwned(store, new ProjectLocator(project, "scan-budget-test"),
                   MotifSchema.CurrentSchema, MotifProductVersion.Current))
        {
        }

        _manifest = Path.Combine(_root, "stores.json");
        File.WriteAllText(_manifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            referenceDomainComplete = true,
            stores = new[]
            {
                new { databasePath = store, projectPath = project, projectIdentity = "scan-budget-test" }
            }
        }));
    }

    [Fact]
    public void ExactEntryCapWithoutOmissionsRemainsComplete()
    {
        var family = Path.Combine(_root, "assessment-sources");
        Directory.CreateDirectory(family);
        File.WriteAllText(Path.Combine(family, "a.fwdata"), "a");
        File.WriteAllText(Path.Combine(family, "b.fwdata"), "bb");
        File.WriteAllText(Path.Combine(family, "c.fwdata"), "ccc");

        var outcome = Observe(ScanBudget);

        Assert.Equal(new ScanOutcome(true, 6, false), outcome);
    }

    [Fact]
    public void ImmediateChildrenOmittedAtEntryCapMakeInventoryIncomplete()
    {
        var family = Path.Combine(_root, "assessment-sources");
        Directory.CreateDirectory(family);
        for (var index = 0; index < ScanBudget + 1; index++)
            File.WriteAllText(Path.Combine(family, $"{index}.fwdata"), "x");

        var outcome = Observe(ScanBudget);

        Assert.Equal(new ScanOutcome(false, null, true), outcome);
    }

    [Fact]
    public void NestedChildrenOmittedAtEntryCapMakeInventoryIncomplete()
    {
        var nested = Path.Combine(_root, "assessment-runs", "run");
        Directory.CreateDirectory(nested);
        for (var index = 0; index < ScanBudget; index++)
            File.WriteAllText(Path.Combine(nested, $"{index}.fwdata"), "x");

        var outcome = Observe(ScanBudget);

        Assert.Equal(new ScanOutcome(false, null, true), outcome);
    }

    private ScanOutcome Observe(int scanEntryBudget)
    {
        var response = ArtifactRetentionCommands.Inventory(
            new ArtifactInventoryRequest(_root, _manifest), scanEntryBudget);
        return new ScanOutcome(response.InventoryComplete, response.LogicalBytes,
            response.Diagnostics.Any(diagnostic => diagnostic.Code == "scan-entry-limit"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record ScanOutcome(bool InventoryComplete, long? LogicalBytes, bool HasEntryLimitDiagnostic);
}
