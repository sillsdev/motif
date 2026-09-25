using System;
using System.Collections.Generic;
using System.IO;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class CurrentEvidenceQueryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.CurrentEvidenceQueryTests",
        Guid.NewGuid().ToString("N"));

    public CurrentEvidenceQueryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ReadCurrentEvidenceReturnsSavedSummarySelectionAndStaleness()
    {
        var fwDataPath = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(fwDataPath, "synthetic project marker");
        var savedUtc = DateTimeOffset.Parse("2026-08-23T11:30:00Z");
        File.SetLastWriteTimeUtc(fwDataPath, savedUtc.UtcDateTime);
        var project = new ProjectLocator(fwDataPath, "project");
        var textId = Guid.NewGuid();
        var summary = new ProjectSummarySnapshot(2, 3, 4, 5, 6, ["motifa", "motifb"],
            [new ProjectTextSummary(textId, "Genesis", 2, 3,
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["motifa"] = 2,
                    ["motifb"] = 1,
                })]);
        var sourceLastWriteUtc = DateTimeOffset.Parse("2026-08-23T11:00:00Z");

        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var workspaceKey = ProjectWorkspaceKey.Compute(project);
        var root = Path.Combine(_root, "baseline");
        var publication = new BaselinePublication(root, Path.Combine(root, "project.fwdata"),
            new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
                "2026-08-23T10:00:00Z", "sha256:" + new string('a', 64)));
        new BaselineRepository(database).Record(workspaceKey, publication,
            DateTimeOffset.Parse("2026-08-23T12:00:00Z"), sourceLastWriteUtc, summary);
        new NamedSelectionRepository(database).SetDefault("Default", [textId], ["pasted"]);

        var result = CurrentEvidenceQuery.ReadCurrentEvidence(database, project);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(EvidenceFreshness.Stale, result.Value!.Freshness);
        Assert.Equal(savedUtc, result.Value.LastFieldWorksSaveUtc);
        Assert.Equal(2, result.Value.ProjectSummary!.WordCount);
        Assert.Equal("Default", result.Value.DefaultSelection!.Name);
        Assert.Equal(3, result.Value.Selection!.Selection.Words.Count);
        Assert.Equal(2, result.Value.Selection.OccurrencesByWord["motifa"]);
        Assert.Equal(0, result.Value.Selection.OccurrencesByWord["pasted"]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
