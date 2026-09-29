using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheTestCollection.Name)]
public sealed class AgentChangesArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-agent-changes-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot;
    private readonly PristineProjectFixture _pristine;

    public AgentChangesArgvTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        _workerRoot = Path.Combine(_root, "worker");
        Directory.CreateDirectory(_workerRoot);
    }

    [Fact]
    public async Task RemoveAnalysisRunsThroughTheExecutable()
    {
        using var cache = _pristine.NewScratch();
        var text = SeededProject.SeedText(cache, _pristine.Seed);
        var project = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        await CaptureBaseline(project);
        var pending = await ReadPending(project);

        var response = await CliProcess.RunAsync(_workerRoot, null, true, "remove-analysis",
            "--project", project, "--expected-revision", pending.Revision,
            "--change-id", "cli-remove-" + Guid.NewGuid().ToString("N"),
            "--wordform-id", CanonicalId.FromGuid(text.AnalysedWordformId).Value,
            "--word", SeededProject.AnalysedWordForm,
            "--analysis-id", CanonicalId.FromGuid(text.ApprovedAnalysisId).Value, "--json");

        var removed = SuccessfulSnapshot(response);
        var change = Assert.Single(removed.Changes);
        Assert.Equal("remove-analysis", change.Kind);
        Assert.Equal("fits", Assert.Single(removed.FitSummary).Status);
    }

    [Fact]
    public async Task AcceptNewSetRunsThroughTheExecutable()
    {
        using var cache = _pristine.NewScratch();
        var source = cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(_pristine.Seed.FirstEntryId);
        var msaId = source.MorphoSyntaxAnalysesOC.First().Guid;
        Guid wordformId = Guid.Empty;
        const string word = "cli-accept-word";
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs)).Guid);
        var project = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        await CaptureBaseline(project);
        RecordParseAssessment(project, word, _pristine.Seed.FirstLexemeFormId, msaId);
        var pending = await ReadPending(project);

        var response = await CliProcess.RunAsync(_workerRoot, null, true, "accept-new-set",
            "--project", project, "--expected-revision", pending.Revision,
            "--assessment", "cli-accept-assessment", "--wordform-id", CanonicalId.FromGuid(wordformId).Value,
            "--json");

        var accepted = SuccessfulSnapshot(response);
        var change = Assert.Single(accepted.Changes);
        Assert.Equal("add-candidate", change.Kind);
        Assert.Single(change.OperationIds);
    }

    [Fact]
    public async Task PutListRecheckAndRemoveRoundTripThroughTheExecutable()
    {
        var project = _pristine.CopyProjectFile();
        var wordform = AddWordform(project, "agent-roundtrip-word");
        await CaptureBaseline(project);
        var initial = await ReadPending(project);
        Assert.Equal("none", initial.Revision);
        Assert.Empty(initial.Changes);

        var changeId = "agent-change-" + Guid.NewGuid().ToString("N");
        var put = await CliProcess.RunAsync(_workerRoot, null, true, "put-pending-change", "--project", project,
            "--expected-revision", initial.Revision, "--change-id", changeId,
            "--kind", "incorrect-spelling", "--word", "agent-roundtrip-word",
            "--wordform-id", wordform, "--json");
        var added = SuccessfulSnapshot(put);
        Assert.Contains(added.Changes, change => change.ChangeId == changeId && change.Word == "agent-roundtrip-word");

        var listed = await ReadPending(project);
        Assert.Equal(added.Revision, listed.Revision);
        Assert.Contains(listed.Changes, change => change.ChangeId == changeId);

        AddWordform(project, "agent-roundtrip-unrelated");
        File.SetLastWriteTimeUtc(project, File.GetLastWriteTimeUtc(project).AddMinutes(1));
        await CaptureBaseline(project);
        var drifted = await ReadPending(project);
        Assert.False(Assert.Single(drifted.FitSummary).StillFits);

        var recheckedResult = await CliProcess.RunAsync(_workerRoot, null, true, "recheck-pending-changes", "--project", project,
            "--expected-revision", drifted.Revision, "--json");
        var rechecked = SuccessfulSnapshot(recheckedResult);
        Assert.Contains(rechecked.Changes, change => change.ChangeId == changeId);
        Assert.True(Assert.Single(rechecked.FitSummary).StillFits);
        Assert.NotEqual(drifted.Revision, rechecked.Revision);

        var removedResult = await CliProcess.RunAsync(_workerRoot, null, true, "remove-pending-change", "--project", project,
            "--expected-revision", rechecked.Revision, "--change-id", changeId, "--json");
        var removed = SuccessfulSnapshot(removedResult);
        Assert.Empty(removed.Changes);
        Assert.NotNull(removed.DraftId);
    }

    [Fact]
    public async Task AStaleRevisionIsRefusedAndARetryLandsOnce()
    {
        var project = _pristine.CopyProjectFile();
        var firstWordform = AddWordform(project, "agent-stale-word-a");
        var secondWordform = AddWordform(project, "agent-stale-word-b");
        await CaptureBaseline(project);
        var initial = await ReadPending(project);
        var firstId = "agent-change-a-" + Guid.NewGuid().ToString("N");
        var secondId = "agent-change-b-" + Guid.NewGuid().ToString("N");

        var first = SuccessfulSnapshot(await Put(project, initial.Revision, firstId,
            "agent-stale-word-a", firstWordform));
        var stale = await Put(project, initial.Revision, secondId, "agent-stale-word-b", secondWordform);
        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), stale.ExitCode);
        var refusal = ProjectionJson.Deserialize<FailureEnvelope>(stale.Error)!;
        Assert.Equal("change.revision-conflict", refusal.Code);

        var afterRefusal = await ReadPending(project);
        Assert.Equal(first.Revision, afterRefusal.Revision);
        Assert.Single(afterRefusal.Changes);
        Assert.Contains(afterRefusal.Changes, change => change.ChangeId == firstId);

        var retried = SuccessfulSnapshot(await Put(project, afterRefusal.Revision, secondId,
            "agent-stale-word-b", secondWordform));
        Assert.Equal(2, retried.Changes.Count);
        Assert.Single(retried.Changes, change => change.ChangeId == secondId);
    }

    [Fact]
    public async Task AnEditedSentenceReturnsUncertainEvidenceInJson()
    {
        var scenario = await PrepareUncertainOccurrence();
        EditSentenceWord(scenario.Project, scenario.Text.FirstParagraphId, scenario.Text.FirstSegmentId,
            "changedcliword", "motifanalysed changedcliword.");
        File.SetLastWriteTimeUtc(scenario.Project,
            File.GetLastWriteTimeUtc(scenario.Project).AddMinutes(1));
        await CaptureBaseline(scenario.Project);
        var beforeRecheck = await ReadPending(scenario.Project);

        var response = await CliProcess.RunAsync(_workerRoot, null, true,
            "recheck-pending-changes", "--project", scenario.Project,
            "--expected-revision", beforeRecheck.Revision, "--json");
        var rechecked = SuccessfulSnapshot(response);
        var fit = Assert.Single(rechecked.FitSummary);

        Assert.Equal("uncertain", fit.Status);
        Assert.Equal("changedcliword", Assert.Single(fit.Uncertainty!.AfterTokens,
            token => token.Index == 1).Form);
    }

    [Fact]
    public async Task ReconfirmPendingChangeRunsThroughTheExecutable()
    {
        var scenario = await PrepareUncertainOccurrence();
        EditSentenceWord(scenario.Project, scenario.Text.FirstParagraphId, scenario.Text.FirstSegmentId,
            "changedcliword", "motifanalysed changedcliword.");
        File.SetLastWriteTimeUtc(scenario.Project,
            File.GetLastWriteTimeUtc(scenario.Project).AddMinutes(1));
        await CaptureBaseline(scenario.Project);
        var pending = await ReadPending(scenario.Project);
        var checkedResult = await CliProcess.RunAsync(_workerRoot, null, true,
            "recheck-pending-changes", "--project", scenario.Project,
            "--expected-revision", pending.Revision, "--json");
        var uncertain = SuccessfulSnapshot(checkedResult);
        var change = Assert.Single(uncertain.Changes);

        var result = await CliProcess.RunAsync(_workerRoot, null, true,
            "reconfirm-pending-change", "--project", scenario.Project,
            "--expected-revision", uncertain.Revision, "--change-id", change.ChangeId, "--json");
        var reconfirmed = SuccessfulSnapshot(result);

        Assert.Equal("fits", Assert.Single(reconfirmed.FitSummary).Status);
        Assert.NotEqual(uncertain.Revision, reconfirmed.Revision);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task CaptureBaseline(string project)
    {
        var result = await CliProcess.RunAsync(_workerRoot, null, false,
            "baseline", "capture", project, "--json");
        Assert.True(result.ExitCode == 0, result.FailureDetails);
    }

    private async Task<CliOccurrenceScenario> PrepareUncertainOccurrence()
    {
        using var cache = _pristine.NewScratch();
        var text = SeededProject.SeedText(cache, _pristine.Seed);
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
            .GetObject(text.FirstParagraphId);
        Guid otherWordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var otherWordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("cli-second-word", cache.DefaultVernWs));
            otherWordformId = otherWordform.Guid;
            paragraph.SegmentsOS[0].AnalysesRS.Insert(1, otherWordform);
            paragraph.Contents = TsStringUtils.MakeString(
                $"{SeededProject.AnalysedWordForm} cli-second-word{SeededProject.PunctuationForm}",
                cache.DefaultVernWs);
            paragraph.ParseIsCurrent = true;
        });
        var project = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        await CaptureBaseline(project);
        var pending = await ReadPending(project);
        var changeId = "cli-occurrence-" + Guid.NewGuid().ToString("N");
        var added = await CliProcess.RunAsync(_workerRoot, null, true,
            "put-pending-change", "--project", project,
            "--expected-revision", pending.Revision,
            "--change-id", changeId, "--kind", AnalysisChangeKinds.Reject,
            "--word", SeededProject.AnalysedWordForm,
            "--wordform-id", CanonicalId.FromGuid(text.AnalysedWordformId).Value,
            "--stored-analysis-id", CanonicalId.FromGuid(text.ApprovedAnalysisId).Value,
            "--occurrence-text-id", text.TextId.ToString("D"),
            "--occurrence-paragraph-id", text.FirstParagraphId.ToString("D"),
            "--occurrence-segment-id", text.FirstSegmentId.ToString("D"),
            "--occurrence-index", "0", "--json");
        Assert.Equal("fits", Assert.Single(SuccessfulSnapshot(added).FitSummary).Status);
        return new CliOccurrenceScenario(project, text, otherWordformId);
    }

    private static void EditSentenceWord(string project, Guid paragraphId, Guid segmentId,
        string replacementForm, string contents)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(project);
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>().GetObject(paragraphId);
        var segment = cache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(segmentId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var replacement = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(replacementForm, cache.DefaultVernWs));
            segment.AnalysesRS.RemoveAt(1);
            segment.AnalysesRS.Insert(1, replacement);
            paragraph.Contents = TsStringUtils.MakeString(contents, cache.DefaultVernWs);
            paragraph.ParseIsCurrent = true;
        });
        new FwDataProjectLoader().Save(cache);
    }

    private async Task<PendingChangesSnapshot> ReadPending(string project)
    {
        var result = await CliProcess.RunAsync(_workerRoot, null, true,
            "pending-changes", "--project", project, "--json");
        return SuccessfulSnapshot(result);
    }

    private Task<CliProcessResult> Put(string project, string revision, string changeId, string word, string wordformId) =>
        CliProcess.RunAsync(_workerRoot, null, true,
            "put-pending-change", "--project", project, "--expected-revision", revision,
            "--change-id", changeId, "--kind", "incorrect-spelling", "--word", word,
            "--wordform-id", wordformId, "--json");

    private static PendingChangesSnapshot SuccessfulSnapshot(CliProcessResult result)
    {
        Assert.True(result.ExitCode == 0, result.FailureDetails);
        return ProjectionJson.Deserialize<PendingChangesSnapshot>(result.Output)!;
    }

    private static void RecordParseAssessment(string path, string word, Guid formId, Guid msaId)
    {
        var fullPath = Path.GetFullPath(path);
        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))!;
        var selection = Selection.Create("CLI acceptance", [word]);
        var reading = new ParseAnalysis([new ParseMorph(formId.ToString("D"), msaId.ToString("D"), null, null)]);
        var morphology = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, word, 0, false, false,
            false, [reading], []);
        var assessedWord = new AssessedWord(word, "analysed",
            [new ParsedAnalysis(null, [], 0, "sha256:cli-reading")]) { Morphology = morphology };
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            "cli-accept-assessment", null, null, "test", AssessmentKind.ParseTime.ToStoredKind(), "{}",
            "sha256:cli-scope", "whitespace-and-punctuation", "1", JsonSerializer.Serialize(baseline.Token),
            selection, "sha256:cli-outcome", "sha256:cli-semantic", "sha256:cli-grammar", "cli-test",
            "cli-test", 0, [assessedWord], SavedUtc: "2026-09-28T12:00:00Z"));
    }

    private static string AddWordform(string project, string word)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(project);
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(cache);
        return SIL.Motif.Contract.Ids.CanonicalId.FromGuid(wordformId).Value;
    }

    private sealed record CliOccurrenceScenario(string Project, SIL.Motif.Tests.TestFixtures.SeededText Text,
        Guid OtherWordformId);

}
