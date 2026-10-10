using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ComposeRecordParsimonyDispositionTests : IDisposable
{
    private const string ProductVersion = "0.1.0";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-record-disposition-" + Guid.NewGuid().ToString("N"));
    private readonly string _projectPath;
    private readonly string _workerRoot;
    private readonly string _preferencePath;
    private readonly CanonicalId _recordTypeId;
    private readonly string[] _memberGuids;
    private readonly string _entryGuid;
    private readonly string _msaId;
    private readonly RecipeObjectIds _recipeObjects;
    private readonly string _negativeCaseId;

    public ComposeRecordParsimonyDispositionTests(PristineProjectFixture pristine)
    {
        Directory.CreateDirectory(_root);
        _workerRoot = Path.Combine(_root, "worker");
        _preferencePath = Path.Combine(_root, "advanced-ai-mode.json");
        _projectPath = pristine.CopyProjectFile();
        using var cache = new FwDataProjectLoader().LoadCache(_projectPath);
        ICmPossibility recordType = null!;
        IMoStemAllomorph duplicate = null!;
        IMoInflAffMsa recipeMsa = null!;
        IMoInflAffMsa secondRecipeMsa = null!;
        IMoInflAffMsa thirdRecipeMsa = null!;
        IMoInflAffMsa fourthRecipeMsa = null!;
        IMoInflAffixSlot recipeSlot = null!;
        IMoInflAffixSlot secondRecipeSlot = null!;
        IMoInflAffixTemplate recipeTemplate = null!;
        IPhNCSegments recipeNaturalClass = null!;
        IMoMorphAdhocProhib[] recipeProhibitions = [];
        string[] alternationMembers = [];
        IWfiWordform caseWordform = null!;
        string[] memberGuids = [];
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var notebook = cache.LangProject.ResearchNotebookOA;
            var recordTypes = notebook.RecTypesOA ?? cache.ServiceLocator
                .GetInstance<ICmPossibilityListFactory>().Create();
            notebook.RecTypesOA = recordTypes;
            recordType = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
            recordTypes.PossibilitiesOS.Add(recordType);
            recordType.Name.set_String(cache.DefaultAnalWs,
                TsStringUtils.MakeString("Motif human judgment", cache.DefaultAnalWs));

            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>()
                .GetObject(pristine.Seed.FirstEntryId);
            var original = Assert.IsAssignableFrom<IMoStemAllomorph>(entry.LexemeFormOA);
            duplicate = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(duplicate);
            duplicate.Form.set_String(cache.DefaultVernWs, "another-form");
            memberGuids = [original.Guid.ToString("D"), duplicate.Guid.ToString("D")];

            var firstEntry = cache.ServiceLocator.GetInstance<ILexEntryRepository>()
                .GetObject(pristine.Seed.FirstEntryId);
            var secondEntry = cache.ServiceLocator.GetInstance<ILexEntryRepository>()
                .GetObject(pristine.Seed.SecondEntryId);
            var category = cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>()
                .GetObject(pristine.Seed.PartOfSpeechId);
            recipeMsa = cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create(
                firstEntry, SandboxGenericMSA.Create(MsaType.kInfl, category));
            secondRecipeMsa = cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create(
                secondEntry, SandboxGenericMSA.Create(MsaType.kInfl, category));
            thirdRecipeMsa = cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create(
                firstEntry, SandboxGenericMSA.Create(MsaType.kInfl, category));
            fourthRecipeMsa = cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create(
                secondEntry, SandboxGenericMSA.Create(MsaType.kInfl, category));
            recipeSlot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
            category.AffixSlotsOC.Add(recipeSlot);
            recipeSlot.Name.set_String(cache.DefaultAnalWs, "Recipe position");
            secondRecipeSlot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
            category.AffixSlotsOC.Add(secondRecipeSlot);
            secondRecipeSlot.Name.set_String(cache.DefaultAnalWs, "Following position");
            recipeMsa.SlotsRC.Add(recipeSlot);
            thirdRecipeMsa.SlotsRC.Add(recipeSlot);
            secondRecipeMsa.SlotsRC.Add(secondRecipeSlot);
            fourthRecipeMsa.SlotsRC.Add(secondRecipeSlot);
            recipeTemplate = cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create();
            category.AffixTemplatesOS.Add(recipeTemplate);
            recipeTemplate.Name.set_String(cache.DefaultAnalWs, "Recipe order");
            recipeTemplate.SuffixSlotsRS.Add(recipeSlot);
            recipeTemplate.SuffixSlotsRS.Add(secondRecipeSlot);

            recipeNaturalClass = cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(recipeNaturalClass);
            recipeNaturalClass.Name.set_String(cache.DefaultAnalWs, "Recipe segment class");

            recipeProhibitions = [
                CreateMorphemeProhibition(cache, recipeMsa, secondRecipeMsa),
                CreateMorphemeProhibition(cache, thirdRecipeMsa, fourthRecipeMsa),
            ];
            alternationMembers = CreateAlternationFamily(cache, category);
            caseWordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString("pka", cache.DefaultVernWs));
        });
        _recordTypeId = CanonicalId.FromGuid(recordType.Guid);
        _memberGuids = memberGuids;
        _entryGuid = pristine.Seed.FirstEntryId.ToString("D");
        _msaId = CanonicalId.FromGuid(cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(pristine.Seed.FirstEntryId).MorphoSyntaxAnalysesOC.First().Guid).Value;
        var caseId = Guid.NewGuid();
        _negativeCaseId = CanonicalId.FromGuid(caseId).Value;
        _recipeObjects = new RecipeObjectIds(
            CanonicalId.FromGuid(recipeMsa.Guid).Value,
            CanonicalId.FromGuid(recipeSlot.Guid).Value,
            CanonicalId.FromGuid(recipeTemplate.Guid).Value,
            CanonicalId.FromGuid(recipeNaturalClass.Guid).Value,
            CanonicalId.FromGuid(cache.ServiceLocator.GetInstance<ILexEntryRepository>()
                .GetObject(pristine.Seed.FirstEntryId).LexemeFormOA!.Guid).Value,
            CanonicalId.FromGuid(cache.ServiceLocator.GetInstance<ILexEntryRepository>()
                .GetObject(pristine.Seed.SecondEntryId).LexemeFormOA!.Guid).Value,
            CanonicalId.FromGuid(caseWordform.Guid).Value,
            recipeProhibitions.Select(item => CanonicalId.FromGuid(item.Guid).Value).ToArray(),
            alternationMembers);
        new FwDataProjectLoader().Save(cache);
    }

    [Theory]
    [InlineData("fix")]
    [InlineData("keep")]
    [InlineData("ask")]
    [InlineData("defer")]
    public async Task AFindingDispositionRunsDryAndSurvivesSaveAndReopen(string disposition)
    {
        var initialization = ProjectInitializationCommand.Initialize(
            new ProjectInitializationRequest(_projectPath, ProjectInitializationCommand.ConfirmationPhrase), _workerRoot);
        Assert.True(initialization.Succeeded, initialization.Refusal?.Message);
        var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_projectPath), _workerRoot);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);

        var reportId = SaveReport(baseline.Value!.Token);

        var draft = "disposition-" + disposition;
        Assert.True(ProposalCommands.New(new NewDraftRequest(_projectPath, ProductVersion, draft,
            "Record a disposition for one frozen finding")).Succeeded);
        new FileAdvancedAiModePreferenceStore(_preferencePath).SetEnabled(true);
        var arguments = new List<string>
        {
            "parsimony", "dispose", "--project", _projectPath, "--report", reportId,
            "--finding", "P-allo-duplicate-form:exact-finding", "--disposition", disposition,
            "--record-type", _recordTypeId.Value, "--draft", draft,
            "--reason", "This choice is intentional.",
        };
        if (disposition == "ask") arguments.AddRange(["--question", "Should these forms remain listed?"]);
        arguments.Add("--json");
        var cli = await CliProcess.RunAsync(CliProcess.CreateStartInfoWithAdvancedAiModePath(
            _workerRoot, null, developerCommands: false, _preferencePath, [.. arguments]));

        Assert.True(cli.ExitCode == 0, cli.Error + Environment.NewLine + cli.Output);
        var composed = ProjectionJson.Deserialize<ComposedOperationsResponse>(cli.Output);
        Assert.NotNull(composed);
        Assert.Equal(7, composed.Operations.Count);
        Assert.All(composed.Operations, operation => Assert.False(string.IsNullOrWhiteSpace(operation.Kind)));
        Assert.True(ProposalCommands.Comment(new CommentRequest(_projectPath, ProductVersion, draft,
            "Record the exact Parsimony finding and retain its evidence with the applied Notebook decision.")).Succeeded);

        var finalized = ProposalCommands.Finalize(new FinalizeRequest(_projectPath, ProductVersion, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposal = ProjectStoreCommand.Run<Proposal>(_projectPath, ProductVersion, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!))).Value!;

        using var live = new FwDataProjectLoader().LoadScratchCache(_projectPath);
        Assert.Empty(HumanJudgmentReader.Read(live).Judgments);

        var dryRun = ScratchDryRun.Of(live, proposal);
        Assert.Contains(dryRun.ExpectedEffects, effect =>
            effect.Field == SnapshotFields.RnGenericRecMotifHumanJudgment &&
            effect.Before.Count == 0 && effect.After.ContainsKey("text"));
        Assert.Empty(HumanJudgmentReader.Read(live).Judgments);

        var receipt = ProposalApplier.Apply(live, proposal, dryRun.Anchor, "reviewer");
        Assert.False(receipt.AlreadyApplied);
        var judgment = Assert.Single(HumanJudgmentReader.Read(live).Judgments).Judgment;
        Assert.Equal(JudgmentActorKind.Agent, judgment.Actor!.Kind);
        Assert.Equal("This choice is intentional.", judgment.Reason);
        var body = Assert.IsType<DispositionJudgment>(judgment.Body);
        Assert.Equal(ParseDisposition(disposition), body.Disposition);
        Assert.Equal("sha256:" + new string('a', 64), body.EvidenceDigest);
        Assert.Equal("allomorph-duplicate-form/v1", body.EvidenceContract);
        Assert.Equal(reportId, judgment.Source!.ReportId);
        Assert.Equal("Repeated allomorph forms", body.MeasureCaption);
        Assert.Contains("motifa", body.SubjectCaption, StringComparison.Ordinal);
        Assert.Contains("another-form", body.SubjectCaption, StringComparison.Ordinal);
        var subject = Assert.IsType<GroupJudgmentSubject>(body.Subject);
        Assert.Equal("P-allo-duplicate-form", subject.MeasureId);
        Assert.Equal(2, subject.Members.Count);
        Assert.All(subject.Members, member => Assert.Equal("MoStemAllomorph", member.Class));
        if (disposition == "ask") Assert.Equal("Should these forms remain listed?", body.Question);
        else Assert.Null(body.Question);

        new FwDataProjectLoader().Save(live);
        live.Dispose();
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_projectPath);
        var reopenedJudgment = Assert.Single(HumanJudgmentReader.Read(reopened).Judgments).Judgment;
        Assert.Equal(HumanJudgmentCodec.LogicalDigest(judgment), HumanJudgmentCodec.LogicalDigest(reopenedJudgment));
        Assert.Equal(ParseDisposition(disposition), Assert.IsType<DispositionJudgment>(reopenedJudgment.Body).Disposition);
    }

    [Theory]
    [MemberData(nameof(RecipeDispositionRoutes))]
    public async Task AFirstWaveRecipeDispositionUsesItsExactFindingWithoutEditingGrammar(
        string measureId, string disposition)
    {
        SeedReviewedNegativeForRecipeFinding();
        var recipe = RecipeRouteDefinitions.Single(item => item.MeasureId == measureId);
        var initialization = ProjectInitializationCommand.Initialize(
            new ProjectInitializationRequest(_projectPath, ProjectInitializationCommand.ConfirmationPhrase), _workerRoot);
        Assert.True(initialization.Succeeded, initialization.Refusal?.Message);
        var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_projectPath), _workerRoot);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        var report = SaveRecipeReport(baseline.Value!.Token, recipe);

        var draft = "recipe-disposition-" + measureId.Replace('-', '_') + "-" + disposition;
        Assert.True(ProposalCommands.New(new NewDraftRequest(_projectPath, ProductVersion, draft,
            "Record a recipe disposition against its exact finding")).Succeeded);
        new FileAdvancedAiModePreferenceStore(_preferencePath).SetEnabled(true);
        var reason = recipe.ReasonFor(disposition);
        var arguments = new List<string>
        {
            "parsimony", "dispose", "--project", _projectPath, "--report", report.ReportId,
            "--finding", report.Finding.FindingId, "--disposition", disposition,
            "--record-type", _recordTypeId.Value, "--draft", draft, "--reason", reason,
        };
        if (disposition == "ask") arguments.AddRange(["--question", recipe.Question]);
        arguments.Add("--json");
        var cli = await CliProcess.RunAsync(CliProcess.CreateStartInfoWithAdvancedAiModePath(
            _workerRoot, null, developerCommands: false, _preferencePath, [.. arguments]));

        Assert.True(cli.ExitCode == 0, cli.Error + Environment.NewLine + cli.Output);
        var composed = ProjectionJson.Deserialize<ComposedOperationsResponse>(cli.Output);
        Assert.NotNull(composed);
        Assert.Equal(7, composed.Operations.Count);
        Assert.True(ProposalCommands.Comment(new CommentRequest(_projectPath, ProductVersion, draft,
            "Retain the exact recipe finding and its evidence in the Notebook judgment.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(_projectPath, ProductVersion, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposal = ProjectStoreCommand.Run<Proposal>(_projectPath, ProductVersion, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!))).Value!;

        using (var live = new FwDataProjectLoader().LoadScratchCache(_projectPath))
        {
            var existingJudgments = HumanJudgmentReader.Read(live).Judgments;
            Assert.Contains(existingJudgments, item => item.Judgment.Body is ReviewedNegativeJudgment negative &&
                negative.CaseId == _negativeCaseId);
            Assert.DoesNotContain(existingJudgments, item => item.Judgment.Body is DispositionJudgment);
            var dryRun = ScratchDryRun.Of(live, proposal);
            Assert.Equal(7, dryRun.ExpectedEffects.Count);
            var effect = Assert.Single(dryRun.ExpectedEffects,
                item => item.Field == SnapshotFields.RnGenericRecMotifHumanJudgment);
            Assert.Equal(new[]
            {
                SnapshotFields.RnResearchNbkRecords,
                SnapshotFields.RnGenericRecType,
                SnapshotFields.RnGenericRecTitle,
                SnapshotFields.RnGenericRecDescription,
                SnapshotFields.StTextParagraphs,
                SnapshotFields.StTxtParaContents,
                SnapshotFields.RnGenericRecMotifHumanJudgment,
            }.Order(StringComparer.Ordinal),
                dryRun.ExpectedEffects.Select(item => item.Field).Order(StringComparer.Ordinal));
            Assert.Empty(effect.Before);
            Assert.Contains("text", effect.After.Keys);
            var receipt = ProposalApplier.Apply(live, proposal, dryRun.Anchor, "reviewer");
            Assert.False(receipt.AlreadyApplied);
            var judgment = Assert.Single(HumanJudgmentReader.Read(live).Judgments,
                item => item.Judgment.Body is DispositionJudgment).Judgment;
            var body = Assert.IsType<DispositionJudgment>(judgment.Body);
            Assert.Equal(measureId, body.MeasureId);
            Assert.Equal(ParseDisposition(disposition), body.Disposition);
            Assert.Equal("sha256:" + report.Finding.EvidenceDigest, body.EvidenceDigest);
            Assert.Equal(MeasureCatalog.Find(measureId)!.QueryId, body.EvidenceContract);
            Assert.Equal(recipe.Question is not null && disposition == "ask" ? recipe.Question : null, body.Question);
            Assert.Equal(reason, judgment.Reason);
            Assert.Equal(report.ReportId, judgment.Source!.ReportId);
            AssertRecipeSubject(body.Subject, report.Finding);
            new FwDataProjectLoader().Save(live);
        }

        var refreshed = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_projectPath), _workerRoot);
        Assert.True(refreshed.Succeeded, refreshed.Refusal?.Message);
        var refreshedReport = SaveRecipeReport(refreshed.Value!.Token, recipe);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_projectPath);
        var reopenedJudgments = HumanJudgmentReader.Read(reopened).Judgments;
        var persisted = Assert.Single(reopenedJudgments,
            item => item.Judgment.Body is DispositionJudgment).Judgment;
        var projection = SIL.Motif.Host.Parsimony.ParsimonyDispositionQuery.Project(
            refreshedReport.BundleId, refreshedReport.ReportId, refreshed.Value.Token.BundleDigest,
            [refreshedReport.Finding], JudgmentLineageResolver.Resolve(HumanJudgmentReader.Read(reopened)));
        var row = Assert.Single(projection.Findings);
        Assert.Equal(disposition == "ask" ? "active" : "suppressed", row.State);
        Assert.Equal(persisted.Reason, row.Reason);

        if (disposition == "keep")
        {
            var changed = refreshedReport.Finding with { EvidenceDigest = new string('e', 64) };
            var resurfaced = SIL.Motif.Host.Parsimony.ParsimonyDispositionQuery.Project(
                refreshedReport.BundleId, refreshedReport.ReportId, refreshed.Value.Token.BundleDigest,
                [changed], JudgmentLineageResolver.Resolve(HumanJudgmentReader.Read(reopened)));
            var resurfacedFinding = Assert.Single(resurfaced.Findings);
            Assert.Equal("resurfaced", resurfacedFinding.State);
            Assert.Equal(recipe.KeepReason, resurfacedFinding.Reason);
            Assert.Equal(1, resurfaced.ResurfacedCount);
        }
    }

    public static IEnumerable<object[]> RecipeDispositionRoutes()
    {
        foreach (var recipe in RecipeRouteDefinitions)
        {
            foreach (var disposition in new[] { "keep", "ask", "defer" })
            {
                if (disposition == "keep" && recipe.KeepAlreadyCovered) continue;
                yield return [recipe.MeasureId, disposition];
            }
        }
    }

    [Fact]
    public void NotebookRecordTypesListReadableNamesAndPortableIds()
    {
        var result = ParsimonyCommands.ListNotebookRecordTypes(
            new ListNotebookRecordTypesRequest(_projectPath, ProductVersion));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var recordType = Assert.Single(result.Value!.RecordTypes);
        Assert.Equal(_recordTypeId.Value, recordType.Id);
        Assert.Equal("Motif human judgment", recordType.Name);
    }

    [Fact]
    public void AFindingAbsentFromTheStoredReportIsRefused()
    {
        var initialization = ProjectInitializationCommand.Initialize(
            new ProjectInitializationRequest(_projectPath, ProjectInitializationCommand.ConfirmationPhrase), _workerRoot);
        Assert.True(initialization.Succeeded, initialization.Refusal?.Message);
        var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_projectPath), _workerRoot);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        var reportId = SaveReport(baseline.Value!.Token);

        var result = ParsimonyCommands.RecordDisposition(new RecordParsimonyDispositionFromFindingRequest(
            _projectPath, ProductVersion, reportId, "P-allo-duplicate-form:other", "keep",
            _recordTypeId.Value, "missing-finding"));

        Assert.False(result.Succeeded);
        Assert.Equal("parsimony.finding-not-in-report", result.Refusal!.Code);
    }

    [Fact]
    public void DisposeRefusesAFindingIdThatMatchesTwoFindings()
    {
        var initialization = ProjectInitializationCommand.Initialize(
            new ProjectInitializationRequest(_projectPath, ProjectInitializationCommand.ConfirmationPhrase), _workerRoot);
        Assert.True(initialization.Succeeded, initialization.Refusal?.Message);
        var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_projectPath), _workerRoot);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        var reportId = SaveReport(baseline.Value!.Token, findingCopies: 2);

        var result = ParsimonyCommands.RecordDisposition(new RecordParsimonyDispositionFromFindingRequest(
            _projectPath, ProductVersion, reportId, "P-allo-duplicate-form:exact-finding", "keep",
            _recordTypeId.Value, "ambiguous-finding"));

        Assert.False(result.Succeeded);
        Assert.Equal("parsimony.finding-ambiguous", result.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, result.Refusal.Reason);
    }

    [Fact]
    public void AnIdenticalDecisionAlreadyStagedIsRefused()
    {
        var reportId = StageReportAndDraft("staged-twice");
        Assert.True(ParsimonyCommands.RecordDisposition(DispositionRequest(reportId, "staged-twice", "keep")).Succeeded);

        var result = ParsimonyCommands.RecordDisposition(DispositionRequest(reportId, "staged-twice", "keep"));

        Assert.False(result.Succeeded);
        Assert.Equal("parsimony.decision-already-staged", result.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, result.Refusal.Reason);
    }

    [Fact]
    public void AnIdenticalDecisionAlreadyAppliedReturnsAlreadyRecorded()
    {
        var reportId = StageReportAndDraft("applied-first");
        Assert.True(ParsimonyCommands.RecordDisposition(DispositionRequest(reportId, "applied-first", "keep")).Succeeded);
        Assert.True(ProposalCommands.Comment(new CommentRequest(_projectPath, ProductVersion, "applied-first",
            "Record the exact Parsimony finding.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(_projectPath, ProductVersion, "applied-first"));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        Assert.True(DryRunJobRunner.Run(_projectPath, ProductVersion, finalized.Value!.ProposalId).Succeeded);
        var applied = ProposalCommands.Apply(new ApplyRequest(_projectPath, ProductVersion,
            finalized.Value.ProposalId, "reviewer"));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);

        Assert.True(ProposalCommands.New(new NewDraftRequest(_projectPath, ProductVersion, "applied-again",
            "Record the same decision again")).Succeeded);

        var result = ParsimonyCommands.RecordDisposition(DispositionRequest(reportId, "applied-again", "keep"));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal("already-recorded", result.Value!.Outcome);
        Assert.Empty(result.Value.Operations);
        Assert.Equal(0, result.Value.OperationCount);
        using var json = JsonDocument.Parse(ProjectionJson.Serialize(result.Value));
        Assert.Equal("already-recorded", json.RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public void AChangedDecisionIsStagedAsANewDecision()
    {
        var reportId = StageReportAndDraft("changed");
        Assert.True(ParsimonyCommands.RecordDisposition(DispositionRequest(reportId, "changed", "keep")).Succeeded);

        var changed = ParsimonyCommands.RecordDisposition(DispositionRequest(reportId, "changed", "fix"));

        Assert.True(changed.Succeeded, changed.Refusal?.Message);
        Assert.Equal(7, changed.Value!.Operations.Count);
        Assert.Null(changed.Value.Outcome);
    }

    [Fact]
    public void AnIdenticalDecisionFromALaterReportIsAlreadyRecorded()
    {
        var earlierReportId = StageReportAndDraft("earlier-report");
        Assert.True(ParsimonyCommands.RecordDisposition(DispositionRequest(earlierReportId, "earlier-report", "keep")).Succeeded);
        Assert.True(ProposalCommands.Comment(new CommentRequest(_projectPath, ProductVersion, "earlier-report",
            "Record the exact Parsimony finding.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(_projectPath, ProductVersion, "earlier-report"));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        Assert.True(DryRunJobRunner.Run(_projectPath, ProductVersion, finalized.Value!.ProposalId).Succeeded);
        var applied = ProposalCommands.Apply(new ApplyRequest(_projectPath, ProductVersion,
            finalized.Value.ProposalId, "reviewer"));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);

        var refreshed = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_projectPath), _workerRoot);
        Assert.True(refreshed.Succeeded, refreshed.Refusal?.Message);
        var laterReportId = SaveReport(refreshed.Value!.Token);
        Assert.NotEqual(earlierReportId, laterReportId);
        Assert.True(ProposalCommands.New(new NewDraftRequest(_projectPath, ProductVersion, "later-report",
            "Record the same decision from a later Report")).Succeeded);

        var result = ParsimonyCommands.RecordDisposition(DispositionRequest(laterReportId, "later-report", "keep"));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal("already-recorded", result.Value!.Outcome);
        Assert.Empty(result.Value.Operations);
    }

    [Fact]
    public void AnIdenticalDecisionFromALaterReportIsAlreadyStaged()
    {
        var earlierReportId = StageReportAndDraft("staged-earlier");
        Assert.True(ParsimonyCommands.RecordDisposition(DispositionRequest(earlierReportId, "staged-earlier", "keep")).Succeeded);
        var refreshed = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_projectPath), _workerRoot);
        Assert.True(refreshed.Succeeded, refreshed.Refusal?.Message);
        var laterReportId = SaveReport(refreshed.Value!.Token);
        Assert.NotEqual(earlierReportId, laterReportId);

        var result = ParsimonyCommands.RecordDisposition(DispositionRequest(laterReportId, "staged-earlier", "keep"));

        Assert.False(result.Succeeded);
        Assert.Equal("parsimony.decision-already-staged", result.Refusal!.Code);
    }

    private string StageReportAndDraft(string draft)
    {
        var initialization = ProjectInitializationCommand.Initialize(
            new ProjectInitializationRequest(_projectPath, ProjectInitializationCommand.ConfirmationPhrase), _workerRoot);
        Assert.True(initialization.Succeeded, initialization.Refusal?.Message);
        var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_projectPath), _workerRoot);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        var reportId = SaveReport(baseline.Value!.Token);
        Assert.True(ProposalCommands.New(new NewDraftRequest(_projectPath, ProductVersion, draft,
            "Record a Parsimony decision")).Succeeded);
        return reportId;
    }

    private RecordParsimonyDispositionFromFindingRequest DispositionRequest(string reportId, string draft,
        string disposition) => new(_projectPath, ProductVersion, reportId, "P-allo-duplicate-form:exact-finding",
            disposition, _recordTypeId.Value, draft, "This choice is intentional.");

    [Fact]
    public void AReportFromAnotherBaselineIsRefused()
    {
        var initialization = ProjectInitializationCommand.Initialize(
            new ProjectInitializationRequest(_projectPath, ProjectInitializationCommand.ConfirmationPhrase), _workerRoot);
        Assert.True(initialization.Succeeded, initialization.Refusal?.Message);
        var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_projectPath), _workerRoot);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        var token = baseline.Value!.Token;
        var otherBaseline = new SIL.Motif.Contract.Baselines.BaselineToken(token.ProjectIdentity,
            "sha256:" + new string('e', 64), token.ProjectionVersion, token.CapturedUtc, token.BundleDigest,
            token.CapturedHostSessionId, token.CapturedEditGeneration);
        var reportId = SaveReport(otherBaseline);

        var result = ParsimonyCommands.RecordDisposition(new RecordParsimonyDispositionFromFindingRequest(
            _projectPath, ProductVersion, reportId, "P-allo-duplicate-form:exact-finding", "keep",
            _recordTypeId.Value, "wrong-baseline"));

        Assert.False(result.Succeeded);
        Assert.Equal("parsimony.report-baseline-mismatch", result.Refusal!.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void FailureAfterEachDispositionWriteRollsBackTheWholeNotebookRevision(int failAfter)
    {
        var proposal = CreateFinalizedDispositionProposal("rollback-" + failAfter);
        using (var cache = new FwDataProjectLoader().LoadScratchCache(_projectPath))
        {
            var dryRun = ScratchDryRun.Of(cache, proposal);
            var appliedOperations = 0;

            var failure = Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(
                cache, proposal, dryRun.Anchor, "reviewer", description: "",
                afterOperation: (index, _) =>
                {
                    appliedOperations = index;
                    if (index == failAfter) throw new InvalidOperationException("injected operation failure");
                }));

            Assert.Equal("injected operation failure", failure.Message);
            Assert.Equal(failAfter, appliedOperations);
        }

        using var reopened = new FwDataProjectLoader().LoadScratchCache(_projectPath);
        Assert.Empty(reopened.LangProject.ResearchNotebookOA!.RecordsOC);
        Assert.Empty(HumanJudgmentReader.Read(reopened).Judgments);
        Assert.Empty(ProjectAppliedLog.ReadAll(reopened));
    }

    [Fact]
    public void ApplyRefusesWhenTheReservedJudgmentFieldSchemaChangesAfterDryRun()
    {
        var proposal = CreateFinalizedDispositionProposal("schema-drift");
        SIL.Motif.Model.DryRun.BoundDryRunAnchor anchor;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(_projectPath))
            anchor = ScratchDryRun.Of(cache, proposal).Anchor;
        using (var database = ProjectMotifDatabase.Open(_projectPath))
            new ProposalRepository(database).SetAnchor(proposal.ProposalId, JsonSerializer.Serialize(anchor));

        new FieldWorksSimulator(_projectPath).SaveEdit(cache =>
        {
            var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                metadata.AddCustomField("LexEntry", "MotifHumanJudgment", CellarPropertyType.String, 0,
                    "Drifted duplicate", WritingSystemServices.kwsAnal, Guid.Empty));
        });

        var result = ProposalCommands.Apply(new ApplyRequest(_projectPath, ProductVersion,
            proposal.ProposalId.Value, "reviewer", Force: true));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.drift", result.Refusal!.Code);
        Assert.Contains("reserved judgment field", result.Refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreflightRefusesWhenTheJudgmentFieldWasRenamed()
    {
        var proposal = CreateFinalizedDispositionProposal("preflight-renamed");
        EditReservedJudgmentField("MotifHumanJudgmentRenamed");

        AssertPreflightRefusesDrift(proposal);
    }

    [Fact]
    public void PreflightRefusesWhenTheJudgmentFieldWasDeleted()
    {
        var proposal = CreateFinalizedDispositionProposal("preflight-deleted");
        EditReservedJudgmentField(replacementName: null);

        AssertPreflightRefusesDrift(proposal);
    }

    private void AssertPreflightRefusesDrift(Proposal proposal)
    {
        var bytesBefore = File.ReadAllBytes(_projectPath);

        var result = ProposalCommands.Preflight(new PreflightRequest(_projectPath, ProductVersion,
            proposal.ProposalId.Value));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.drift", result.Refusal!.Code);
        Assert.Contains("reserved judgment field", result.Refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(bytesBefore, File.ReadAllBytes(_projectPath));
    }

    private void EditReservedJudgmentField(string? replacementName)
    {
        new FieldWorksSimulator(_projectPath).SaveEdit(cache =>
        {
            var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
            var field = metadata.GetFieldIds().Single(flid =>
                string.Equals(metadata.GetFieldName(flid), "MotifHumanJudgment", StringComparison.Ordinal));
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                metadata.DeleteCustomField(field);
                if (replacementName is not null)
                    metadata.AddCustomField("RnGenericRec", replacementName, CellarPropertyType.String, 0,
                        "Renamed judgment", WritingSystemServices.kwsAnal, Guid.Empty);
            });
        });
    }

    [Fact]
    public void JudgmentOnlyProposalAppliesWithoutCorrectnessAssessmentAndExplainsReadiness()
    {
        var proposal = CreateFinalizedDispositionProposal("judgment-readiness");
        var baselineBeforeDryRun = CurrentBaselineToken();
        var dryRun = DryRunJobRunner.Run(_projectPath, ProductVersion, proposal.ProposalId.Value);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var job = Assert.Single(ReadDryRunJobs(proposal.ProposalId.Value));
        var dryRunInput = DryRunJobInput.Parse(job.InputJson);
        Assert.Equal(ReadFinalizedProposalJson(proposal.ProposalId), dryRunInput.Proposal.ProposalJson);
        Assert.Equal(ReadFinalizedIntentDigest(proposal.ProposalId), dryRunInput.Proposal.IntentDigest);
        Assert.Equal(SIL.Motif.Contract.Canonicalization.IntentDigest.Compute(proposal),
            dryRunInput.Proposal.IntentDigest);
        Assert.NotNull(dryRunInput.SourceBaseline);
        Assert.True(baselineBeforeDryRun.HasSameSemanticIdentity(dryRunInput.SourceBaseline.Token));
        Assert.True(baselineBeforeDryRun.HasSameSemanticIdentity(CurrentBaselineToken()));
        var completion = DryRunJobCompletion.Parse(job.ResultJson!);
        Assert.Equal(dryRunInput.SourceBaseline, completion.SourceBaseline);
        Assert.True(baselineBeforeDryRun.HasSameSemanticIdentity(completion.BaselineToken));
        Assert.Contains(completion.Freshness, new[] { "current", "currentness-not-checked" });
        var publishedDryRun = JobCommands.ParsePublishedDryRun(job.DryRunJson!);
        var boundAnchor = ReadFinalizedAnchor(proposal.ProposalId);
        Assert.Equal(boundAnchor, publishedDryRun.Anchor);
        Assert.Equal(boundAnchor.IntentDigest, publishedDryRun.IntentDigest);
        Assert.Equal(ReadFinalizedIntentDigest(proposal.ProposalId), boundAnchor.IntentDigest);
        Assert.Equal(boundAnchor.EffectDigest, publishedDryRun.EffectDigest);
        Assert.Equal(ExpectedEffectSetDigest.Compute(publishedDryRun.ExpectedEffects), publishedDryRun.EffectDigest);
        Assert.Equal(JudgmentOnlyProposalReadiness.ExemptionReason,
            JudgmentOnlyProposalReadiness.ExemptionFor(proposal, publishedDryRun.ExpectedEffects));

        var result = ProposalCommands.Apply(new ApplyRequest(
            _projectPath, ProductVersion, proposal.ProposalId.Value, "reviewer"));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var readiness = Assert.IsType<ReadinessProjection>(result.Value!.Readiness);
        Assert.False(readiness.CorrectnessAssessmentRequired);
        Assert.Contains("Dry Run", readiness.CorrectnessAssessmentExemptionReason, StringComparison.Ordinal);
        Assert.Contains("Notebook", readiness.CorrectnessAssessmentExemptionReason, StringComparison.Ordinal);
        Assert.Contains("Correctness Assessment not required", result.Value.ResultNote, StringComparison.Ordinal);
        Assert.Contains("Correctness Assessment not required", SIL.Motif.Projection.Rendering.CommandTextRenderer.Render(result.Value), StringComparison.Ordinal);
    }

    [Fact]
    public void JudgmentAndGrammarProposalStillRequiresCorrectnessAssessment()
    {
        var proposal = CreateFinalizedDispositionProposal("mixed-judgment-grammar", includeGrammar: true);
        var dryRun = DryRunJobRunner.Run(_projectPath, ProductVersion, proposal.ProposalId.Value);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        Assert.Contains(dryRun.Value!.Effects, effect => effect.Field.StartsWith("grammar/", StringComparison.Ordinal));

        var result = ProposalCommands.Apply(new ApplyRequest(
            _projectPath, ProductVersion, proposal.ProposalId.Value, "reviewer"));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.not-ready", result.Refusal!.Code);
        Assert.Contains("Apply requires a Correctness Assessment", result.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnexpectedGrammarEffectInBoundDryRunDisablesJudgmentExemption()
    {
        var proposal = CreateFinalizedDispositionProposal("unexpected-grammar-effect");
        var dryRun = DryRunJobRunner.Run(_projectPath, ProductVersion, proposal.ProposalId.Value);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var injectedEffect = new ExpectedEffect(CanonicalId.Parse(_entryGuid), "grammar/moStemMsa/msFeatures",
            new Dictionary<string, string>(), new Dictionary<string, string> { ["ref"] = _msaId });
        var jobs = ReadDryRunJobs(proposal.ProposalId.Value);
        var job = Assert.Single(jobs);
        var effects = ReadExpectedEffects(job.DryRunJson!).Append(injectedEffect).ToArray();
        var effectDigest = ExpectedEffectSetDigest.Compute(effects);
        var published = JsonNode.Parse(job.DryRunJson!)!;
        var anchor = JsonSerializer.Deserialize<BoundDryRunAnchor>(
            published["anchor"]!.ToJsonString(), MotifJson.CreateOptions())!;
        published["expectedEffects"] = JsonNode.Parse(ExpectedEffectSetJsonWriter.WritePublishedJson(effects));
        published["effectDigest"] = effectDigest;
        published["anchor"]!["effectDigest"] = effectDigest;
        using (var database = ProjectMotifDatabase.Open(_projectPath))
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Jobs SET DryRunJson = $json WHERE JobId = $job;";
            command.Parameters.AddWithValue("$json", published.ToJsonString());
            command.Parameters.AddWithValue("$job", job.JobId);
            Assert.Equal(1, command.ExecuteNonQuery());
            new ProposalRepository(database).SetAnchor(proposal.ProposalId,
                JsonSerializer.Serialize(anchor with { EffectDigest = effectDigest }));
        }

        var result = ProposalCommands.Apply(new ApplyRequest(
            _projectPath, ProductVersion, proposal.ProposalId.Value, "reviewer"));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.not-ready", result.Refusal!.Code);
        Assert.Contains("Apply requires a Correctness Assessment", result.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedDryRunSourceBindingDoesNotReceiveJudgmentExemption()
    {
        var proposal = CreateFinalizedDispositionProposal("changed-judgment-dry-run-source");
        var dryRun = DryRunJobRunner.Run(_projectPath, ProductVersion, proposal.ProposalId.Value);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var job = Assert.Single(ReadDryRunJobs(proposal.ProposalId.Value));
        var completion = JsonNode.Parse(job.ResultJson!)!;
        completion["sourceBaseline"]!["SourceSha256"] = "sha256:" + new string('0', 64);
        using (var database = ProjectMotifDatabase.Open(_projectPath))
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Jobs SET ResultJson = $json WHERE JobId = $job;";
            command.Parameters.AddWithValue("$json", completion.ToJsonString());
            command.Parameters.AddWithValue("$job", job.JobId);
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var result = ProposalCommands.Apply(new ApplyRequest(
            _projectPath, ProductVersion, proposal.ProposalId.Value, "reviewer"));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.not-ready", result.Refusal!.Code);
        Assert.Contains("Apply requires a Correctness Assessment", result.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JudgmentProposalWithStaleDryRunRequiresCorrectnessAssessment()
    {
        var proposal = CreateFinalizedDispositionProposal("stale-judgment-dry-run");
        var dryRun = DryRunJobRunner.Run(_projectPath, ProductVersion, proposal.ProposalId.Value);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        new FieldWorksSimulator(_projectPath).SaveEdit(cache =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>()
                .GetObject(Guid.Parse(_entryGuid));
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                entry.CitationForm.set_String(cache.DefaultAnalWs,
                    TsStringUtils.MakeString("changed after dry run", cache.DefaultAnalWs)));
        });
        var refreshed = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_projectPath), _workerRoot);
        Assert.True(refreshed.Succeeded, refreshed.Refusal?.Message);

        var result = ProposalCommands.Apply(new ApplyRequest(
            _projectPath, ProductVersion, proposal.ProposalId.Value, "reviewer"));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.not-ready", result.Refusal!.Code);
        Assert.Contains("Apply requires a Correctness Assessment", result.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDryRunDoesNotReceiveJudgmentExemption()
    {
        var proposal = CreateFinalizedDispositionProposal("missing-judgment-dry-run");

        var result = ProposalCommands.Apply(new ApplyRequest(
            _projectPath, ProductVersion, proposal.ProposalId.Value, "reviewer"));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.dry-run-missing", result.Refusal!.Code);
    }

    [Fact]
    public async Task ApplyThroughCliRefusesWhileFieldWorksHoldsTheProject()
    {
        var proposal = CreateFinalizedDispositionProposal("fieldworks-lock");
        using (var cache = new FwDataProjectLoader().LoadScratchCache(_projectPath))
        {
            var anchor = ScratchDryRun.Of(cache, proposal).Anchor;
            using var database = ProjectMotifDatabase.Open(_projectPath);
            new ProposalRepository(database).SetAnchor(proposal.ProposalId, JsonSerializer.Serialize(anchor));
        }

        using var owner = new FieldWorksSimulator(_projectPath).Hold();
        var cli = await CliProcess.RunAsync(CliProcess.CreateStartInfoWithAdvancedAiModePath(
            _workerRoot, null, developerCommands: true, _preferencePath,
            "apply", "--project", _projectPath, proposal.ProposalId.Value, "--user", "reviewer", "--force", "--json"));

        Assert.NotEqual(0, cli.ExitCode);
        Assert.Contains("apply.project-in-use", cli.Output + cli.Error, StringComparison.Ordinal);
    }

    private Proposal CreateFinalizedDispositionProposal(string draft, bool includeGrammar = false)
    {
        var initialization = ProjectInitializationCommand.Initialize(
            new ProjectInitializationRequest(_projectPath, ProjectInitializationCommand.ConfirmationPhrase), _workerRoot);
        Assert.True(initialization.Succeeded, initialization.Refusal?.Message);
        var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_projectPath), _workerRoot);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        var reportId = SaveReport(baseline.Value!.Token);
        Assert.True(ProposalCommands.New(new NewDraftRequest(_projectPath, ProductVersion, draft,
            "Record one exact Parsimony finding")).Succeeded);
        var composed = ParsimonyCommands.RecordDisposition(new RecordParsimonyDispositionFromFindingRequest(
            _projectPath, ProductVersion, reportId, "P-allo-duplicate-form:exact-finding", "keep",
            _recordTypeId.Value, draft, "This choice is intentional."));
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        if (includeGrammar)
        {
            var grammar = ProposalCommands.ComposeAuthorFeatureStructure(new ComposeAuthorFeatureStructureRequest(
                _projectPath, ProductVersion, draft, JsonSerializer.Serialize(new { msa = _msaId })));
            Assert.True(grammar.Succeeded, grammar.Refusal?.Message);
        }
        Assert.True(ProposalCommands.Comment(new CommentRequest(_projectPath, ProductVersion, draft,
            "Retain the exact finding evidence with the Notebook decision.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(_projectPath, ProductVersion, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var parsed = ProjectStoreCommand.Run<Proposal>(_projectPath, ProductVersion, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(parsed.Succeeded, parsed.Refusal?.Message);
        return parsed.Value!;
    }

    private IReadOnlyList<JobRecord> ReadDryRunJobs(string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_projectPath);
        var full = Path.GetFullPath(_projectPath);
        var project = new ProjectLocator(full, Path.GetFileNameWithoutExtension(full));
        var jobs = new JobRepository(database).ListByProjectAndKind(
            ProjectWorkspaceKey.Compute(project), JobCommands.DryRunKind);
        return jobs.Where(job => job.InputJson.Contains(proposalId, StringComparison.Ordinal) &&
            job.DryRunJson is not null).ToArray();
    }

    private SIL.Motif.Contract.Baselines.BaselineToken CurrentBaselineToken()
    {
        using var database = ProjectMotifDatabase.Open(_projectPath);
        var full = Path.GetFullPath(_projectPath);
        var project = new ProjectLocator(full, Path.GetFileNameWithoutExtension(full));
        return new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))!.Token;
    }

    private string ReadFinalizedProposalJson(CanonicalId proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_projectPath);
        var (record, _) = new ProposalRepository(database).GetFinalized(proposalId);
        return record.ProposalJson!;
    }

    private string ReadFinalizedIntentDigest(CanonicalId proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_projectPath);
        var (record, _) = new ProposalRepository(database).GetFinalized(proposalId);
        return record.IntentDigest!;
    }

    private SIL.Motif.Model.DryRun.BoundDryRunAnchor ReadFinalizedAnchor(CanonicalId proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_projectPath);
        var (record, _) = new ProposalRepository(database).GetFinalized(proposalId);
        return ProposalRecordMapping.ToManifest(record).Anchor!;
    }

    private static IReadOnlyList<ExpectedEffect> ReadExpectedEffects(string dryRunJson)
    {
        using var document = JsonDocument.Parse(dryRunJson);
        return document.RootElement.GetProperty("expectedEffects").EnumerateArray()
            .Select(effect => new ExpectedEffect(
                CanonicalId.Parse(effect.GetProperty("canonicalId").GetString()!),
                effect.GetProperty("field").GetString()!,
                ReadAlternatives(effect.GetProperty("before")),
                ReadAlternatives(effect.GetProperty("after")),
                effect.TryGetProperty("preview", out var preview) ? preview.Clone() : null))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> ReadAlternatives(JsonElement values) =>
        values.EnumerateObject().ToDictionary(property => property.Name,
            property => property.Value.GetString() ?? "", StringComparer.Ordinal);

    private RecipeReport SaveRecipeReport(SIL.Motif.Contract.Baselines.BaselineToken baseline,
        RecipeRoute recipe)
    {
        const string bundleId = "bundle/recipe-disposition-test";
        var measure = MeasureCatalog.Find(recipe.MeasureId)!;
        var (attachment, groupKey, findingId, references) = recipe.MeasureId switch
        {
            "P-adhoc-duplicate" => Authored(ParsimonyAuthoredObjectKind.AdhocProhibition,
                _recipeObjects.AdhocProhibitionIds[0], "adhoc-context"),
            "R-word-negative-accepted" => WordCase(_negativeCaseId,
                "reviewed-negative/" + _negativeCaseId, recipe.MeasureId + ":" + _negativeCaseId + ":revision",
                "parser-cases", new { caseKey = "negative/" + _negativeCaseId }),
            "R-word-disapproved-produced" => WordCase(_recipeObjects.CaseWordformId,
                "wordform/" + _recipeObjects.CaseWordformId,
                recipe.MeasureId + ":" + _recipeObjects.CaseWordformId + ":disapproved-signature",
                "parser-cases", new { caseKey = "disapproved/" + _recipeObjects.CaseWordformId }),
            "B-affix-unslotted" => Authored(ParsimonyAuthoredObjectKind.InflectionalMsa,
                _recipeObjects.InflectionalMsaId, "unslotted-affixes"),
            "R-tmpl-precedence" => Authored(ParsimonyAuthoredObjectKind.AffixTemplate,
                _recipeObjects.TemplateId, "template-order"),
            "R-slot-blocking" => Authored(ParsimonyAuthoredObjectKind.AffixSlot,
                _recipeObjects.SlotId, "slot-context"),
            "R-allo-unconditioned" => Authored(ParsimonyAuthoredObjectKind.Allomorph,
                _recipeObjects.FirstAllomorphId, "allomorph-context"),
            "R-env-broad" => Authored(ParsimonyAuthoredObjectKind.Allomorph,
                _recipeObjects.FirstAllomorphId, "environment-excess"),
            "R-nc-excess" => Authored(ParsimonyAuthoredObjectKind.NaturalClass,
                _recipeObjects.NaturalClassId, "natural-class-excess"),
            "P-allo-alternation-family" => AlternationFamily(),
            "B-adhoc-is-slot-order" => AdhocSlotOrder(),
            "B-affix-null-vs-optional" => Authored(ParsimonyAuthoredObjectKind.InflectionalMsa,
                _recipeObjects.InflectionalMsaId, "null-optional"),
            _ => throw new ArgumentOutOfRangeException(nameof(recipe), recipe.MeasureId,
                "The recipe fixture has no registered finding shape."),
        };
        var finding = new ParsimonyFinding(findingId, measure.Id, measure.Axis, measure.Tier, attachment,
            groupKey, new ParsimonyMeasureNumber(1, 2, measure.Unit), measure.Threshold!,
            new string('a', 64), references, measure.RecipeLink,
            ["The finding carries the recipe-specific evidence references and its stated limitation."],
            ParsimonyVerification.NotRun);
        var reportId = CanonicalId.Mint("report/").Value;
        var inputs = new ParsimonyReportInputs(bundleId, baseline, "baseline", null,
            "sha256:" + new string('b', 64), new ParsimonyArtifactDigest(6, new string('c', 64)),
            new ParsimonyArtifactDigest(1, new string('d', 64)), null, null, []);
        var response = new ParsimonyReportResponse(reportId, inputs, [finding],
            "A recipe finding with its current evidence.")
        {
            Notes = [],
            JoinQuality = new ParsimonyJoinQuality("project-approved", true, 0, 0, 0, 0, 0, 0, 0,
                null, null, null, null, null),
        };
        var saved = ProjectStoreCommand.Run(_projectPath, ProductVersion, (database, _) =>
        {
            new ReportRepository(database).Save(new ReportRecord(reportId, null, null,
                JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                null, "parsimony", response.Text));
            return CommandOutcome<ParsimonyReportResponse>.Success(response);
        });
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        return new RecipeReport(reportId, bundleId, finding);

        (ParsimonyFindingAttachment Attachment, string? GroupKey, string FindingId,
            IReadOnlyList<ParsimonyEvidenceReference> References) Authored(
            ParsimonyAuthoredObjectKind kind, string identity, string view)
        {
            var evidence = Evidence(bundleId, view, new { objectGuid = identity });
            return (new ParsimonyFindingAttachment(ParsimonyAttachmentKind.AuthoredObject, identity,
                AuthoredObjectKind: kind), null, recipe.MeasureId + ":" + identity, [evidence]);
        }

        (ParsimonyFindingAttachment Attachment, string? GroupKey, string FindingId,
            IReadOnlyList<ParsimonyEvidenceReference> References) WordCase(
            string identity, string caseGroup, string finding, string view, object arguments) =>
            (new ParsimonyFindingAttachment(ParsimonyAttachmentKind.WordCase, identity), caseGroup, finding,
                [Evidence(bundleId, view, arguments)]);

        (ParsimonyFindingAttachment Attachment, string? GroupKey, string FindingId,
            IReadOnlyList<ParsimonyEvidenceReference> References) AlternationFamily()
        {
            const string key = "change/n-m/context/bilabial/stem";
            var references = _recipeObjects.AlternationMemberIds.Select(identity =>
                Evidence(bundleId, "allomorph-context", new { objectGuid = identity })).ToArray();
            return (new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, key,
                    GroupKind: ParsimonyGroupKind.AlternationFamily), key,
                recipe.MeasureId + ":family/n-m/context/bilabial", references);
        }

        (ParsimonyFindingAttachment Attachment, string? GroupKey, string FindingId,
            IReadOnlyList<ParsimonyEvidenceReference> References) AdhocSlotOrder()
        {
            const string key = "category/suffix/A-before-B";
            var references = _recipeObjects.AdhocProhibitionIds.Select(identity =>
                    Evidence(bundleId, "adhoc-context", new { objectGuid = identity }))
                .Append(Evidence(bundleId, "template-order", new { objectGuid = _recipeObjects.TemplateId }))
                .Append(Evidence(bundleId, "approved-morph-sequences",
                    new { objectGuid = _recipeObjects.InflectionalMsaId }))
                .ToArray();
            return (new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group,
                    "adhoc-slot-order/" + key, GroupKind: ParsimonyGroupKind.AdhocSlotOrder), key,
                recipe.MeasureId + ":theme/suffix/A-before-B", references);
        }
    }

    private void SeedReviewedNegativeForRecipeFinding()
    {
        using var cache = new FwDataProjectLoader().LoadCache(_projectPath);
        var recordId = Guid.NewGuid();
        var field = NotebookJudgmentFixture.InitializeReservedField(cache);
        var negative = new HumanJudgment(CanonicalId.FromGuid(cache.LangProject.Guid).Value,
            CanonicalId.FromGuid(recordId).Value, CanonicalId.FromGuid(recordId).Value, [],
            new ReviewedNegativeJudgment(_negativeCaseId,
                cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs), "tkap", "standard dialect",
                new SurfaceNegativeTarget()), "The reviewed slot swap is impossible.",
            new JudgmentActor(JudgmentActorKind.Human));
        NotebookJudgmentFixture.AddRecord(cache, recordId, field,
            TsStringUtils.MakeString(HumanJudgmentCodec.Format(negative), cache.DefaultAnalWs));
        new FwDataProjectLoader().Save(cache);
    }

    private static ParsimonyEvidenceReference Evidence(string bundleId, string view, object arguments) =>
        new(bundleId, view, JsonSerializer.SerializeToElement(arguments));

    private static void AssertRecipeSubject(HumanJudgmentSubject subject, ParsimonyFinding finding)
    {
        if (finding.AttachesTo.Kind == ParsimonyAttachmentKind.WordCase)
        {
            var exactFinding = Assert.IsType<ParsimonyFindingJudgmentSubject>(subject);
            Assert.Equal(finding.MeasureId, exactFinding.MeasureId);
            Assert.Equal(finding.FindingId, exactFinding.FindingId);
            return;
        }

        if (finding.AttachesTo.Kind == ParsimonyAttachmentKind.Group)
        {
            var group = Assert.IsType<GroupJudgmentSubject>(subject);
            var expectedRole = finding.AttachesTo.GroupKind switch
            {
                ParsimonyGroupKind.AlternationFamily => JudgmentGroupRole.AlternationFamily,
                ParsimonyGroupKind.AdhocSlotOrder => JudgmentGroupRole.AdhocSlotOrder,
                _ => throw new ArgumentOutOfRangeException(nameof(finding)),
            };
            Assert.Equal(expectedRole, group.Role);
            var view = finding.AttachesTo.GroupKind == ParsimonyGroupKind.AlternationFamily
                ? "allomorph-context" : "adhoc-context";
            var expectedMembers = finding.EvidenceRefs.Where(reference => reference.View == view)
                .Select(reference => PortableId(reference.Arguments.GetProperty("objectGuid").GetString()!))
                .Order(StringComparer.Ordinal);
            Assert.Equal(expectedMembers, group.Members.Select(member => member.Id).Order(StringComparer.Ordinal));
            return;
        }

        var item = Assert.IsType<ObjectJudgmentSubject>(subject).Object;
        Assert.Equal(finding.AttachesTo.Identity, item.Id);
        Assert.Equal(finding.AttachesTo.AuthoredObjectKind switch
        {
            ParsimonyAuthoredObjectKind.AdhocProhibition => "MoMorphAdhocProhib",
            ParsimonyAuthoredObjectKind.InflectionalMsa => "MoInflAffMsa",
            ParsimonyAuthoredObjectKind.AffixTemplate => "MoInflAffixTemplate",
            ParsimonyAuthoredObjectKind.AffixSlot => "MoInflAffixSlot",
            ParsimonyAuthoredObjectKind.Allomorph => "MoStemAllomorph",
            ParsimonyAuthoredObjectKind.NaturalClass => "PhNCSegments",
            _ => throw new ArgumentOutOfRangeException(nameof(finding)),
        }, item.Class);
    }

    private static string PortableId(string identity) => Guid.TryParse(identity, out var guid)
        ? CanonicalId.FromGuid(guid).Value
        : CanonicalId.Parse(identity).Value;

    private static IMoMorphAdhocProhib CreateMorphemeProhibition(
        LcmCache cache, IMoMorphSynAnalysis first, IMoMorphSynAnalysis other)
    {
        var prohibition = cache.ServiceLocator.GetInstance<IMoMorphAdhocProhibFactory>().Create();
        cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(prohibition);
        prohibition.FirstMorphemeRA = first;
        prohibition.Adjacency = 1;
        prohibition.RestOfMorphsRS.Add(other);
        return prohibition;
    }

    private static string[] CreateAlternationFamily(LcmCache cache, IPartOfSpeech category)
    {
        var stemType = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
            .GetObject(MoMorphTypeTags.kguidMorphStem);
        var first = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(stemType,
            TsStringUtils.MakeString("tan", cache.DefaultVernWs), "first alternation",
            SandboxGenericMSA.Create(MsaType.kStem, category));
        var second = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(stemType,
            TsStringUtils.MakeString("kan", cache.DefaultVernWs), "second alternation",
            SandboxGenericMSA.Create(MsaType.kStem, category));
        AddStemAlternate(cache, first, stemType, "tam");
        AddStemAlternate(cache, second, stemType, "kam");
        return [first.LexemeFormOA!.Guid.ToString("D"), first.AlternateFormsOS.Single().Guid.ToString("D"),
            second.LexemeFormOA!.Guid.ToString("D"), second.AlternateFormsOS.Single().Guid.ToString("D")];
    }

    private static void AddStemAlternate(LcmCache cache, ILexEntry entry, IMoMorphType stemType, string form)
    {
        var alternate = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
        entry.AlternateFormsOS.Add(alternate);
        alternate.MorphTypeRA = stemType;
        alternate.Form.set_String(cache.DefaultVernWs, form);
    }

    private sealed record RecipeReport(string ReportId, string BundleId, ParsimonyFinding Finding);

    private sealed record RecipeObjectIds(
        string InflectionalMsaId,
        string SlotId,
        string TemplateId,
        string NaturalClassId,
        string FirstAllomorphId,
        string SecondAllomorphId,
        string CaseWordformId,
        string[] AdhocProhibitionIds,
        string[] AlternationMemberIds);

    private sealed record RecipeRoute(
        string MeasureId,
        string Question,
        string KeepReason,
        string DeferReason,
        bool KeepAlreadyCovered)
    {
        public string ReasonFor(string disposition) => disposition switch
        {
            "keep" => KeepReason,
            "ask" => "The question is pending; no grammar change is proposed.",
            "defer" => DeferReason,
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };
    }

    private static readonly RecipeRoute[] RecipeRouteDefinitions =
    [
        new("P-adhoc-duplicate",
            "Which wording should remain for these two rules, and do their descriptions carry different linguistic explanations?",
            "Both prohibitions retain distinct linguistic explanations that are not yet captured in grouped facts.",
            "Defer until the full ordered targets and grouped rationale for both prohibitions are available.", false),
        new("R-word-negative-accepted",
            "The reviewed form tkap parses with the reversed A/B order. Is it impossible in this category, or valid in another use?",
            "The reversed order is valid in a distinct use; revise the negative judgment separately and leave the grammar unchanged.",
            "Defer until the reviewed-negative revision, matching positive order, and complete parser case can be compared.", false),
        new("R-word-disapproved-produced",
            "The Disapproved morphology on pka has a competing Approved reading. Does the rejected reading have a distinct grammatical condition?",
            "The Disapproved label concerns sense rather than this morphology, so both readings remain available.",
            "Defer until the exact Disapproved signature and its wordform attribution are complete.", false),
        new("B-affix-unslotted",
            "A precedes B in the cited examples. Is that order required, and does A share a position with another affix?",
            "The affix intentionally remains outside this template while its position is only partially ordered.",
            "Defer until the root, category, and additional Approved sequence evidence support a position choice.", false),
        new("R-tmpl-precedence",
            "The cited Approved sequence has B before A while this template requires A before B. Should the template reverse, or do these forms use a separate template?",
            "Both slot orders are Approved; keep the current order while the alternative template scope is unresolved.",
            "Defer until every applicable template embedding and the exact Approved sequence attribution are complete.", false),
        new("R-slot-blocking",
            "The cited Approved forms leave a required slot empty. May it be omitted by every listed user, or only in this paradigm?",
            "The required slot carries a meaningful marker, so its obligation remains in place.",
            "Defer until the one-slot parser comparison and all shared slot users are available.", false),
        new("R-allo-unconditioned",
            "Should the cited unconditioned variant be the final fallback after its conditioned siblings, or be limited to specific contexts?",
            "The unconditioned variant is the confirmed final fallback outside the conditioned contexts.",
            "Defer until effective allomorph order and the competing sibling conditions are available.", true),
        new("R-env-broad",
            "The cited environment licenses an extra context absent from these Texts. Is the contrasting form possible: valid, invalid, or unknown?",
            "The held-out form confirms that the broader environment remains productive.",
            "Defer until the context universe, compiled order, and unique alignment are available.", true),
        new("R-nc-excess",
            "Should this process also apply to the cited extra segments, or only to the observed subset? Please provide independent contrasts.",
            "The additional class members remain productive in held-out forms.",
            "Defer until class membership and the exact usage site can be attributed completely.", true),
        new("P-allo-alternation-family",
            "The two cited morphemes have n/m alternants. Does this pattern extend to held-out bilabial contexts, and where should it be ordered relative to rule R?",
            "The listed alternants are lexical patterns; a shared sound rule is not confirmed.",
            "Defer until the context and rule order are known beyond the exact comparison and every replacement identity can be mapped.", false),
        new("B-adhoc-is-slot-order",
            "These rules forbid reversed A/B order. Does A always precede B when another affix intervenes, or is only immediate adjacency forbidden?",
            "These rules express immediate adjacency, which a broader precedence rule would overstate.",
            "Defer until grouped ad hoc provenance, including rule members and rationale, is complete.", false),
        new("B-affix-null-vs-optional",
            "Does the cited zero affix contribute a feature or distinct reading, or only permit an already optional slot to be empty?",
            "The zero affix contributes a distinct reading and remains in the grammar.",
            "Defer until the compiler confirms that the authored zero marker is loaded as a final morphology output.", false),
    ];

    private string SaveReport(SIL.Motif.Contract.Baselines.BaselineToken baseline, int findingCopies = 1)
    {
        var measure = MeasureCatalog.Find("P-allo-duplicate-form")!;
        var bundleId = "bundle/disposition-test";
        var groupKey = "entry/" + _entryGuid;
        var inputs = new ParsimonyReportInputs(bundleId, baseline, "baseline", null, "sha256:" + new string('b', 64),
            new ParsimonyArtifactDigest(6, new string('c', 64)),
            new ParsimonyArtifactDigest(1, new string('d', 64)), null, null, []);
        var finding = new ParsimonyFinding("P-allo-duplicate-form:exact-finding", measure.Id,
            measure.Axis, measure.Tier,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group,
                groupKey + "/duplicate-forms/exact", GroupKind: ParsimonyGroupKind.AllomorphDuplicateForms),
            groupKey, new ParsimonyMeasureNumber(1, 2, measure.Unit), measure.Threshold!,
            new string('a', 64),
            _memberGuids.Select(guid => new ParsimonyEvidenceReference(bundleId, "allomorph-context",
                JsonSerializer.SerializeToElement(new { objectGuid = guid }))).ToArray(),
            measure.RecipeLink, [], ParsimonyVerification.NotRun);
        var reportId = CanonicalId.Mint("report/").Value;
        var response = new ParsimonyReportResponse(reportId, inputs,
            Enumerable.Repeat(finding, findingCopies).ToArray(), "Two duplicate forms.")
        {
            Notes = [],
            JoinQuality = new ParsimonyJoinQuality("project-approved", true, 0, 0, 0, 0, 0, 0, 0,
                null, null, null, null, null),
        };
        var saved = ProjectStoreCommand.Run(_projectPath, ProductVersion, (database, _) =>
        {
            new ReportRepository(database).Save(new ReportRecord(reportId, null, null,
                JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                null, "parsimony", response.Text));
            return CommandOutcome<ParsimonyReportResponse>.Success(response);
        });
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        return reportId;
    }

    private static ParsimonyDispositionKind ParseDisposition(string value) => value switch
    {
        "fix" => ParsimonyDispositionKind.Fix,
        "keep" => ParsimonyDispositionKind.Keep,
        "ask" => ParsimonyDispositionKind.Ask,
        "defer" => ParsimonyDispositionKind.Defer,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
