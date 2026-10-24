using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Input;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class ParsimonyPageIntegrationTests(PristineProjectFixture pristine)
{
    private const string MeasureId = "P-adhoc-duplicate";

    [Fact]
    public async Task AdvancedAiModeLoadsARealParsimonyReportThroughTheCommandClient()
    {
        using var scenario = await CreateScenarioAsync(includeEditedJudgment: false);
        var client = RealCommandClient.Create(scenario.ManagedRoot, scenario.ParserPath);
        var (context, page) = CreatePage(client);

        await context.OpenProjectAsync(scenario.ProjectPath);
        context.OpenPage(WorkspacePage.Parsimony);

        Assert.True(context.CanOpenPage(WorkspacePage.Parsimony));
        Assert.Equal(ParsimonyPageState.Listed, page.State);
        Assert.Equal("1 finding to review", page.AxisSummary);
        var row = Assert.Single(page.Rows, item => !item.IsHeader);
        Assert.True(page.SelectFinding(row.Link!));
        Assert.Contains(page.Evidence!.Details,
            detail => detail.Label == "Counted" && detail.Value == "1 of 2 prohibitions");
    }

    [Fact]
    public async Task EditedJudgmentDataAppearsUnavailableAndHashFailedOnThePage()
    {
        using var scenario = await CreateScenarioAsync(includeEditedJudgment: true);
        var client = RealCommandClient.Create(scenario.ManagedRoot, scenario.ParserPath);
        var (context, page) = CreatePage(client);

        await context.OpenProjectAsync(scenario.ProjectPath);

        Assert.Equal(ParsimonyPageState.Listed, page.State);
        var history = await client.ReadParsimonyViewAsync(new ReadParsimonyViewRequest(
            scenario.ProjectPath, MotifProductVersion.CurrentText,
            new ParsimonyNamedViewRequest(scenario.Report.Inputs.BundleId,
                ParsimonyViews.ParsimonySuppressionHistory,
                new ParsimonyViewFilters(ReportId: scenario.Report.ReportId))), CancellationToken.None);
        Assert.True(history.Succeeded, history.Refusal?.Message);
        var unresolved = Assert.Single(history.Value!.Rows.OfType<ParsimonyUnresolvedJudgmentViewRow>());
        Assert.Equal(scenario.EditedVisibleId, unresolved.VisibleId);
        Assert.Equal("unavailable", unresolved.State);
        Assert.Equal("hash-failed", unresolved.Check);
        Assert.True(page.HasJudgmentIssues);
        Assert.Contains(scenario.EditedVisibleId!, page.JudgmentIssuesText, StringComparison.Ordinal);
        Assert.Contains(unresolved.Reason, page.JudgmentIssuesText, StringComparison.Ordinal);
        Assert.Contains("it does not suppress its finding", page.JudgmentIssuesText, StringComparison.Ordinal);
        Assert.Single(page.Groups.SelectMany(group => group.Items));
    }

    [Fact]
    public async Task KeepStagesAProposalThatANewClientReadsWithoutWritingTheProject()
    {
        using var scenario = await CreateScenarioAsync(includeEditedJudgment: false);
        var client = RealCommandClient.Create(scenario.ManagedRoot, scenario.ParserPath);
        var (context, page) = CreatePage(client);
        await context.OpenProjectAsync(scenario.ProjectPath);
        var row = Assert.Single(page.Rows, item => !item.IsHeader);
        Assert.True(page.SelectFinding(row.Link!));
        page.SelectedRecordType = Assert.Single(page.RecordTypes, item => item.Name == "Parsimony review");
        Assert.True(page.KeepCommand.CanExecute(null));
        page.ReasonText = "The duplicate prohibition is retained for this check.";
        var projectBytes = await File.ReadAllBytesAsync(scenario.ProjectPath);
        var projectWriteTime = File.GetLastWriteTimeUtc(scenario.ProjectPath);

        await page.KeepCommand.ExecuteAsync(null);

        Assert.Equal("Staged in pending changes. Nothing is written to FieldWorks until Apply.", page.ActionMessage);
        Assert.Equal("Kept · staged, not yet applied",
            Assert.Single(page.Rows, item => !item.IsHeader).Disposition);
        Assert.Equal(projectWriteTime, File.GetLastWriteTimeUtc(scenario.ProjectPath));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(projectBytes)),
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(scenario.ProjectPath))));

        var reopened = RealCommandClient.Create(scenario.ManagedRoot, scenario.ParserPath);
        var standings = await reopened.ProposalStandingsAsync(
            new ProposalStandingsRequest(scenario.ProjectPath), CancellationToken.None);
        Assert.True(standings.Succeeded, standings.Refusal?.Message);
        var pending = Assert.Single(standings.Value!.Proposals, item => item.IsPendingDraft);
        Assert.Equal(ProposalGroup.NotAppliedYet, pending.Group);
        var review = await reopened.ReadProposalReviewAsync(
            new ProposalReviewRequest(scenario.ProjectPath, pending.ProposalId), CancellationToken.None);
        Assert.True(review.Succeeded, review.Refusal?.Message);
        Assert.True(review.Value!.IsDraft);
        Assert.True(review.Value.IsPendingChanges);
        Assert.Equal(PendingChanges.DraftName, review.Value.DraftName);
        var staged = Assert.IsType<GrammarChangeEntry>(Assert.Single(review.Value.Entries));
        Assert.Equal("RecordParsimonyDisposition", staged.Composer);
    }

    [Fact]
    public async Task AMissingReportStartsAllMeasuresWithoutLeavingOverviewOrStartingAnAssessment()
    {
        using var scenario = await CreateScenarioAsync(includeEditedJudgment: false, createReport: false);
        await using var runner = new HeldRunner(new JobRunnerLaunchOptions(scenario.ManagedRoot, scenario.ParserPath));
        var client = RealCommandClient.Create(scenario.ManagedRoot, scenario.ParserPath, runner);
        var (context, page) = CreatePage(client);
        try
        {
            Assert.Empty(ParsimonyJobs(scenario.ProjectPath));
            AssertNoAssessments(scenario.ProjectPath);

            // The real runner cannot process the job until Release; opening must not await its completion.
            await context.OpenProjectAsync(scenario.ProjectPath);
            Assert.Equal(WorkspacePage.Overview, context.CurrentPage);
            Assert.True(context.Baseline?.HasBaseline);
            Assert.NotEqual(ParsimonyPageState.NoReport, page.State);
            Assert.NotEqual(ParsimonyPageState.Refused, page.State);
            await runner.Started;
            var queued = Assert.Single(ParsimonyJobs(scenario.ProjectPath));
            Assert.Equal(JobStatus.Queued, queued.Status);
            var input = JsonSerializer.Deserialize<ParsimonyJobInput>(queued.InputJson,
                SIL.Motif.Contract.MotifJson.CreateOptions());
            Assert.NotNull(input);
            Assert.Equal(ParsimonyAdmission.AllMeasures, input.MeasureId);
            Assert.NotNull(input.ScopeBinding);
            Assert.Equal(ParsimonyEvidenceScopeKind.ProjectApproved, input.ScopeBinding.Kind);
            Assert.NotNull(input.AssessmentIds);
            Assert.Empty(input.AssessmentIds);
            AssertNoAssessments(scenario.ProjectPath);

            runner.Release();
            await runner.WhenIdleAsync();
            var completed = await ParsimonyCommands.WaitAllAsync(new WaitForParsimonyReportSetRequest(
                scenario.ProjectPath, MotifProductVersion.CurrentText, queued.JobId, TimeSpan.Zero),
                CancellationToken.None);
            Assert.True(completed.Succeeded, completed.Refusal?.Message);
            Assert.Equal(ParsimonyAdmission.SupportedMeasureIds(), completed.Value!.Reports
                .SelectMany(report => report.MeasureRuns.Select(run => run.MeasureId)).ToArray());
            var duplicate = Assert.Single(completed.Value.Reports,
                report => report.MeasureRuns.Any(run => run.MeasureId == MeasureId));
            Assert.Single(duplicate.Findings);
            Assert.All(completed.Value.Reports, report =>
            {
                Assert.Equal(context.Baseline!.Token, report.Inputs.BaselineToken);
                Assert.Equal(ParsimonyEvidenceScopeKind.ProjectApproved, report.Inputs.EvidenceScope);
                Assert.Empty(report.AssessmentIds);
                Assert.Empty(report.Inputs.AssessmentIds);
            });
            await ListedAsync(page);
            Assert.Equal(WorkspacePage.Overview, context.CurrentPage);
            Assert.Contains(page.Rows, row => row.Link?.FindingId == duplicate.Findings[0].FindingId);
            Assert.Single(ParsimonyJobs(scenario.ProjectPath));
            AssertNoAssessments(scenario.ProjectPath);
        }
        finally
        {
            runner.Release();
            await context.StopPageWorkAsync();
        }
    }

    [Fact]
    public async Task AMatchingStoredAllMeasureReportSetIsReusedWithoutEnqueueing()
    {
        using var scenario = await CreateScenarioAsync(includeEditedJudgment: false, allMeasures: true);
        var allJob = Assert.Single(ParsimonyJobs(scenario.ProjectPath));
        var otherMeasure = ParsimonyAdmission.SupportedMeasureIds().First(id => id != MeasureId);
        var unrelated = ParsimonyCommands.Enqueue(new EnqueueParsimonyReportRequest(scenario.ProjectPath,
            MotifProductVersion.CurrentText, otherMeasure, ParsimonyEvidenceScopeKind.ProjectApproved), scenario.ParserPath);
        Assert.True(unrelated.Succeeded, unrelated.Refusal?.Message);
        await using (var seedRunner = new InProcessRunnerLauncher(
                         new JobRunnerLaunchOptions(scenario.ManagedRoot, scenario.ParserPath)))
        {
            seedRunner.Start(scenario.ProjectPath);
            await seedRunner.WhenIdleAsync();
        }
        var lastSingleReport = ParsimonyCommands.Wait(new WaitForParsimonyReportRequest(scenario.ProjectPath,
            MotifProductVersion.CurrentText, unrelated.Value!.JobId, TimeSpan.Zero));
        Assert.True(lastSingleReport.Succeeded, lastSingleReport.Refusal?.Message);
        Assert.DoesNotContain(lastSingleReport.Value!.Findings, finding => finding.MeasureId == MeasureId);
        var before = ParsimonyJobs(scenario.ProjectPath).Select(job => job.JobId).ToArray();
        Assert.Contains(allJob.JobId, before);
        var invocations = FakeParser.Invocations(scenario.ParserPath).ToArray();
        await using var runner = new HeldRunner(new JobRunnerLaunchOptions(scenario.ManagedRoot, scenario.ParserPath));
        var client = RealCommandClient.Create(scenario.ManagedRoot, scenario.ParserPath, runner);
        var (context, page) = CreatePage(client);
        try
        {
            await context.OpenProjectAsync(scenario.ProjectPath);
            await ListedAsync(page);
            Assert.Equal(WorkspacePage.Overview, context.CurrentPage);
            Assert.Contains(page.Rows, row => row.Link?.FindingId == scenario.Report.Findings[0].FindingId);
            await page.ReloadAsync();
            await context.StopPageWorkAsync();
            Assert.Equal(before, ParsimonyJobs(scenario.ProjectPath).Select(job => job.JobId));
            Assert.False(runner.Started.IsCompleted);
            Assert.Equal(invocations, FakeParser.Invocations(scenario.ParserPath));
            AssertNoAssessments(scenario.ProjectPath);
        }
        finally
        {
            runner.Release();
            await context.StopPageWorkAsync();
        }
    }

    [Fact]
    public async Task AdvancedAiModeOffDoesNotStartHiddenParsimonyWork()
    {
        using var scenario = await CreateScenarioAsync(includeEditedJudgment: false, createReport: false);
        await using var runner = new HeldRunner(new JobRunnerLaunchOptions(scenario.ManagedRoot, scenario.ParserPath));
        var client = RealCommandClient.Create(scenario.ManagedRoot, scenario.ParserPath, runner);
        var (context, page) = CreatePage(client, advancedAi: false);
        try
        {
            await context.OpenProjectAsync(scenario.ProjectPath);
            await page.ReloadAsync();
            await context.StopPageWorkAsync();
            Assert.Equal(ParsimonyPageState.Off, page.State);
            Assert.Equal(WorkspacePage.Overview, context.CurrentPage);
            Assert.Empty(ParsimonyJobs(scenario.ProjectPath));
            Assert.False(runner.Started.IsCompleted);
            AssertNoAssessments(scenario.ProjectPath);
        }
        finally
        {
            runner.Release();
            await context.StopPageWorkAsync();
        }
    }

    [Fact]
    public async Task StoppingTheWorkspaceCancelsItsRealQueuedParsimonyJob()
    {
        using var scenario = await CreateScenarioAsync(includeEditedJudgment: false, createReport: false);
        await using var runner = new HeldRunner(new JobRunnerLaunchOptions(scenario.ManagedRoot, scenario.ParserPath));
        var client = RealCommandClient.Create(scenario.ManagedRoot, scenario.ParserPath, runner);
        var (context, page) = CreatePage(client);
        try
        {
            await context.OpenProjectAsync(scenario.ProjectPath);
            Assert.NotEqual(ParsimonyPageState.NoReport, page.State);
            await runner.Started;
            Assert.Equal(JobStatus.Queued, Assert.Single(ParsimonyJobs(scenario.ProjectPath)).Status);
            await context.StopPageWorkAsync();
            Assert.Equal(JobStatus.Cancelled, Assert.Single(ParsimonyJobs(scenario.ProjectPath)).Status);
            AssertNoAssessments(scenario.ProjectPath);
        }
        finally
        {
            runner.Release();
            await context.StopPageWorkAsync();
        }
    }

    [Fact]
    public async Task AStoredAllMeasureSetFromDifferentParserBytesIsNotReusable()
    {
        using var scenario = await CreateScenarioAsync(includeEditedJudgment: false, allMeasures: true);
        var parserDirectory = Path.Combine(scenario.ManagedRoot, "different-parser-" + Guid.NewGuid().ToString("N"));
        try
        {
            var differentParser = FakeParser.CopyReportingVersion(parserDirectory, "different-test-parser");
            var client = RealCommandClient.Create(scenario.ManagedRoot, differentParser);
            var read = await client.ReadLatestParsimonyReportSetAsync(new ReadLatestParsimonyReportRequest(
                scenario.ProjectPath, MotifProductVersion.CurrentText), CancellationToken.None);
            Assert.True(read.Succeeded, read.Refusal?.Message);
            Assert.Null(read.Value);
            Assert.Single(ParsimonyJobs(scenario.ProjectPath));
            AssertNoAssessments(scenario.ProjectPath);
        }
        finally { Directory.Delete(parserDirectory, recursive: true); }
    }

    [Fact]
    public async Task ARunnerStartFaultDoesNotLeaveAnUnownedQueuedJob()
    {
        using var scenario = await CreateScenarioAsync(includeEditedJudgment: false, createReport: false);
        var runner = new RefusingRunner(new JobRunnerLaunchOptions(scenario.ManagedRoot, scenario.ParserPath));
        var client = RealCommandClient.Create(scenario.ManagedRoot, scenario.ParserPath, runner);
        var queued = await client.EnqueueParsimonyReportAsync(new EnqueueParsimonyReportRequest(scenario.ProjectPath,
            MotifProductVersion.CurrentText, ParsimonyAdmission.AllMeasures, ParsimonyEvidenceScopeKind.ProjectApproved),
            CancellationToken.None);
        Assert.False(queued.Succeeded);
        Assert.Equal("parsimony.runner-start-failed", queued.Refusal!.Code);
        Assert.Equal(JobStatus.Cancelled, Assert.Single(ParsimonyJobs(scenario.ProjectPath)).Status);
        AssertNoAssessments(scenario.ProjectPath);
    }

    [Fact]
    public async Task AnAllSetWithMissingMeasureMetadataIsNotReusable()
    {
        using var scenario = await CreateScenarioAsync(includeEditedJudgment: false, allMeasures: true);
        using (var database = ProjectMotifDatabase.Open(scenario.ProjectPath))
        {
            var reports = new ReportRepository(database);
            var saved = reports.Get(scenario.Report.ReportId)!;
            var document = JsonNode.Parse(saved.ReportJson)!;
            document["measureRuns"] = null;
            reports.Save(saved with { ReportJson = document.ToJsonString() });
        }
        var client = RealCommandClient.Create(scenario.ManagedRoot, scenario.ParserPath);
        var read = await client.ReadLatestParsimonyReportSetAsync(new ReadLatestParsimonyReportRequest(
            scenario.ProjectPath, MotifProductVersion.CurrentText), CancellationToken.None);
        Assert.True(read.Succeeded, read.Refusal?.Message);
        Assert.Null(read.Value);
        Assert.Single(ParsimonyJobs(scenario.ProjectPath));
        AssertNoAssessments(scenario.ProjectPath);
    }

    private async Task<ParsimonyScenario> CreateScenarioAsync(
        bool includeEditedJudgment, bool createReport = true, bool allMeasures = true)
    {
        var managedRoot = RunnerOptions.ResolveRoot();
        Guid firstProhibition = Guid.Empty;
        Guid duplicateProhibition = Guid.Empty;
        Guid firstMsa = Guid.Empty;
        Guid secondMsa = Guid.Empty;
        string? editedVisibleId = null;
        string projectPath;

        using (var cache = pristine.NewScratch())
        {
            projectPath = cache.ProjectId.Path;
            var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
            var firstEntry = (ILexEntry)repository.GetObject(pristine.Seed.FirstEntryId);
            var secondEntry = (ILexEntry)repository.GetObject(pristine.Seed.SecondEntryId);
            var firstAnalysis = firstEntry.MorphoSyntaxAnalysesOC.Single();
            var secondAnalysis = secondEntry.MorphoSyntaxAnalysesOC.Single();
            firstMsa = firstAnalysis.Guid;
            secondMsa = secondAnalysis.Guid;

            IMoMorphAdhocProhib first = null!;
            IMoMorphAdhocProhib duplicate = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                first = cache.ServiceLocator.GetInstance<IMoMorphAdhocProhibFactory>().Create();
                duplicate = cache.ServiceLocator.GetInstance<IMoMorphAdhocProhibFactory>().Create();
                cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(first);
                cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(duplicate);
                foreach (var prohibition in new[] { first, duplicate })
                {
                    prohibition.FirstMorphemeRA = firstAnalysis;
                    prohibition.Adjacency = 1;
                    prohibition.RestOfMorphsRS.Add(secondAnalysis);
                }
            });
            firstProhibition = first.Guid;
            duplicateProhibition = duplicate.Guid;

            var judgmentField = NotebookJudgmentFixture.InitializeReservedField(cache);
            AddNotebookRecordType(cache);
            if (includeEditedJudgment)
                editedVisibleId = AddEditedJudgment(cache, judgmentField, firstProhibition);

            new FwDataProjectLoader().Save(cache);
        }

        var parserDirectory = Path.Combine(managedRoot, "parsimony-page-" + Guid.NewGuid().ToString("N"));
        var parserPath = FakeParser.CopyRecordingInvocations(parserDirectory);
        FakeParser.WriteFactsFixture(parserPath, new
        {
            prohibitions = new[]
            {
                Prohibition(firstProhibition, firstMsa, secondMsa),
                Prohibition(duplicateProhibition, firstMsa, secondMsa),
            },
        });

        try
        {
            var captured = BaselineCaptureCommand.Capture(
                new BaselineCaptureRequest(projectPath), managedRoot);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            var draft = ProposalCommands.New(new NewDraftRequest(projectPath,
                MotifProductVersion.CurrentText, PendingChanges.DraftName, "Parsimony review"));
            Assert.True(draft.Succeeded, draft.Refusal?.Message);
            if (!createReport)
                return new ParsimonyScenario(projectPath, managedRoot, parserPath, null, editedVisibleId);
            var queued = ParsimonyCommands.Enqueue(new EnqueueParsimonyReportRequest(
                projectPath, MotifProductVersion.CurrentText, allMeasures ? ParsimonyAdmission.AllMeasures : MeasureId,
                ParsimonyEvidenceScopeKind.ProjectApproved), parserPath);
            Assert.True(queued.Succeeded, queued.Refusal?.Message);

            ParsimonyReportResponse report;
            await using (var runner = new InProcessRunnerLauncher(
                new JobRunnerLaunchOptions(managedRoot, parserPath)))
            {
                runner.Start(projectPath);
                await runner.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(2));
                if (allMeasures)
                {
                    var completed = await ParsimonyCommands.WaitAllAsync(new WaitForParsimonyReportSetRequest(
                        projectPath, MotifProductVersion.CurrentText, queued.Value!.JobId, TimeSpan.FromMinutes(1)),
                        CancellationToken.None);
                    Assert.True(completed.Succeeded, completed.Refusal?.Message);
                    report = Assert.Single(completed.Value!.Reports,
                        item => item.MeasureRuns.Any(run => run.MeasureId == MeasureId));
                }
                else
                {
                    var completed = ParsimonyCommands.Wait(new WaitForParsimonyReportRequest(
                        projectPath, MotifProductVersion.CurrentText, queued.Value!.JobId, TimeSpan.FromMinutes(1)));
                    Assert.True(completed.Succeeded, completed.Refusal?.Message);
                    report = completed.Value!;
                }
            }

            Assert.Single(report.Findings);
            return new ParsimonyScenario(projectPath, managedRoot, parserPath, report, editedVisibleId);
        }
        catch
        {
            try { Directory.Delete(parserDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static object Prohibition(Guid prohibition, Guid primary, Guid other) => new
    {
        prohibitionGuid = prohibition.ToString("D"),
        kind = "morpheme",
        disabled = false,
        adjacency = "somewhereToLeft",
        primaryGuid = primary.ToString("D"),
        targetKind = "msa",
        others = new[] { new { targetGuid = other.ToString("D"), targetKind = "msa" } },
    };

    private static string AddEditedJudgment(LcmCache cache, int fieldId, Guid subjectId)
    {
        var recordId = Guid.NewGuid();
        var recordPortableId = CanonicalId.FromGuid(recordId).Value;
        var measure = MeasureCatalog.Find(MeasureId)!;
        var judgment = new HumanJudgment(CanonicalId.FromGuid(cache.LangProject.Guid).Value,
            CanonicalId.Mint().Value, recordPortableId, [], new DispositionJudgment(
                new ObjectJudgmentSubject(new JudgmentObject("MoMorphAdhocProhib",
                    CanonicalId.FromGuid(subjectId).Value)), MeasureId, ParsimonyDispositionKind.Keep,
                "sha256:" + new string('8', 64), measure.QueryId!, "the prohibition", "Duplicate prohibitions"));
        var formatted = HumanJudgmentCodec.Format(judgment);
        var record = NotebookJudgmentFixture.AddRecord(cache, recordId, fieldId, formatted);
        var editedData = formatted.Data.Replace("\"keep\"", "\"defer\"", StringComparison.Ordinal);
        Assert.NotEqual(formatted.Data, editedData);
        NotebookJudgmentFixture.SetValue(cache, record,
            TsStringUtils.MakeString(editedData, cache.DefaultAnalWs));
        return formatted.VisibleId;
    }

    private static void AddNotebookRecordType(LcmCache cache)
    {
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var notebook = cache.LangProject.ResearchNotebookOA;
            var recordTypes = notebook.RecTypesOA ?? cache.ServiceLocator
                .GetInstance<ICmPossibilityListFactory>().Create();
            notebook.RecTypesOA = recordTypes;
            var recordType = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
            recordTypes.PossibilitiesOS.Add(recordType);
            recordType.Name.set_String(cache.DefaultAnalWs,
                TsStringUtils.MakeString("Parsimony review", cache.DefaultAnalWs));
        });
    }

    private static (WorkspaceContext Context, ParsimonyPageModel Page) CreatePage(CommandClient client, bool advancedAi = true)
    {
        var selection = new SelectionViewModel(client);
        var baseline = new BaselineViewModel(client);
        var context = new WorkspaceContext(selection, new AssessViewModel(client, selection),
            new ChangesViewModel(client), client, new FolderPicker(), new DragSource(), baseline)
        {
            AdvancedAiModeEnabled = advancedAi,
        };
        // The shell publishes this snapshot from the real Baseline model; this isolated page owns the same wiring.
        baseline.PropertyChanged += (_, _) => context.Baseline = context.ProjectPath is null ? null :
            new WorkspaceBaseline(baseline.HasBaseline, baseline.CapturedTimeText, baseline.SavedText,
                baseline.CapturedAtText, baseline.HeldStatusText, baseline.ShownRefusal?.Sentence)
            {
                Token = baseline.Token,
                SourceLastWriteUtc = baseline.SourceLastWriteUtc,
                ProjectLastWriteUtc = baseline.ProjectLastWriteUtc,
                FieldWorksHeldProject = baseline.FieldWorksHeldProject,
            };
        return (context, new ParsimonyPageModel(context));
    }

    private sealed class ParsimonyScenario(
        string projectPath, string managedRoot, string parserPath, ParsimonyReportResponse? report,
        string? editedVisibleId) : IDisposable
    {
        public string ProjectPath { get; } = projectPath;

        public string ManagedRoot { get; } = managedRoot;

        public string ParserPath { get; } = parserPath;

        public ParsimonyReportResponse Report => report ?? throw new InvalidOperationException("The scenario has no report yet.");

        public string? EditedVisibleId { get; } = editedVisibleId;

        public void Dispose()
        {
            try { Directory.Delete(Path.GetDirectoryName(ParserPath)!, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static IReadOnlyList<JobRecord> ParsimonyJobs(string projectPath)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        var key = ProjectWorkspaceKey.Compute(new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath)));
        return new JobRepository(database).ListByProjectAndKind(key, ParsimonyJobHandler.JobKind);
    }

    private static void AssertNoAssessments(string projectPath)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        var assessments = new AssessmentRepository(database);
        Assert.Null(assessments.GetCurrent());
        Assert.Empty(assessments.ListByKind(AssessmentKinds.ParseTime));
        Assert.Empty(assessments.ListByKind(AssessmentKinds.ObjectTiming));
        Assert.Empty(assessments.ListByKind(AssessmentKinds.Correctness));
    }

    private static async Task ListedAsync(ParsimonyPageModel page)
    {
        if (page.State == ParsimonyPageState.Listed) return;
        var listed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(page.State)) return;
            if (page.State == ParsimonyPageState.Listed) listed.TrySetResult();
            else if (page.State is ParsimonyPageState.Refused or ParsimonyPageState.NoReport)
                listed.TrySetException(new InvalidOperationException(page.State + ": " + page.Message));
        }
        page.PropertyChanged += Changed;
        try
        {
            Changed(null, new System.ComponentModel.PropertyChangedEventArgs(nameof(page.State)));
            await listed.Task;
        }
        finally { page.PropertyChanged -= Changed; }
    }

    // Holding startup proves opening returns before job completion, without sleeps or a fake command client.
    private sealed class HeldRunner(JobRunnerLaunchOptions options) : IJobRunnerLauncher, IAsyncDisposable
    {
        private readonly InProcessRunnerLauncher _inner = new(options);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _sync = new();
        private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
        private bool _released;

        public JobRunnerLaunchOptions Options => options;
        public Task Started => _started.Task;

        public void Start(string projectPath, Action<string>? reportWarning = null)
        {
            lock (_sync)
            {
                if (_released) _inner.Start(projectPath, reportWarning);
                else _pending.Add(projectPath);
                _started.TrySetResult();
            }
        }

        public void Release()
        {
            lock (_sync)
            {
                if (_released) return;
                _released = true;
                foreach (var path in _pending) _inner.Start(path);
                _pending.Clear();
            }
        }

        public Task WhenIdleAsync() => _inner.WhenIdleAsync();

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class RefusingRunner(JobRunnerLaunchOptions options) : IJobRunnerLauncher
    {
        public JobRunnerLaunchOptions Options => options;
        public void Start(string projectPath, Action<string>? reportWarning = null) => throw new IOException("Cannot start runner.");
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
