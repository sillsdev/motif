using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

/// <summary>
/// A parser-only keep, staged with the identity the Passage sends (Approve, the parser reading and its index, the
/// word's Assessment), must create exactly that one analysis when applied to a disposable copy of the project.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class ParserOnlyKeepApplyRealClientTests(PristineProjectFixture pristine)
{
    [UiThreadFact]
    public async Task ApplyingAParserOnlyKeepCreatesExactlyThatAnalysisAndApprovesIt()
    {
        using var project = new WalkthroughProject(pristine);
        const string word = "parseronlykeep";
        var wordformId = Guid.Empty;
        Guid formId = Guid.Empty, msaId = Guid.Empty;
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs)).Guid;
                var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
                formId = entry.LexemeFormOA.Guid;
                msaId = entry.MorphoSyntaxAnalysesOC.First().Guid;
            }));

        var parser = FakeParser.Copy(project.ManagedRoot);
        await using var runner = new InProcessRunnerLauncher(new JobRunnerLaunchOptions(project.ManagedRoot, parser));
        var client = RealCommandClient.Create(project.ManagedRoot, parser, runner);
        var captured = await client.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var reading = new ParseAnalysis([new ParseMorph(formId.ToString("D"), msaId.ToString("D"), null, word)]);
        const string assessmentId = "parser-only-keep-assessment";
        RecordParseAssessment(project.FwDataPath, assessmentId, word, reading);

        var initial = await client.LoadPendingChangesAsync(new PendingChangesRequest(project.FwDataPath, "1.0"),
            CancellationToken.None);
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        var staged = await client.PutPendingChangeAsync(new PutPendingChangeRequest(project.FwDataPath, "1.0",
            initial.Value!.Revision, new ChangeIntent(CanonicalId.Mint().Value, ChangeKinds.Approve,
                CanonicalId.FromGuid(wordformId).Value, word, assessmentId, reading, DisplayReading: word, ReadingIndex: 0,
                OriginPage: WorkspacePage.Texts.ToString())), CancellationToken.None);
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        var measured = await client.MeasurePendingAsync(new MeasurePendingRequest(project.FwDataPath,
            staged.Value!.DraftId!, staged.Value.Revision, [word]), new Progress<MeasureProgress>(), CancellationToken.None);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);

        var current = await client.LoadPendingChangesAsync(new PendingChangesRequest(project.FwDataPath, "1.0"),
            CancellationToken.None);
        Assert.True(current.Succeeded, current.Refusal?.Message);
        var applied = await client.ApplyPendingAsync(new ApplyPendingRequest(project.FwDataPath,
            current.Value!.DraftId!, current.Value.Revision), CancellationToken.None);

        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        using var cache = new FwDataProjectLoader().LoadCache(project.FwDataPath);
        var created = Assert.Single(cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformId).AnalysesOC);
        var bundle = Assert.Single(created.MorphBundlesOS);
        Assert.Equal(formId, bundle.MorphRA.Guid);
        Assert.Equal(msaId, bundle.MsaRA.Guid);
        Assert.Equal(Opinions.approves, created.GetAgentOpinion(cache.LangProject.DefaultUserAgent));
        await runner.WhenIdleAsync();
    }

    private static void RecordParseAssessment(string path, string assessmentId, string word, ParseAnalysis reading)
    {
        var fullPath = Path.GetFullPath(path);
        var locator = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(locator), locator,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(locator))!;
        var assessed = new AssessedWord(word, "analysed", [new ParsedAnalysis(null, [], 0, "sha256:keep")])
        {
            Morphology = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, word, 0, false, false, false, [reading], []),
        };
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            AssessmentId: assessmentId, ProposalId: null, ProposalIntentDigest: null, Assessor: "test",
            Kind: AssessmentKind.ParseTime.ToStoredKind(), ScopeJson: "{}", ScopeDigest: "sha256:keep-scope",
            Subject: new AssessmentSubject.Words(Selection.Create("Keep", [word]), "whitespace-and-punctuation", "1"),
            BaselineToken: JsonSerializer.Serialize(baseline.Token), OutcomeDigest: "sha256:keep-outcome",
            SemanticDigest: "sha256:keep-semantic", GrammarSourceSha256: "sha256:keep-grammar",
            ModelFingerprint: "keep-test", Pipeline: "keep-test", DiagnosticCount: 0,
            Material: new AssessmentRecordMaterial.Words([assessed], null, null, []), SavedUtc: "2026-11-15T12:00:00Z"));
    }
}
