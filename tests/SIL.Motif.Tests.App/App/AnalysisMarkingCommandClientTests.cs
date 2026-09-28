using System.Text.Json;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
public sealed class AnalysisMarkingCommandClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task RealCommandClientStagesStoredAndParserOnlyMarkingChoicesAtTheirOccurrences()
    {
        await AssertStoredChoiceAsync("unknown", ChangeKinds.Approve);
        await AssertStoredChoiceAsync("approved", ChangeKinds.Reject);
        await AssertStoredChoiceAsync("disapproved", ChangeKinds.Approve);
        await AssertStoredChoiceAsync("disapproved", ChangeKinds.Candidate);
        await AssertParserChoiceAsync(SeededProject.UnanalysedWordForm, ChangeKinds.AddCandidate,
            [Guessed("new unknown")], null);
        await AssertParserChoiceAsync(SeededProject.UnanalysedWordForm, ChangeKinds.Approve,
            [Guessed("new approved")], 0);
        await AssertParserChoiceAsync(SeededProject.AnalysedWordForm, ChangeKinds.Reject,
            [Guessed("extra reading")], 1, keepStoredReading: true);
    }

    [Theory]
    [InlineData("same", "analysed", false, AnalysisMarkingClass.Same)]
    [InlineData("different", "analysed", false, AnalysisMarkingClass.Different)]
    [InlineData("extra", "analysed", false, AnalysisMarkingClass.Extra)]
    [InlineData("none", "no-analysis", false, AnalysisMarkingClass.None)]
    [InlineData("capped", "capped", true, AnalysisMarkingClass.Capped)]
    public async Task AStoredAssessmentReadThroughTheCommandClientBuildsTheMarkingClass(
        string name, string outcome, bool capped, AnalysisMarkingClass expected)
    {
        using var project = await CreateProjectAsync();
        var token = AnalyzedToken(project);
        var stored = Assert.Single(token.StoredAnalyses);
        var exact = StoredReading(stored);
        var readings = name switch
        {
            "same" => new[] { exact },
            "different" => [Guessed("different reading")],
            "extra" => new[] { exact, Guessed("extra reading") },
            "none" => Array.Empty<ParseAnalysis>(),
            _ => [Guessed("partial reading")],
        };
        RecordAssessment(project, [new AssessedWord(token.Form!, outcome, [])
        {
            IsIncomplete = capped,
            Morphology = new ParseWordEvidence("v1", 0, token.Form!, 1, capped, false, false, readings, []),
        }]);

        var storedEvidence = await project.Client.ReadCurrentEvidenceAsync(project.Project.FwDataPath,
            CancellationToken.None);
        Assert.True(storedEvidence.Succeeded, storedEvidence.Refusal?.Message);
        var assessment = Assert.IsType<AssessCommandResponse>(storedEvidence.Value!.Assessment);
        var result = Assert.Single(assessment.Words);

        Assert.Equal(expected, AnalysisMarkingState.Create(token, result).PanGlossClass);
    }

    private async Task AssertStoredChoiceAsync(string opinion, string kind)
    {
        using var project = await CreateProjectAsync(opinion);
        var token = AnalyzedToken(project);
        var analysis = Assert.Single(token.StoredAnalyses);
        var request = new ChangeIntent(CanonicalId.Mint().Value, kind, token.WordformId!, token.Form!,
            StoredAnalysisId: analysis.StoredAnalysisId, OriginPage: "Texts", Occurrence: token.Occurrence);

        var response = await PutAsync(project, request);

        var staged = Assert.Single(response.Changes);
        Assert.Equal(kind, staged.Kind);
        Assert.Equal(analysis.StoredAnalysisId, staged.StoredAnalysisId);
        Assert.Equal(token.Occurrence, staged.Occurrence);
        Assert.Contains(response.FitSummary, fit => fit.ChangeId == staged.ChangeId);
    }

    private async Task AssertParserChoiceAsync(string form, string kind,
        IReadOnlyList<ParseAnalysis> readings, int? occurrenceIndex, bool keepStoredReading = false)
    {
        using var project = await CreateProjectAsync();
        var token = TokenOf(project, form);
        if (keepStoredReading)
        {
            var stored = Assert.Single(token.StoredAnalyses);
            readings = [StoredReading(stored), .. readings];
        }
        var assessmentId = RecordAssessment(project, [new AssessedWord(form, "analysed", [])
        {
            Morphology = new ParseWordEvidence("v1", 0, form, 1, false, false, false, readings, []),
        }]);
        var index = occurrenceIndex ?? 0;
        var request = new ChangeIntent(CanonicalId.Mint().Value, kind, token.WordformId!, form,
            assessmentId, readings[index], ReadingIndex: index, OriginPage: "Texts",
            Occurrence: kind == ChangeKinds.AddCandidate ? null : token.Occurrence);

        var response = await PutAsync(project, request);

        var staged = Assert.Single(response.Changes);
        Assert.Equal(kind, staged.Kind);
        Assert.Equal(kind == ChangeKinds.AddCandidate ? null : token.Occurrence, staged.Occurrence);
        Assert.Null(staged.StoredAnalysisId);
        Assert.Contains(response.FitSummary, fit => fit.ChangeId == staged.ChangeId);
    }

    private async Task<MarkingCommandProject> CreateProjectAsync(string? opinion = null)
    {
        var project = new WalkthroughProject(pristine);
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
        {
            var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(project.Text.TextId);
            var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>()
                .GetObject(project.Text.ApprovedAnalysisId);
            var evaluation = opinion switch
            {
                "approved" => Opinions.approves,
                "disapproved" => Opinions.disapproves,
                "unknown" => Opinions.noopinion,
                _ => (Opinions?)null,
            };
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                foreach (var paragraph in text.ContentsOA!.ParagraphsOS.OfType<IStTxtPara>())
                    paragraph.ParseIsCurrent = true;
                if (evaluation is { } value)
                    cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, value);
            });
        });

        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        var captured = await client.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath),
            CancellationToken.None);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var selection = await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
            project.FwDataPath, Path.GetFileNameWithoutExtension(project.FwDataPath),
            [project.Text.TextId], []), CancellationToken.None);
        Assert.True(selection.Succeeded, selection.Refusal?.Message);
        var words = await client.ListTextWordsAsync(new TextWordsRequest(project.FwDataPath,
            [project.Text.TextId]), CancellationToken.None);
        Assert.True(words.Succeeded, words.Refusal?.Message);
        return new MarkingCommandProject(project, client, captured.Value!.Token, words.Value!);
    }

    private static string RecordAssessment(MarkingCommandProject project, IReadOnlyList<AssessedWord> words)
    {
        var locator = new ProjectLocator(Path.GetFullPath(project.Project.FwDataPath),
            Path.GetFileNameWithoutExtension(project.Project.FwDataPath));
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(locator), locator,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(locator))!;
        using var baselineCache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
        var composition = SelectionComposer.Compose(baselineCache,
            new SelectionRequest(false, [project.Project.Text.TextId], [], false, null),
            new AssessmentRepository(database), JsonSerializer.Serialize(project.BaselineToken,
                MotifJson.CreateOptions()));
        Assert.True(composition.Succeeded, composition.Refusal?.Message);
        var assessmentId = "assessment/marking/" + Guid.NewGuid().ToString("N");
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            assessmentId, null, null, "test", AssessmentKind.ParseTime.ToStoredKind(), "{}", "sha256:scope",
            "whitespace-and-punctuation", "1", JsonSerializer.Serialize(project.BaselineToken,
                MotifJson.CreateOptions()), composition.Value!.Selection, "sha256:outcome", "sha256:semantic",
            "sha256:grammar", "model", "pipeline", 0, words,
            SavedUtc: DateTimeOffset.UtcNow.ToString("O")));
        return assessmentId;
    }

    private static async Task<PendingChangesSnapshot> PutAsync(MarkingCommandProject project, ChangeIntent change)
    {
        var version = SIL.Motif.Host.MotifProductVersion.CurrentText;
        var loaded = await project.Client.LoadPendingChangesAsync(
            new PendingChangesRequest(project.Project.FwDataPath, version), CancellationToken.None);
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var outcome = await project.Client.PutPendingChangeAsync(new PutPendingChangeRequest(
            project.Project.FwDataPath, version, loaded.Value!.Revision, change), CancellationToken.None);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Code + ": " + outcome.Refusal?.Message);
        return outcome.Value!;
    }

    private static TextToken AnalyzedToken(MarkingCommandProject project) =>
        TokenOf(project, SeededProject.AnalysedWordForm);

    private static TextToken TokenOf(MarkingCommandProject project, string form) =>
        Assert.Single(project.Words.Texts.SelectMany(text => text.Lines)
            .SelectMany(line => line.Tokens), token => token.Form == form);

    private static ParseAnalysis Guessed(string text) => new([new ParseMorph(null, null, null, text)]);

    private static ParseAnalysis StoredReading(ProjectAnalysis analysis) => new(
        analysis.Identity!.Morphs.Select(morph => new ParseMorph(morph.Form, morph.Msa, morph.InflType, null)).ToArray());

    private sealed record MarkingCommandProject(WalkthroughProject Project, CommandClient Client,
        SIL.Motif.Contract.Baselines.BaselineToken BaselineToken, TextWordsResponse Words) : IDisposable
    {
        public void Dispose() => Project.Dispose();
    }
}
