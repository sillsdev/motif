using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Host;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class AnalysisMarkingsTests(PristineProjectFixture pristine)
{
    [Fact]
    public void AcceptNewSetAddsEachMissingReadingOnceAndUndoRemovesTheAction()
    {
        var scenario = NewScenario("accept-complete");
        var reading = Reading(scenario.FormId, scenario.MsaId);
        RecordAssessment(scenario, [Word(scenario.Word, "analysed", [reading, reading])]);
        var initial = PendingChanges.Load(new PendingChangesRequest(scenario.Path, ProductVersion)).Value!;

        var accepted = PendingChanges.AcceptNewSet(new AcceptNewSetRequest(scenario.Path, ProductVersion,
            initial.Revision, "assessment-accept", WordformId: scenario.WordformId));

        Assert.True(accepted.Succeeded, accepted.Refusal?.Message);
        var change = Assert.Single(accepted.Value!.Changes);
        Assert.Equal("add-candidate", change.Kind);
        Assert.Single(change.OperationIds);
        Assert.Equal(ChangeFitStatus.Fits, Assert.Single(accepted.Value.FitSummary).Status);

        var repeated = PendingChanges.AcceptNewSet(new AcceptNewSetRequest(scenario.Path, ProductVersion,
            accepted.Value.Revision, "assessment-accept", WordformId: scenario.WordformId));
        Assert.True(repeated.Succeeded, repeated.Refusal?.Message);
        Assert.Equal(accepted.Value.Revision, repeated.Value!.Revision);
        Assert.Single(repeated.Value.Changes);

        var undone = PendingChanges.Remove(new RemovePendingChangeRequest(scenario.Path, ProductVersion,
            repeated.Value.Revision, change.ChangeId));
        Assert.True(undone.Succeeded, undone.Refusal?.Message);
        Assert.Empty(undone.Value!.Changes);
    }

    [Fact]
    public void PendingChangeSnapshotCanDriveRedoThroughThePutCommand()
    {
        var scenario = NewScenario("redo-spelling");
        var initial = PendingChanges.Load(new PendingChangesRequest(scenario.Path, ProductVersion)).Value!;
        var intent = new ChangeIntent("redo-spelling", "incorrect-spelling", scenario.WordformId,
            scenario.Word);

        var staged = PendingChanges.Put(new PutPendingChangeRequest(scenario.Path, ProductVersion,
            initial.Revision, intent));

        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        var reloaded = PendingChanges.Load(new PendingChangesRequest(scenario.Path, ProductVersion));
        Assert.True(reloaded.Succeeded, reloaded.Refusal?.Message);
        var recordedIntent = Assert.Single(reloaded.Value!.Changes).StagingIntent;
        Assert.Equal(intent, recordedIntent);
        var removed = PendingChanges.Remove(new RemovePendingChangeRequest(scenario.Path, ProductVersion,
            reloaded.Value.Revision, intent.ChangeId));
        Assert.True(removed.Succeeded, removed.Refusal?.Message);

        var redone = PendingChanges.Put(new PutPendingChangeRequest(scenario.Path, ProductVersion,
            removed.Value!.Revision, recordedIntent!));

        Assert.True(redone.Succeeded, redone.Refusal?.Message);
        var restored = Assert.Single(redone.Value!.Changes);
        Assert.Equal(intent.ChangeId, restored.ChangeId);
        Assert.Equal(intent.Kind, restored.Kind);
        Assert.Equal(intent.WordformId, restored.WordformId);
        Assert.Equal(intent.Word, restored.Word);
    }

    [Theory]
    [InlineData("capped", true, false)]
    [InlineData("skipped", false, false)]
    [InlineData("timed-out", false, true)]
    public void AcceptNewSetRefusesIncompleteOrSkippedWordsWithoutWriting(string outcome, bool capped,
        bool timedOut)
    {
        var scenario = NewScenario("accept-" + outcome);
        var reading = Reading(scenario.FormId, scenario.MsaId);
        RecordAssessment(scenario, [Word(scenario.Word, outcome, [reading], capped, timedOut)]);
        var initial = PendingChanges.Load(new PendingChangesRequest(scenario.Path, ProductVersion)).Value!;

        var accepted = PendingChanges.AcceptNewSet(new AcceptNewSetRequest(scenario.Path, ProductVersion,
            initial.Revision, "assessment-accept", WordformId: scenario.WordformId));

        Assert.False(accepted.Succeeded);
        Assert.Equal("change.assessment-incomplete", accepted.Refusal?.Code);
        Assert.Contains("complete Assessment", accepted.Refusal!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(PendingChanges.Load(new PendingChangesRequest(scenario.Path, ProductVersion)).Value!.Changes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AcceptNewSetSupportsTextAndSelectionScopesAndUndoGroup(bool useTextScope)
    {
        var scenario = NewScenario("accept-scope-" + useTextScope);
        var text = AddTextWithSecondWord(scenario);
        scenario = text.Scenario;
        var reading = Reading(scenario.FormId, scenario.MsaId);
        RecordAssessment(scenario,
        [
            Word(scenario.Word, "analysed", [reading]),
            Word(text.OtherWord, "analysed", [reading]),
        ]);
        var initial = PendingChanges.Load(new PendingChangesRequest(scenario.Path, ProductVersion)).Value!;
        var request = new AcceptNewSetRequest(scenario.Path, ProductVersion, initial.Revision,
            "assessment-accept", TextId: useTextScope ? text.TextId : null, Selection: !useTextScope);

        var accepted = PendingChanges.AcceptNewSet(request);

        Assert.True(accepted.Succeeded, accepted.Refusal?.Message);
        Assert.Equal(2, accepted.Value!.Changes.Count);
        Assert.All(accepted.Value.Changes, change => Assert.NotNull(change.GroupId));
        var groupId = Assert.Single(accepted.Value.Changes.Select(change => change.GroupId).Distinct());
        Assert.NotNull(groupId);

        var undone = PendingChanges.Remove(new RemovePendingChangeRequest(scenario.Path, ProductVersion,
            accepted.Value.Revision, groupId!));

        Assert.True(undone.Succeeded, undone.Refusal?.Message);
        Assert.Empty(undone.Value!.Changes);
    }

    [Fact]
    public void AcceptNewSetNamesASelectionWordThatIsNotInTheFieldWorksProject()
    {
        var scenario = NewScenario("accept-pasted-word");
        var reading = Reading(scenario.FormId, scenario.MsaId);
        RecordAssessment(scenario, [Word("pasted-word", "analysed", [reading])]);
        var initial = PendingChanges.Load(new PendingChangesRequest(scenario.Path, ProductVersion)).Value!;

        var accepted = PendingChanges.AcceptNewSet(new AcceptNewSetRequest(scenario.Path, ProductVersion,
            initial.Revision, "assessment-accept", Selection: true));

        Assert.False(accepted.Succeeded);
        Assert.Contains("isn't in the FieldWorks project", accepted.Refusal!.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeleteAnalysisProposal_RoundTripsThroughSerializationAndContractParsing()
    {
        var proposalId = CanonicalId.Mint().Value;
        var operationId = CanonicalId.Mint().Value;
        var target = CanonicalId.Mint().Value;
        var draft = new DraftDocument
        {
            ProposalId = proposalId,
            ContractVersions = new Dictionary<string, string> { ["analysis"] = "1.0" },
            Operations =
            [
                new DraftOperation
                {
                    OperationId = operationId,
                    Kind = "analysis/wfiAnalysis/delete",
                    Target = target,
                },
            ],
        };

        var firstRead = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
        var parsedOperation = Assert.Single(firstRead.Operations);
        var roundTrip = ProposalCommands.BuildProposalJson(new DraftDocument
        {
            ProposalId = firstRead.ProposalId.Value,
            ContractVersions = new Dictionary<string, string>(firstRead.ContractVersions),
            Requires = firstRead.Requires.Select(item => item.Value).ToList(),
            Operations = firstRead.Operations.Select(operation => new DraftOperation
            {
                OperationId = operation.OperationId.Value,
                Kind = operation.Kind,
                Target = operation.Target?.Value,
                After = operation.After is { } after
                    ? after.EnumerateObject().ToDictionary(property => property.Name,
                        property => property.Value.Clone(), StringComparer.Ordinal)
                    : new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            }).ToList(),
        });
        var secondRead = ProposalJsonParser.Parse(roundTrip);
        var deletion = Assert.Single(secondRead.Operations);

        Assert.Equal("analysis/wfiAnalysis/delete", deletion.Kind);
        Assert.Equal(operationId, deletion.OperationId.Value);
        Assert.Equal(target, deletion.Target!.Value.Value);
        Assert.Equal(JsonValueKind.Object, deletion.After!.Value.ValueKind);
        Assert.Empty(deletion.After.Value.EnumerateObject());
    }

    [Fact]
    public void RemoveAnalysisNoLongerFitsAfterItsReadingChanges()
    {
        var scenario = NewScenario("remove-changed-reading");
        Guid analysisId = Guid.Empty;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(scenario.Path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>()
                    .GetObject(CanonicalId.Parse(scenario.WordformId).ToGuid());
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(analysis);
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = cache.ServiceLocator.GetInstance<IMoFormRepository>()
                    .GetObject(Guid.Parse(scenario.FormId));
                bundle.MsaRA = cache.ServiceLocator.GetInstance<IMoMorphSynAnalysisRepository>()
                    .GetObject(Guid.Parse(scenario.MsaId));
                analysisId = analysis.Guid;
            });
            new FwDataProjectLoader().Save(cache);
        }
        CaptureBaseline(scenario.Path);
        AssertStoredAnalysisInCurrentBaseline(scenario, analysisId);
        var initial = PendingChanges.Load(new PendingChangesRequest(scenario.Path, ProductVersion)).Value!;
        var staged = PendingChanges.RemoveAnalysis(new RemoveAnalysisRequest(scenario.Path, ProductVersion,
            initial.Revision, CanonicalId.Mint().Value, scenario.WordformId, scenario.Word,
            CanonicalId.FromGuid(analysisId).Value));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        var stagedChange = Assert.Single(staged.Value!.Changes);
        Assert.Equal(ChangeFitStatus.Fits, Assert.Single(staged.Value.FitSummary).Status);

        using (var cache = new FwDataProjectLoader().LoadScratchCache(scenario.Path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(analysisId);
                analysis.MorphBundlesOS[0].MorphRA = cache.ServiceLocator.GetInstance<IMoFormRepository>()
                    .GetObject(pristine.Seed.SecondLexemeFormId);
            });
            new FwDataProjectLoader().Save(cache);
        }
        File.SetLastWriteTimeUtc(scenario.Path, File.GetLastWriteTimeUtc(scenario.Path).AddMinutes(1));

        var changed = PendingChanges.Load(new PendingChangesRequest(scenario.Path, ProductVersion));

        Assert.True(changed.Succeeded, changed.Refusal?.Message);
        Assert.Equal(ChangeFitStatus.NoLongerFits, Assert.Single(changed.Value!.FitSummary).Status);
        Assert.Contains("changed its reading", Assert.Single(changed.Value.FitSummary).Reasons[0],
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(stagedChange.ChangeId, Assert.Single(changed.Value.Changes).ChangeId);
    }

    [Fact]
    public void RemoveAnalysisDeduplicatesSelectionAndCollectsEveryAnalysisUsedByAText()
    {
        var scenario = NewScenario("remove-bulk");
        Guid analysisId = Guid.Empty;
        Guid textId = Guid.Empty;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(scenario.Path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>()
                    .GetObject(CanonicalId.Parse(scenario.WordformId).ToGuid());
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(analysis);
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = cache.ServiceLocator.GetInstance<IMoFormRepository>()
                    .GetObject(Guid.Parse(scenario.FormId));
                bundle.MsaRA = cache.ServiceLocator.GetInstance<IMoMorphSynAnalysisRepository>()
                    .GetObject(Guid.Parse(scenario.MsaId));
                var text = cache.ServiceLocator.GetInstance<ITextFactory>().Create();
                textId = text.Guid;
                text.ContentsOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
                var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                text.ContentsOA.ParagraphsOS.Add(paragraph);
                paragraph.Contents = TsStringUtils.MakeString(scenario.Word, cache.DefaultVernWs);
                paragraph.SegmentsOS[0].AnalysesRS.Add(analysis);
                analysisId = analysis.Guid;
            });
            new FwDataProjectLoader().Save(cache);
        }
        CaptureBaseline(scenario.Path);
        AssertStoredAnalysisInCurrentBaseline(scenario, analysisId);
        var initial = PendingChanges.Load(new PendingChangesRequest(scenario.Path, ProductVersion)).Value!;
        var duplicateIds = new[] { CanonicalId.FromGuid(analysisId).Value, CanonicalId.FromGuid(analysisId).Value };

        var selected = PendingChanges.RemoveAnalysis(new RemoveAnalysisRequest(scenario.Path, ProductVersion,
            initial.Revision, AnalysisIds: duplicateIds));

        Assert.True(selected.Succeeded, selected.Refusal?.Message);
        Assert.Single(selected.Value!.Changes);
        Assert.Single(selected.Value.FitSummary);
        var undone = PendingChanges.Remove(new RemovePendingChangeRequest(scenario.Path, ProductVersion,
            selected.Value.Revision, selected.Value.Changes[0].ChangeId));
        Assert.True(undone.Succeeded, undone.Refusal?.Message);

        var textRemoval = PendingChanges.RemoveAnalysis(new RemoveAnalysisRequest(scenario.Path, ProductVersion,
            undone.Value!.Revision, TextId: textId));

        Assert.True(textRemoval.Succeeded, textRemoval.Refusal?.Message);
        Assert.Single(textRemoval.Value!.Changes);
        Assert.Single(textRemoval.Value.FitSummary);
    }

    private Scenario NewScenario(string word)
    {
        using var cache = pristine.NewScratch();
        var project = cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(pristine.Seed.FirstEntryId);
        var msaId = project.MorphoSyntaxAnalysesOC.First().Guid;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs)).Guid);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        var root = Path.Combine(Path.GetTempPath(), "motif-analysis-markings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var token = CurrentBaseline(path).Baseline.Token;
        return new Scenario(path, word, CanonicalId.FromGuid(wordformId).Value,
            pristine.Seed.FirstLexemeFormId.ToString("D"), msaId.ToString("D"), token);
    }

    private (Scenario Scenario, Guid TextId, string OtherWord) AddTextWithSecondWord(Scenario scenario)
    {
        const string otherWord = "accept-second-word";
        Guid textId = Guid.Empty;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(scenario.Path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var first = cache.ServiceLocator.GetInstance<IWfiWordformRepository>()
                    .GetObject(CanonicalId.Parse(scenario.WordformId).ToGuid());
                var second = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(otherWord, cache.DefaultVernWs));
                var firstAnalysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                var secondAnalysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                first.AnalysesOC.Add(firstAnalysis);
                second.AnalysesOC.Add(secondAnalysis);

                var text = cache.ServiceLocator.GetInstance<ITextFactory>().Create();
                textId = text.Guid;
                text.ContentsOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
                var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                text.ContentsOA.ParagraphsOS.Add(paragraph);
                paragraph.Contents = TsStringUtils.MakeString(scenario.Word + " " + otherWord,
                    cache.DefaultVernWs);
                paragraph.SegmentsOS[0].AnalysesRS.Add(firstAnalysis);
                paragraph.SegmentsOS[0].AnalysesRS.Add(secondAnalysis);
            });
            new FwDataProjectLoader().Save(cache);
        }
        CaptureBaseline(scenario.Path);
        return (scenario with { BaselineToken = CurrentBaseline(scenario.Path).Baseline.Token }, textId, otherWord);
    }

    private static void RecordAssessment(Scenario scenario, IReadOnlyList<AssessedWord> words)
    {
        using var database = OpenDatabase(scenario.Path);
        var selection = Selection.Create("analysis markings", words.Select(word => word.Word));
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            "assessment-accept", null, null, "test", AssessmentKind.ParseTime.ToStoredKind(), "{}",
            "sha256:scope", "whitespace-and-punctuation", "1", JsonSerializer.Serialize(scenario.BaselineToken),
            selection, "sha256:outcome", "sha256:semantic", "sha256:grammar", "test-model", "test", 0,
            words, SavedUtc: "2026-09-28T12:00:00Z"));
    }

    private static void CaptureBaseline(string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-analysis-markings-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
    }

    private static void AssertStoredAnalysisInCurrentBaseline(Scenario scenario, Guid analysisId)
    {
        using (var source = new FwDataProjectLoader().LoadScratchCache(scenario.Path))
        {
            var sourceWordform = source.ServiceLocator.GetInstance<IWfiWordformRepository>()
                .GetObject(CanonicalId.Parse(scenario.WordformId).ToGuid());
            Assert.True(sourceWordform.AnalysesOC.Any(item => item.Guid == analysisId),
                $"Source analyses: {string.Join(",", sourceWordform.AnalysesOC.Select(item => item.Guid))}; " +
                $"expected {analysisId}.");
        }
        using var database = OpenDatabase(scenario.Path);
        var fullPath = Path.GetFullPath(scenario.Path);
        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))!;
        using var cache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
        var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>()
            .GetObject(CanonicalId.Parse(scenario.WordformId).ToGuid());
        Assert.True(wordform.AnalysesOC.Any(item => item.Guid == analysisId),
            $"Baseline analyses: {string.Join(",", wordform.AnalysesOC.Select(item => item.Guid))}; " +
            $"expected {analysisId}.");
        Assert.Equal(analysisId, CanonicalId.Parse(CanonicalId.FromGuid(analysisId).Value).ToGuid());
    }

    private static AssessedWord Word(string form, string outcome, IReadOnlyList<ParseAnalysis> readings,
        bool capped = false, bool timedOut = false)
    {
        var morphology = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, form, 0, capped, timedOut,
            false, readings, []);
        var analyses = readings.Select((_, index) => new ParsedAnalysis(null, [], 0, "sha256:reading-" + index))
            .ToArray();
        return new AssessedWord(form, outcome, analyses) { Morphology = morphology };
    }

    private static ParseAnalysis Reading(string formId, string msaId) =>
        new([new ParseMorph(formId, msaId, null, null)]);

    private static CurrentBaselineTextWords CurrentBaseline(string path)
    {
        using var database = OpenDatabase(path);
        var fullPath = Path.GetFullPath(path);
        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        return new BaselineRepository(database).GetCurrentTextWords(
            ProjectWorkspaceKey.Compute(project), []) ?? throw new InvalidOperationException("No current Baseline.");
    }

    private static MotifDatabase OpenDatabase(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        return MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
    }

    private static string ProductVersion => MotifProductVersion.CurrentText;

    private sealed record Scenario(string Path, string Word, string WordformId, string FormId, string MsaId,
        BaselineToken BaselineToken);
}
