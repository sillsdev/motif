using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Store;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class PhonologyResultsCommandTests(PristineProjectFixture pristine)
{
    private static readonly string BareHash = new('a', 64);
    private static readonly BaselineToken Baseline = new("project", "sha256:" + new string('b', 64), "projection",
        "2026-01-01T00:00:00Z", "sha256:" + new string('c', 64));

    [Fact]
    public void ResultsReturnsASummaryPageAndThenOneCompleteExampleFromStoredRows()
    {
        var (projectPath, assessmentId) = StoreCheck(["front", "back"]);

        var page = PhonologyExampleCommands.Results(new PhonologyResultsRequest(projectPath, assessmentId,
            new PhonologyResultsRead.Page(PageSize: 1)));

        Assert.True(page.Succeeded, page.Refusal?.Message);
        var first = Assert.IsType<PhonologyResultsPageResponse>(page.Value);
        Assert.Equal("front", Assert.Single(first.Items).Example.Id);
        Assert.NotNull(first.Continuation);

        var next = PhonologyExampleCommands.Results(new PhonologyResultsRequest(projectPath, assessmentId,
            new PhonologyResultsRead.Page(1, first.Continuation)));
        Assert.Equal("back", Assert.Single(Assert.IsType<PhonologyResultsPageResponse>(next.Value).Items).Example.Id);

        var detail = PhonologyExampleCommands.Results(new PhonologyResultsRequest(projectPath, assessmentId,
            new PhonologyResultsRead.Example("back")));
        Assert.True(detail.Succeeded, detail.Refusal?.Message);
        var example = Assert.IsType<PhonologyExampleResultResponse>(detail.Value);
        Assert.Equal("back", example.Result.Example.Id);
        Assert.Equal(assessmentId, example.Assessment.AssessmentId);
    }

    [Fact]
    public void ResultsRefusesUnknownAssessmentsExamplesAndForeignOrBadPaging()
    {
        var (projectPath, assessmentId) = StoreCheck(["front"]);

        Assert.Equal("phonology-examples.assessment-not-found", PhonologyExampleCommands.Results(
            new PhonologyResultsRequest(projectPath, "assessment/missing",
                new PhonologyResultsRead.Page())).Refusal?.Code);
        Assert.Equal("phonology-examples.example-not-found", PhonologyExampleCommands.Results(
            new PhonologyResultsRequest(projectPath, assessmentId,
                new PhonologyResultsRead.Example("nope"))).Refusal?.Code);
        Assert.Equal("phonology-examples.invalid", PhonologyExampleCommands.Results(
            new PhonologyResultsRequest(projectPath, assessmentId,
                new PhonologyResultsRead.Page(0))).Refusal?.Code);
        Assert.Equal("phonology-examples.invalid", PhonologyExampleCommands.Results(
            new PhonologyResultsRequest(projectPath, assessmentId,
                new PhonologyResultsRead.Page(5, new PhonologyPageCursor("other", 0)))).Refusal?.Code);
    }

    [Fact]
    public void TheReadSelectorIsClosedOnTheWire()
    {
        var options = MotifJson.CreateOptions();
        var page = JsonSerializer.Deserialize<PhonologyResultsRead>("""{"kind":"page","pageSize":5}""", options);
        var example = JsonSerializer.Deserialize<PhonologyResultsRead>("""{"kind":"example","exampleId":"x"}""", options);

        Assert.Equal(new PhonologyResultsRead.Page(5), page);
        Assert.Equal(new PhonologyResultsRead.Example("x"), example);
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<PhonologyResultsRead>(
            """{"kind":"example","exampleId":"x","pageSize":3}""", options));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<PhonologyResultsRead>(
            """{"kind":"everything"}""", options));
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<PhonologyResultsRead>("""{}""", options));
    }

    private (string ProjectPath, string AssessmentId) StoreCheck(string[] ids)
    {
        var projectPath = pristine.CopyProjectFile();
        var saved = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(projectPath, "Set",
            [.. ids.Select(id => new PhonologyExample(id, "a-ku", ["e-ki"],
                new PhonologyGenerationContext.HcXmlEntry("entry-17")))]));
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        var revision = saved.Value!.ExampleSet;
        var rows = ids.Select(id => Row(id)).ToArray();
        var limits = new PhonologyLimits(100, 200, 100);
        rows = [.. rows.Select((row, index) => row with
        {
            EffectiveLimits = new PhonologyLimits(
                limits.StepCap / (ulong)ids.Length + ((ulong)index < limits.StepCap % (ulong)ids.Length ? 1UL : 0UL),
                limits.WorkCap / (ulong)ids.Length + ((ulong)index < limits.WorkCap % (ulong)ids.Length ? 1UL : 0UL),
                limits.TimeoutMs),
            Steps = 1, WorkSteps = 1,
        })];
        var measurement = new PhonologyMeasurement(BareHash, [new("WritingSystemStore/ws.ldml", BareHash)],
            new string('e', 64), new string('f', 64), "producer-build", new string('d', 64), BareHash, 1024, limits, 9, rows);
        var assessmentId = "assessment/phonology-results";
        var record = new NewAssessmentRecord(assessmentId, null, null, "pangloss", "PhonologyExamples",
            ScopeCodec.Write(new StoredScope.PhonologyExamples(revision.Revision, limits)), "sha256:scope",
            new AssessmentSubject.PhonologyExamples(revision.Revision),
            JsonSerializer.Serialize(Baseline, MotifJson.CreateOptions()), null, null, "sha256:" + BareHash, null, null, null,
            new AssessmentRecordMaterial.PhonologyExamples(new PhonologyBaselineSource(Baseline), limits,
                new PhonologyAssessmentOutcome.Produced(measurement)));
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            new AssessmentRepository(database).Record(record);
            return CommandOutcome<string>.Success("ok");
        });
        return (projectPath, assessmentId);
    }

    private static PhonologyMeasuredRow Row(string id) => new(id,
        new PhonologyGenerationContext.HcXmlEntry("entry-17"), new PhonologyGenerationContext.HcXmlEntry("entry-17"),
        "a-ku", ["e-ki"], new PhonologyLimits(0, 0, 100), PhonologyDisposition.Agree, PhonologyCompletion.Complete,
        [true], "e-ki", null, 1, 1, false, false, ["r1"], ["r1"], ["r1"]);
}
