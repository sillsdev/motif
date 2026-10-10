using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AnalysisOperationBulkStagingTests
{
    private const string ProjectPath = @"C:\projects\bulk.fwdata";
    private const string NextProjectPath = @"C:\projects\next.fwdata";
    private const string AssessmentId = "assessment/bulk";
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly Guid TextId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ParagraphId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SegmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly string[] Forms = ["first", "second", "third"];
    private static readonly Guid[] WordformIds =
    [
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
    ];

    [Theory]
    [InlineData(BulkAction.AddParserReadings)]
    [InlineData(BulkAction.IncorrectSpellings)]
    [InlineData(BulkAction.AcceptNewSet)]
    public void BulkActionStopsAtRevisionConflictAndKeepsEarlierChange(BulkAction action)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var project = await OpenProjectAsync();
            var refusal = new Refusal(RefusalCodes.ChangeRevisionConflict, FailureReason.Refused,
                "The pending changes changed elsewhere.");
            var latest = EmptySnapshot();
            var putCalls = 0;
            var reloads = 0;

            project.Client.PendingLoadHandler = (_, _) =>
            {
                reloads++;
                return Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(latest));
            };
            project.Client.PendingPutHandler = (request, _) =>
            {
                putCalls++;
                if (putCalls == 2)
                    return Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Refused(refusal));
                latest = WithChange(latest, FromIntent(request.Change));
                return Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(latest));
            };
            switch (action)
            {
                case BulkAction.AddParserReadings:
                    await project.InText.AddParserReadingsCommand.ExecuteAsync(AnalysisOperationScope.ChosenTexts);
                    break;
                case BulkAction.IncorrectSpellings:
                    await project.InText.MarkSpellingsIncorrectCommand.ExecuteAsync(AnalysisOperationScope.ChosenTexts);
                    break;
                case BulkAction.AcceptNewSet:
                    foreach (var token in SelectionModelFixture.VisibleLines(project.InText)
                                 .SelectMany(line => line.Tokens).Where(token => token.IsWord).Take(3))
                        token.IsSelectedForActions = true;
                    await project.InText.AcceptNewSetCommand.ExecuteAsync(AnalysisOperationScope.CheckedWords);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }

            var requestCount = project.Client.PendingPutRequests.Count;
            if (action == BulkAction.AcceptNewSet)
            {
                Assert.NotNull(project.Client.PendingPutRequests[0].Change.GroupId);
                Assert.Equal(project.Client.PendingPutRequests[0].Change.GroupId, project.Client.PendingPutRequests[1].Change.GroupId);
            }
            Assert.Equal(2, requestCount);
            Assert.Equal(1, reloads);
            var firstChange = Assert.Single(project.InText.Changes.Snapshot.Changes);
            Assert.Equal(Forms[0], firstChange.Word);
            Assert.Equal("revision/1", project.InText.Changes.Snapshot.Revision);
            Assert.DoesNotContain(project.InText.Changes.Snapshot.Changes, change => change.Word == Forms[2]);
            Assert.Single(project.InText.Changes.Items);
            Assert.Equal(firstChange.ChangeId, project.InText.Changes.Items[0].ChangeId);
            Assert.Equal(refusal.Code, project.InText.Changes.LastRefusal?.Code);
            Assert.Equal(refusal.Message, project.InText.Changes.LastRefusal?.Message);
            Assert.True(project.InText.Changes.HasError);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ProjectChangeDuringSpellingRequestStopsTheRemainingWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var project = await OpenProjectAsync();
            var entered = new TaskCompletionSource<PutPendingChangeRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<CommandOutcome<PendingChangesSnapshot>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            project.Client.PendingPutHandler = (request, _) =>
            {
                entered.TrySetResult(request);
                return release.Task;
            };

            var action = project.InText.MarkSpellingsIncorrectCommand.ExecuteAsync(AnalysisOperationScope.ChosenTexts);
            var firstRequest = await entered.Task;
            await project.InText.Changes.OpenProjectAsync(NextProjectPath);
            release.SetResult(CommandOutcome<PendingChangesSnapshot>.Success(
                WithChange(EmptySnapshot(), FromIntent(firstRequest.Change))));

            await action;

            Assert.Single(project.Client.PendingPutRequests);
            Assert.Equal(ProjectPath, project.Client.PendingPutRequests[0].FwDataPath);
            Assert.Equal(NextProjectPath, project.InText.Changes.ProjectPath);
            Assert.Empty(project.InText.Changes.Items);
        }, TimeSpan.FromSeconds(180));
    }

    private static async Task<TestProject> OpenProjectAsync()
    {
        var client = new FakeCommandClient();
        client.ReadWordStateCompletesWith(new WordReadStateResponse([], true));
        var selection = new SelectionViewModel(client) { AllWordforms = true };
        var texts = new TextWordsViewModel(client, selection, client.ReaderOwner);
        var assess = new AssessViewModel(client, selection) { ProjectPath = ProjectPath };
        var changes = new ChangesViewModel(client);
        await changes.OpenProjectAsync(ProjectPath);
        var line = new TextLine(1, Forms.Select((form, index) => new TextToken(form, form, null, "unanalysed")
        {
            WordformId = WordformIds[index],
            OccurrenceIndex = index,
        }).ToArray())
        {
            ParagraphId = ParagraphId,
            SegmentId = SegmentId,
            ParseIsCurrent = true,
        };
        var source = new TextWordsResponse([], [new TextLines(TextId, "Bulk text", [line])], true);
        var fixture = new SelectionModelFixture(client);
        var inText = new ResultsInTextViewModel(texts, assess, _ => { }, _ => { }, changes, client, client.ReaderOwner);
        await texts.SetProjectAsync(ProjectPath);
        client.AssessCompletesWith(SelectionModelFixture.WithOrigins(Assessment(Forms)));
        await assess.RunCommand.ExecuteAsync(null);
        await fixture.PublishAsync(source, assess.Result);
        await SelectionModelFixture.RealizeAsync(inText);
        changes.AssessmentId = AssessmentId;
        return new TestProject(inText, client, texts, fixture);
    }

    private static AssessCommandResponse Assessment(IReadOnlyList<string> words) =>
        new(new BaselineCaptureResponse(new BaselineToken("project-1", Digest, "1", "2026-09-05T00:00:00Z", Digest),
                ProjectPath, DateTimeOffset.UtcNow, FieldWorksHeldProject: false, ReusedExistingBytes: true),
            new SelectionProjection([], []), [AssessmentId], "(summary)")
        {
            Measurements = [new ProducedAssessmentReference(AssessmentId, AssessmentKinds.ParseTime, "invocation/one")],
            Words = words.Select(AssessmentWord).ToArray(),
        };

    private static AssessmentWordResult AssessmentWord(string word)
    {
        var reading = new ParseAnalysis([new ParseMorph(word, "bbbbbbbb-0000-0000-0000-000000000001", null, null)]);
        return new AssessmentWordResult(word, "analysed", false, "Complete", 3, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, word, 3, false, false, false, [reading], []),
            Readings = [new ParserReading([new ParserReadingMorph(word, "gloss", "n", null, false, null)])],
            ReadingGrades = [ReadingGrade.NoOpinion],
        };
    }

    private static PendingChangesSnapshot EmptySnapshot() => new(null, "none", [], []);

    private static PendingChangesSnapshot WithChange(PendingChangesSnapshot snapshot, PendingChange change) =>
        new(snapshot.DraftId ?? "draft/bulk", $"revision/{snapshot.Changes.Count + 1}",
            snapshot.Changes.Append(change).ToArray(), []);

    private static PendingChange FromIntent(ChangeIntent change) =>
        new(change.ChangeId, change.WordformId, change.Word, change.Kind, change.AssessmentId,
            change.DisplayReading, [change.ChangeId]);

    private sealed record TestProject(ResultsInTextViewModel InText, FakeCommandClient Client,
        TextWordsViewModel Words, SelectionModelFixture Fixture) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await InText.StopAsync();
            await Words.StopAsync();
            await Fixture.DisposeAsync();
        }
    }

    public enum BulkAction
    {
        AddParserReadings,
        IncorrectSpellings,
        AcceptNewSet,
    }
}
