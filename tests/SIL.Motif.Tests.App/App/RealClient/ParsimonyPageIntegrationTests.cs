using System.Security.Cryptography;
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
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
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

    private async Task<ParsimonyScenario> CreateScenarioAsync(bool includeEditedJudgment)
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
            var queued = ParsimonyCommands.Enqueue(new EnqueueParsimonyReportRequest(
                projectPath, MotifProductVersion.CurrentText, MeasureId,
                ParsimonyEvidenceScopeKind.ProjectApproved));
            Assert.True(queued.Succeeded, queued.Refusal?.Message);

            ParsimonyReportResponse report;
            await using (var runner = new InProcessRunnerLauncher(
                new JobRunnerLaunchOptions(managedRoot, parserPath)))
            {
                runner.Start(projectPath);
                await runner.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(2));
                var completed = ParsimonyCommands.Wait(new WaitForParsimonyReportRequest(
                    projectPath, MotifProductVersion.CurrentText, queued.Value!.JobId, TimeSpan.FromMinutes(1)));
                Assert.True(completed.Succeeded, completed.Refusal?.Message);
                report = completed.Value!;
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

    private static (WorkspaceContext Context, ParsimonyPageModel Page) CreatePage(CommandClient client)
    {
        var selection = new SelectionViewModel(client);
        var context = new WorkspaceContext(selection, new AssessViewModel(client, selection),
            new ChangesViewModel(client), client, new FolderPicker(), new DragSource(), new BaselineViewModel(client))
        {
            AdvancedAiModeEnabled = true,
        };
        return (context, new ParsimonyPageModel(context));
    }

    private sealed class ParsimonyScenario(
        string projectPath, string managedRoot, string parserPath, ParsimonyReportResponse report,
        string? editedVisibleId) : IDisposable
    {
        public string ProjectPath { get; } = projectPath;

        public string ManagedRoot { get; } = managedRoot;

        public string ParserPath { get; } = parserPath;

        public ParsimonyReportResponse Report { get; } = report;

        public string? EditedVisibleId { get; } = editedVisibleId;

        public void Dispose()
        {
            try { Directory.Delete(Path.GetDirectoryName(ParserPath)!, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
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
