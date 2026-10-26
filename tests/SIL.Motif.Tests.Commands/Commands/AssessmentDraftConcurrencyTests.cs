using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class AssessmentDraftConcurrencyTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task DraftWriteCompletesAndRemainsReadableWhileAssessmentIsParsing()
    {
        var projectPath = CreateSeededProjectPath();
        const string draftName = "assessment-concurrency";
        const string gloss = "authored during assessment";
        var words = Enumerable.Range(0, 7206).Select(index => $"synthetic-{index:D4}").ToArray();
        var version = MotifProductVersion.CurrentText;
        var created = ProposalCommands.New(new NewDraftRequest(projectPath, version, draftName, "Concurrency"));
        Assert.True(created.Succeeded, created.Refusal?.Message);

        await using var assessment = new PausedAssessment(projectPath, words);
        Assert.True(assessment.WaitForParser(TimeSpan.FromSeconds(30)),
            "The Assessment did not reach parser work.");

        var edited = ProposalCommands.AddSetGloss(new AddSetGlossRequest(projectPath, version, draftName,
            CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, NewLangProjFixture.AnalysisTag, gloss));

        Assert.True(edited.Succeeded, edited.Refusal?.Message);
        Assert.Equal(1, edited.Value!.OperationCount);
        assessment.ReleaseParser();
        var assessed = await assessment.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
        Assert.Equal(words.Length, assessed.Value!.Words.Count);

        var shown = ProposalCommands.Show(new ShowProposalRequest(projectPath, version, draftName));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Contains(gloss, shown.Value!.Operations.Single().AfterJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssessmentIsJoinedAndItsRootDeletedAfterAnEarlyTestFailure()
    {
        var projectPath = CreateSeededProjectPath();
        var words = Enumerable.Range(0, 7206).Select(index => $"synthetic-{index:D4}").ToArray();
        var assessment = new PausedAssessment(projectPath, words);
        var completion = assessment.Completion;
        var root = assessment.Root;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using (assessment)
            {
                Assert.True(assessment.WaitForParser(TimeSpan.FromSeconds(30)),
                    "The Assessment did not reach parser work.");
                Assert.False(completion.IsCompleted);
                throw new InvalidOperationException("forced early test-body failure");
            }
        });

        Assert.Equal("forced early test-body failure", failure.Message);
        Assert.True(completion.IsCompleted);
        Assert.False(Directory.Exists(root));
    }

    private string CreateSeededProjectPath()
    {
        using var cache = pristine.NewScratch();
        SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        return cache.ProjectId.Path;
    }

    private sealed class PausedAssessment : IAsyncDisposable
    {
        private readonly ManualResetEventSlim _parserStarted = new();
        private readonly ManualResetEventSlim _resumeParser = new();
        private readonly CancellationTokenSource _cancellation = new();

        public PausedAssessment(string projectPath, string[] words)
        {
            Root = Directory.CreateTempSubdirectory("motif-assessment-draft-concurrency-").FullName;
            var assessor = new FakeAssessor("paused-assessor",
                [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming],
                rawForScope: (scope, _, kind) => kind switch
                {
                    AssessmentKind.ParseTime => new AssessmentRaw.Batch(new BatchAnalysis(
                        scope.WordForms.Select((word, index) =>
                            new WordAnalysis(index, word, 1, WordOutcome.NoAnalysis, "none")).ToArray(),
                        1000, projectPath, [])),
                    AssessmentKind.ObjectTiming => new AssessmentRaw.WordMeasurements([]),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                })
            {
                CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(Root, scope, candidate),
                EmitProgress = _ =>
                {
                    _parserStarted.Set();
                    if (!_resumeParser.Wait(TimeSpan.FromMinutes(2), _cancellation.Token))
                        throw new TimeoutException("The parser hold was not released.");
                },
            };

            Completion = Task.Run(() => AssessCommand.Run(
                new AssessRequest(projectPath, new SelectionRequest(false, [], words, false, null)),
                Path.Combine(Root, "managed"), assessor, new FakeInvoker(), null, _cancellation.Token));
        }

        public string Root { get; }

        public Task<CommandOutcome<AssessCommandResponse>> Completion { get; }

        public bool WaitForParser(TimeSpan timeout) => _parserStarted.Wait(timeout);

        public void ReleaseParser() => _resumeParser.Set();

        public async ValueTask DisposeAsync()
        {
            _resumeParser.Set();
            try { _cancellation.Cancel(); }
            finally
            {
                try
                {
                    try { await Completion.ConfigureAwait(false); }
                    catch
                    {
                        // Observe teardown faults without replacing the test-body failure already in flight.
                    }
                }
                finally
                {
                    try { Directory.Delete(Root, recursive: true); }
                    finally
                    {
                        _parserStarted.Dispose();
                        _resumeParser.Dispose();
                        _cancellation.Dispose();
                    }
                }
            }
        }
    }
}
