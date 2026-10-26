using System.Security.Cryptography;
using SIL.LCModel;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class ConformanceGrammarAssessTests
{
    [RealParserFact]
    public void TheConformanceGrammarGivesItsKnownAnalyses()
    {
        using var project = new ConformanceProject();
        var selection = new SelectionRequest(false, [], [
            project.OneAnalysisShort,
            project.OneAnalysisLong,
            project.NineHundredTwentyFour,
        ], false, null);

        var outcome = AssessCommand.Assess(
            new AssessRequest(project.FwDataPath, selection), project.ManagedRoot);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var result = outcome.Value!;
        Assert.Equal(3, result.Words.Count);
        AssertBoundaryResult(result, project.OneAnalysisShort);
        AssertBoundaryResult(result, project.OneAnalysisLong);

        var midpoint = Assert.Single(result.Words, word => word.Word == project.NineHundredTwentyFour);
        Assert.True(midpoint.IsIncomplete || midpoint.Outcome == "analysed");
        if (midpoint.IsIncomplete)
        {
            Assert.NotNull(midpoint.Morphology);
            Assert.NotEmpty(midpoint.CompletionStatus);
            Assert.Contains("limit", midpoint.CompletionStatus, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.NotNull(midpoint.Morphology);
            Assert.Equal(924, midpoint.Morphology!.Analyses.Count);
        }

        var sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(project.FwDataPath)));
        Assert.Equal(project.SourceSha256, sourceHash);
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
}

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class ConformanceProjectFixtureTests
{
    [Fact]
    public void CopiedConformanceProjectLoadsWithThirteenLexicalEntries()
    {
        using var project = new ConformanceProject();
        using var cache = new FwDataProjectLoader().LoadScratchCache(project.FwDataPath);

        Assert.Equal(13, cache.ServiceLocator.GetInstance<ILexEntryRepository>().Count);
    }
}
