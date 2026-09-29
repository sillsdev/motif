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
public sealed class ConformanceGrammarAssessTests
{
    [RealParserFact]
    public async Task TheConformanceGrammarGivesItsKnownAnalysesAndHandoffIncludesEveryEntry()
    {
        using var project = new ConformanceProject();
        var client = RealCommandClient.Create(project.ManagedRoot);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var selection = new SelectionRequest(false, [],
            [ConformanceProject.OneAnalysisShort, ConformanceProject.OneAnalysisLong,
                ConformanceProject.NineHundredTwentyFour], false, null);

        var assessed = await client.AssessAsync(new AssessRequest(project.FwDataPath, selection,
            PerWordLimitMs: 120_000, PerWordStepLimit: new StepCap(100_000_000)),
            new Progress<AssessmentProgress>(), timeout.Token);

        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
        var words = assessed.Value!.Words;
        Assert.Equal(3, words.Count);
        AssertAnalyses(words, ConformanceProject.OneAnalysisShort, 1);
        AssertAnalyses(words, ConformanceProject.OneAnalysisLong, 1);
        AssertAnalyses(words, ConformanceProject.NineHundredTwentyFour, 924);

        var handoffParent = Path.Combine(
            Path.GetTempPath(), "Motif.Conformance.Handoff", Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(handoffParent, "handoff");
        try
        {
            var handedOff = await client.HandoffAsync(new HandoffRequest(
                    project.FwDataPath, outputDirectory, selection, Assess: false,
                    InvocationId: assessed.Value.InvocationId),
                new Progress<AssessmentProgress>(), timeout.Token);
            Assert.True(handedOff.Succeeded, handedOff.Refusal?.Message);

            var grammarPath = Path.Combine(outputDirectory, "grammar.json");
            Assert.True(File.Exists(grammarPath), grammarPath);
            using var grammar = System.Text.Json.JsonDocument.Parse(File.ReadAllText(grammarPath));
            var entries = grammar.RootElement.GetProperty("lexicon").GetProperty("entries");
            Assert.Equal(13, entries.GetArrayLength());
        }
        finally
        {
            WalkthroughTestFiles.DeleteDirectory(handoffParent);
        }

        Assert.Equal(project.SourceSha256, Walkthrough.WalkthroughStoreAssertions.Sha256(project.FwDataPath));
    }

    private static void AssertAnalyses(IReadOnlyList<AssessmentWordResult> words, string spelling, int count)
    {
        var word = Assert.Single(words, result => result.Word == spelling);
        Assert.Equal("analysed", word.Outcome);
        Assert.False(word.IsIncomplete, word.CompletionStatus);
        Assert.NotNull(word.Morphology);
        Assert.Equal(count, word.Morphology!.Analyses.Count);
    }
}
