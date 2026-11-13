using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ParsimonyGroupEnqueueTests
{
    private static readonly Guid TextId = Guid.NewGuid();

    [Fact]
    public void AnUnknownGroupAndAMeasureOutsideTheGroupAreRefusedBeforeAnyProjectIsOpened()
    {
        var unknown = ParsimonyCommands.EnqueueGroup(new EnqueueParsimonyGroupRequest("missing.fwdata", "1.0", "both"));
        Assert.Equal("parsimony.invalid-group", unknown.Refusal!.Code);

        var outside = ParsimonyCommands.EnqueueGroup(
            new EnqueueParsimonyGroupRequest("missing.fwdata", "1.0", "grammar", "R-slot-blocking"));
        Assert.Equal("parsimony.measure-outside-group", outside.Refusal!.Code);
    }

    [Fact]
    public void TheGrammarGroupQueuesItsFiveMeasuresWithoutAnyTextOrNote()
    {
        using var fixture = Fixture();

        var queued = ParsimonyCommands.EnqueueGroup(new EnqueueParsimonyGroupRequest(fixture.ProjectPath, "1.0", "grammar"));

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        Assert.Null(queued.Value!.Note);
        var input = ReadInput(fixture.ProjectPath, queued.Value.JobId);
        Assert.Equal(9, Prop(input, "measureIds").GetArrayLength());
        Assert.Equal("project-approved", Prop(Prop(input, "scopeBinding"), "kind").GetString());
    }

    [Fact]
    public void TheTextGroupRefusesWhenNoParsimonySelectionIsSaved()
    {
        using var fixture = Fixture();

        var queued = ParsimonyCommands.EnqueueGroup(new EnqueueParsimonyGroupRequest(fixture.ProjectPath, "1.0", "text"));

        Assert.False(queued.Succeeded);
        Assert.Equal("parsimony.parsimony-selection-not-configured", queued.Refusal!.Code);
        Assert.Contains("selection set-parsimony", queued.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusalForAMissingParsimonySelectionCapturesNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), "group-refusal-" + Guid.NewGuid().ToString("N") + ".fwdata");
        File.WriteAllText(path, "not a project");

        var queued = ParsimonyCommands.EnqueueGroup(new EnqueueParsimonyGroupRequest(path, "1.0", "text"));

        Assert.Equal("parsimony.parsimony-selection-not-configured", queued.Refusal!.Code);
    }

    [Fact]
    public void AllMeasuresInTheAllGroupIsTheWholeGroupAndGetsTheNoteToo()
    {
        using var fixture = Fixture();

        var queued = ParsimonyCommands.EnqueueGroup(
            new EnqueueParsimonyGroupRequest(fixture.ProjectPath, "1.0", "all", "all"));

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        Assert.Equal(ParsimonyCommands.TextGroupSkippedNote, queued.Value!.Note);
    }

    [Fact]
    public void AllWithoutAParsimonySelectionRunsTheGrammarGroupAndSaysOnceHowToSetTheOtherUp()
    {
        using var fixture = Fixture();

        var queued = ParsimonyCommands.EnqueueGroup(new EnqueueParsimonyGroupRequest(fixture.ProjectPath, "1.0", "all"));

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        Assert.Equal(ParsimonyCommands.TextGroupSkippedNote, queued.Value!.Note);
        Assert.Equal(9, ReadInput(fixture.ProjectPath, queued.Value.JobId).GetProperty("measureIds").GetArrayLength());
    }

    [Fact]
    public void ASavedParsimonySelectionIsReusedByTheNextRunWithNoFlag()
    {
        using var fixture = Fixture();
        SaveParsimonySelection(fixture.ProjectPath);

        var first = ParsimonyCommands.EnqueueGroup(new EnqueueParsimonyGroupRequest(fixture.ProjectPath, "1.0", "all"));
        var second = ParsimonyCommands.EnqueueGroup(new EnqueueParsimonyGroupRequest(fixture.ProjectPath, "1.0", "all"));

        foreach (var queued in new[] { first, second })
        {
            Assert.True(queued.Succeeded, queued.Refusal?.Message);
            Assert.Null(queued.Value!.Note);
            var input = ReadInput(fixture.ProjectPath, queued.Value.JobId);
            Assert.Equal(MeasureCatalog.All.Count, Prop(input, "measureIds").GetArrayLength());
            var scope = Prop(input, "scopeBinding");
            Assert.Equal("parsimony-selection", Prop(scope, "kind").GetString());
            Assert.Equal("Texts", Prop(Prop(scope, "selection"), "name").GetString());
        }
    }

    [Fact]
    public void AGrammarMeasureOrAllTriesToCaptureAMissingBaselineBeforeQueueing()
    {
        // A marker file cannot be captured, so the refusal is the capture's own: nothing is queued.
        foreach (var measureId in new[] { "P-statement-unused", ParsimonyAdmission.AllMeasures })
        {
            var path = NewMarkerProject();
            var refused = ParsimonyCommands.Enqueue(new EnqueueParsimonyReportRequest(path, "1.0", measureId,
                ParsimonyEvidenceScopeKind.ProjectApproved, []));

            Assert.False(refused.Succeeded);
            Assert.NotEqual("baseline.missing", refused.Refusal!.Code);
            Assert.False(HasBaseline(path));
        }
    }

    [Fact]
    public void AMeasureOutsideTheGrammarGroupDoesNotTryToCapture()
    {
        var queued = ParsimonyCommands.Enqueue(new EnqueueParsimonyReportRequest(NewMarkerProject(), "1.0",
            "B-affix-unslotted", ParsimonyEvidenceScopeKind.ProjectApproved, []));

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
    }

    private static string NewMarkerProject()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ParsimonyMeasureBaseline", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(path, "project marker");
        return path;
    }

    [Fact]
    public void AllWithNamedAssessmentsDoesNotCaptureABaselineAndRefusesWithoutOne()
    {
        var path = NewMarkerProject();

        var refused = ParsimonyCommands.Enqueue(new EnqueueParsimonyReportRequest(path, "1.0",
            ParsimonyAdmission.AllMeasures, ParsimonyEvidenceScopeKind.ProjectApproved, ["assessment/one"]));

        Assert.False(refused.Succeeded);
        Assert.Equal("parsimony.assessment-baseline-mismatch", refused.Refusal!.Code);
        Assert.False(HasBaseline(path));
    }

    private static bool HasBaseline(string fwDataPath)
    {
        var project = new ProjectLocator(fwDataPath, "project");
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        return new BaselineRepository(database).GetCurrentEvidence(ProjectWorkspaceKey.Compute(project)) is not null;
    }

    private static StoredSelectionFixture Fixture()
    {
        var word = new TextWordsProjectedWordform(Guid.NewGuid(), [], [], 0, false, []);
        var token = new TextWordsProjectedToken("word", [new WritingSystemText("word", "en")], word.WordformId,
            "unanalysed", null, null, null, null, 0, null);
        return new StoredSelectionFixture(new TextWordsProjection([
            new TextWordsProjectedText(TextId, "Words", [new TextWordsProjectedLine(1, "Words", [token],
                Guid.NewGuid(), Guid.NewGuid(), true)], [])], [word]));
    }

    private static JsonElement Prop(JsonElement element, string name) =>
        element.EnumerateObject().Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static void SaveParsimonySelection(string fwDataPath)
    {
        var project = new ProjectLocator(fwDataPath, "project");
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        new NamedSelectionRepository(database).SetParsimony("Texts", [TextId], [],
            new SelectionParsingLimits(ParserWorkLimit.Default, SelectionTimeLimitMode.Estimated, null), null);
    }

    private static JsonElement ReadInput(string fwDataPath, string jobId)
    {
        var project = new ProjectLocator(fwDataPath, "project");
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        return JsonDocument.Parse(new JobRepository(database).Get(jobId)!.InputJson).RootElement.Clone();
    }
}
