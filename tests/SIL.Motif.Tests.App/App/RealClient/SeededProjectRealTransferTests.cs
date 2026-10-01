using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class SeededProjectRealTransferTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task AssessmentAndHandoffTransferTheRetainedResultWithoutChangingTheSavedProject()
    {
        using var project = new WalkthroughProject(pristine);
        var client = RealCommandClient.Create(project.ManagedRoot);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var expectedWords = new[] { SeededProject.FirstForm, SeededProject.SecondForm };
        var selection = new SelectionRequest(false, [], expectedWords, false, null);

        var assessed = await client.AssessAsync(new AssessRequest(project.FwDataPath, selection,
                PerWordLimitMs: 120_000, PerWordStepLimit: new StepCap(100_000_000)),
            new Progress<AssessmentProgress>(), timeout.Token);

        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
        var assessment = assessed.Value!;
        Assert.Equal(expectedWords.Order(StringComparer.Ordinal),
            assessment.Selection.Words.Order(StringComparer.Ordinal));
        Assert.Equal(expectedWords.Order(StringComparer.Ordinal),
            assessment.Words.Select(word => word.Word).Order(StringComparer.Ordinal));
        Assert.All(assessment.Words, word =>
        {
            Assert.Equal("analysed", word.Outcome);
            Assert.False(word.IsIncomplete, word.CompletionStatus);
            Assert.NotNull(word.Morphology);
            Assert.NotEmpty(word.Morphology!.Analyses);
        });
        Assert.NotEmpty(assessment.AssessmentIds);
        var retainedInvocationCount = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath).Count;
        var handoffProgress = new List<AssessmentProgress>();

        var handoffParent = Path.Combine(
            Path.GetTempPath(), "Motif.SeededProject.Handoff", Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(handoffParent, "handoff");
        try
        {
            var handedOff = await client.HandoffAsync(new HandoffRequest(
                    project.FwDataPath, outputDirectory, selection, Assess: true,
                    InvocationId: assessment.InvocationId),
                new SynchronousProgress<AssessmentProgress>(handoffProgress.Add), timeout.Token);
            Assert.True(handedOff.Succeeded, handedOff.Refusal?.Message);
            Assert.Contains(AssessmentStage.ImportingGrammar, handoffProgress.Select(step => step.Stage));
            Assert.Contains(AssessmentStage.Complete, handoffProgress.Select(step => step.Stage));

            var transfer = handedOff.Value!;
            Assert.Equal(assessment.InvocationId, transfer.InvocationId);
            Assert.Equal(assessment.AssessmentIds.Order(StringComparer.Ordinal),
                transfer.AssessmentIds.Order(StringComparer.Ordinal));
            Assert.Equal(expectedWords.Order(StringComparer.Ordinal),
                transfer.Selection.Words.Order(StringComparer.Ordinal));
            var grammarPath = Path.Combine(outputDirectory, "grammar.json");
            Assert.True(File.Exists(grammarPath), grammarPath);
            using var grammar = System.Text.Json.JsonDocument.Parse(File.ReadAllText(grammarPath));
            var entries = grammar.RootElement.GetProperty("lexicon").GetProperty("entries").EnumerateArray();
            Assert.NotEmpty(entries);
            Assert.Equal(retainedInvocationCount,
                WalkthroughStoreAssertions.ListInvocations(project.FwDataPath).Count);
        }
        finally
        {
            WalkthroughTestFiles.DeleteDirectory(handoffParent);
        }

        Assert.Equal(project.SourceSha256, WalkthroughStoreAssertions.Sha256(project.FwDataPath));
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
