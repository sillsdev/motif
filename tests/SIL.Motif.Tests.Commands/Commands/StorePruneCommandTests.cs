using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class StorePruneCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-prune-command-" + Guid.NewGuid().ToString("N"));

    public StorePruneCommandTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void PruneUsesTheProjectSettingAndReportsTheCheckpointedStore()
    {
        var project = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(project, "fixture");
        File.WriteAllText(Path.Combine(_root, "project.motif.toml"), "[retention]\nstandalone-assessments-per-kind = 1\n");
        var seeded = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText, (database, _) =>
        {
            var repository = new AssessmentRepository(database);
            for (var i = 0; i < 3; i++)
                repository.Record(new NewAssessmentRecord($"assessment-{i}", null, null, "pangloss", "ParseTime",
                    "{}", "scope", new AssessmentSubject.Words(Selection.Create("fixture", ["word"]), "none", "1"),
                    "{}", "outcome", "semantic", "grammar", "model", "pipeline", 0,
                    new AssessmentRecordMaterial.Words([new AssessedWord("word", "complete", [])], null, null, []),
                    $"2026-01-0{i + 1}T00:00:00Z"));
            return CommandOutcome<string>.Success("seeded");
        });
        Assert.True(seeded.Succeeded, seeded.Refusal?.Message);

        var result = StorePruneCommand.Run(new StorePruneRequest(project));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(2, result.Value!.AssessmentsRemoved);
        Assert.True(result.Value.CheckpointComplete);
        Assert.True(result.Value.BytesBefore > 0);
        Assert.True(result.Value.BytesAfter > 0);
        var remaining = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText,
            (database, _) => CommandOutcome<string>.Success(new AssessmentRepository(database).Get("assessment-2").AssessmentId));
        Assert.Equal("assessment-2", remaining.Value);
    }

    [Fact]
    public void ExpiredSelectedEvidenceBecomesACataloguedRefusal()
    {
        var project = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(project, "fixture");
        var result = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText,
            (_, _) => throw new AssessmentExpiredException("assessment-1"));

        Assert.Equal(RefusalCodes.AssessmentExpired, result.Refusal!.Code);
        Assert.Equal(FailureReason.NotFound, result.Refusal.Reason);
        Assert.True(RefusalCatalog.Specs.ContainsKey(RefusalCodes.AssessmentExpired));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}
