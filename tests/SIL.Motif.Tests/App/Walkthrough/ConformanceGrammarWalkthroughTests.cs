using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ConformanceGrammarWalkthroughTests(ITestOutputHelper output)
{
    [RealParserFact]
    public void RealWindowWalksConformanceGrammarFromBaselineToHandoff()
    {
        using var project = new ConformanceProject();
        var handoffParent = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Conformance.Handoff", Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(handoffParent, "handoff");

        try
        {
            var deadline = Stopwatch.GetTimestamp() + 660 * Stopwatch.Frequency;
            AvaloniaHeadlessFixture.RunUntilComplete(() =>
            {
                using var walkthrough = new WalkthroughWindow(
                    project.ManagedRoot, project.FwDataPath, outputDirectory);
                walkthrough.Show();
                Assert.Equal(0, walkthrough.Find<ComboBox>("Known projects").ItemCount);

                walkthrough.Click("Browse for a FieldWorks project file");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Baseline.CapturedTimeText == "No Baseline captured yet" &&
                        walkthrough.Workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts.",
                    WalkthroughSteps.Remaining(deadline), "choosing the conformance project did not show no Baseline");
                Assert.Null(walkthrough.Workspace.Baseline.RefusalMessage);
                Assert.Null(walkthrough.Workspace.Selection.RefusalMessage);

                walkthrough.Click("Refresh the Baseline");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Baseline.HasBaseline &&
                        walkthrough.Workspace.Selection.TextsEmptyMessage == "This Baseline has no Texts.",
                    WalkthroughSteps.Remaining(deadline), "refreshing the conformance project did not complete");
                Assert.Equal("This Baseline has no Texts.", walkthrough.Workspace.Selection.TextsEmptyMessage);
                Assert.Null(walkthrough.Workspace.Baseline.RefusalMessage);
                Assert.Null(walkthrough.Workspace.Selection.RefusalMessage);

                walkthrough.Type(
                    "Pasted words",
                    string.Join(Environment.NewLine,
                        ConformanceProject.OneAnalysisShort,
                        ConformanceProject.OneAnalysisLong,
                        ConformanceProject.NineHundredTwentyFour));
                Assert.True(walkthrough.Find<Button>("Run the Assessment").IsEffectivelyEnabled);

                var runStarted = Stopwatch.GetTimestamp();
                walkthrough.Click("Run the Assessment");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Assess.State == RunState.Completed,
                    TimeSpan.FromSeconds(300), "the conformance Assessment did not complete");
                var runElapsed = Stopwatch.GetElapsedTime(runStarted);

                Assert.NotNull(walkthrough.Workspace.Assess.Result);
                var result = walkthrough.Workspace.Assess.Result!;
                Assert.Equal(3, result.Words.Count);
                AssertBoundaryResult(result, ConformanceProject.OneAnalysisShort);
                AssertBoundaryResult(result, ConformanceProject.OneAnalysisLong);

                var midpoint = Assert.Single(
                    result.Words, word => word.Word == ConformanceProject.NineHundredTwentyFour);
                Assert.False(
                    !midpoint.IsIncomplete && (midpoint.Morphology?.Analyses.Count ?? 0) == 0,
                    "A completed midpoint result returned zero analyses.");
                string midpointBranch;
                if (midpoint.IsIncomplete)
                {
                    Assert.NotNull(midpoint.Morphology);
                    Assert.NotEmpty(midpoint.CompletionStatus);
                    Assert.Contains("limit", midpoint.CompletionStatus, StringComparison.OrdinalIgnoreCase);
                    midpointBranch = "incomplete: " + midpoint.CompletionStatus;
                }
                else
                {
                    Assert.Equal("analysed", midpoint.Outcome);
                    Assert.NotNull(midpoint.Morphology);
                    Assert.Equal(924, midpoint.Morphology!.Analyses.Count);
                    midpointBranch = "924 analyses";
                }

                output.WriteLine($"Assessment Run-to-Completed wall time: {runElapsed.TotalSeconds:F3} seconds.");
                output.WriteLine($"Midpoint branch: {midpointBranch}.");
                var retainedBeforeHandoff = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
                var assessmentInvocationId = result.InvocationId;

                walkthrough.Click("Write the Handoff folder");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Handoff.State == RunState.Completed,
                    TimeSpan.FromSeconds(300), "the conformance Handoff did not complete");

                var grammarPath = Path.Combine(outputDirectory, "grammar.json");
                Assert.True(File.Exists(grammarPath), grammarPath);
                using var grammar = JsonDocument.Parse(File.ReadAllText(grammarPath));
                var entries = grammar.RootElement.GetProperty("lexicon").GetProperty("entries");
                Assert.Equal(13, entries.GetArrayLength());
                Assert.Equal(assessmentInvocationId, walkthrough.Workspace.Handoff.Result!.InvocationId);
                Assert.Equal(retainedBeforeHandoff.Count,
                    WalkthroughStoreAssertions.ListInvocations(project.FwDataPath).Count);
                output.WriteLine("grammar.json lexical entry count JSON path: $.lexicon.entries.");
                Assert.Equal(project.SourceSha256, Sha256(project.FwDataPath));

                return Task.CompletedTask;
            }, WalkthroughSteps.Remaining(deadline));
        }
        finally
        {
            try { if (Directory.Exists(handoffParent)) Directory.Delete(handoffParent, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void CopiedConformanceProjectLoadsWithThirteenLexicalEntries()
    {
        using var project = new ConformanceProject();
        using var cache = new FwDataProjectLoader().LoadScratchCache(project.FwDataPath);

        var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>();
        Assert.Equal(13, entries.Count);
    }

    private static void AssertBoundaryResult(
        SIL.Motif.Contract.Responses.AssessCommandResponse result, string word)
    {
        var assessment = Assert.Single(result.Words, item => item.Word == word);
        Assert.Equal("analysed", assessment.Outcome);
        Assert.False(assessment.IsIncomplete);
        Assert.NotNull(assessment.Morphology);
        Assert.Single(assessment.Morphology!.Analyses);
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
