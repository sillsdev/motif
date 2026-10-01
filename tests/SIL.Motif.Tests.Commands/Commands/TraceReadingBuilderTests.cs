using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class TraceReadingBuilderTests
{
    [Fact]
    public void MatinluAttemptsKeepTheStemLookupInTheirStory()
    {
        var response = WordTraceQuery.LoadDiagnostic(ReadFixture()).Value!;

        Assert.All(response.Candidates, candidate =>
            Assert.Contains(candidate.Steps, step => step.Type == "LexicalLookup"));
    }

    [Fact]
    public void BestPathRulesReadInBuildingOrder()
    {
        var response = WordTraceQuery.LoadDiagnostic(ReadFixture()).Value!;

        Assert.Equal(["ma", "lu"], response.Reading!.RulesOnBestPath.Select(rule => rule.Rule));
    }

    [Fact]
    public void DuplicateMatinluAnalysesBecomeOneAnalysis()
    {
        var response = WordTraceQuery.LoadDiagnostic(ReadFixture()).Value!;

        var analysis = Assert.Single(response.Analyses);
        Assert.Equal(2, analysis.FoundWays);
        Assert.Equal(["analysis-0", "analysis-1"], analysis.ProducerAnalysisIds);
    }

    [Fact]
    public void UnavailableFeaturesNeverBecomeRawJsonInAMorph()
    {
        var response = WordTraceQuery.LoadDiagnostic(ReadFixture()).Value!;
        var morphs = response.Candidates.SelectMany(candidate => candidate.RichMorphs).ToArray();

        Assert.NotEmpty(morphs);
        Assert.All(morphs.Where(morph => morph.FeaturesStatus == "unavailable"),
            morph => Assert.Null(morph.Features));
    }

    [Fact]
    public void EveryRecordedStepHasAChildIndexPath()
    {
        var response = WordTraceQuery.LoadDiagnostic(ReadFixture()).Value!;
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(response.Candidates));

        Assert.All(serialized.RootElement.EnumerateArray(), candidate =>
            Assert.All(candidate.GetProperty("Steps").EnumerateArray(), step =>
                Assert.True(step.TryGetProperty("StepId", out var id) && id.GetString() is { Length: > 0 })));
    }

    [Fact]
    public void KumataKeepsTheStemLookupTemplateAndMorphIdentity()
    {
        var document = PanGlossTraceDiagnosticReader.Read(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v2-kumata.json")));
        var reading = TraceReadingBuilder.Build(document);

        Assert.Equal("kumata", reading.Word);
        Assert.All(reading.Attempts, attempt =>
        {
            Assert.Contains(attempt.Steps, step => step.Type == "LexicalLookup" && step.Input == "kuma");
            Assert.Contains(attempt.Steps, step => step.Type == "TemplateSynthesisInput" && step.Source == "NounTemplate");
        });
        var stem = Assert.Single(reading.Analyses).Morphs[0];
        Assert.Equal("00000000-0000-0000-0000-000000000106", stem.FormSourceId);
        Assert.Equal("00000000-0000-0000-0000-000000000107", stem.GlossSourceId);
        Assert.Null(stem.Features);
    }

    [Fact]
    public void APhonologicalLeafSiblingStaysWithItsAttemptWithoutBorrowingAnotherBranch()
    {
        var tree = """
            {"type":"WordAnalysis","children":[
              {"type":"MorphologicalRuleAnalysis","source":"plural","children":[
                {"type":"LexicalLookup","inputShape":"kuma","children":[]},
                {"type":"TemplateSynthesisInput","source":"NounTemplate","children":[]},
                {"type":"MorphologicalRuleSynthesis","source":"plural","outputShape":"kumata","children":[
                  {"type":"PhonologicalRuleSynthesis","source":"Harmony","inputShape":"kumata","outputShape":"kumata","children":[]},
                  {"type":"PhonologicalRuleSynthesis","source":"Assimilation","inputShape":"kumata","outputShape":"kumata","children":[]},
                  {"type":"Successful","outputShape":"kumata","children":[]}]}]},
              {"type":"MorphologicalRuleAnalysis","source":"other","children":[
                {"type":"Failed","failureReason":"PartialParse","children":[]}]}]}
            """;
        var reading = TraceReadingBuilder.Build(PanGlossTraceDiagnosticReader.Read(TraceEnvelope.Of("", tree)));

        Assert.Equal(["Harmony", "Assimilation"], reading.Attempts[0].Steps
            .Where(step => step.Type == "PhonologicalRuleSynthesis").Select(step => step.Source));
        Assert.DoesNotContain(reading.Attempts[1].Steps, step => step.Type is "PhonologicalRuleSynthesis" or "LexicalLookup");
        Assert.Contains(reading.RulesOnBestPath, rule => rule.Rule == "Harmony");
    }

    [Fact]
    public void APhonologicalContractionKeepsItsRecordedBuildingDirection()
    {
        var tree = """
            {"type":"WordAnalysis","children":[
              {"type":"PhonologicalRuleSynthesis","source":"Contraction","inputShape":"kuma","outputShape":"kum","children":[
                {"type":"Successful","outputShape":"kum","children":[]}]}]}
            """;
        var reading = TraceReadingBuilder.Build(PanGlossTraceDiagnosticReader.Read(TraceEnvelope.Of("", tree)));

        Assert.Equal("kuma → kum", Assert.Single(reading.RulesOnBestPath).Explanation);
    }

    [Fact]
    public void SavingAndLoadingKeepsEveryStepIdAndItsOriginalTreeLocation()
    {
        var response = WordTraceQuery.LoadDiagnostic(ReadFixture()).Value!;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".trace.json");
        try
        {
            File.WriteAllText(path, response.DiagnosticJson);
            var loaded = WordTraceQuery.LoadDiagnostic(File.ReadAllText(path)).Value!;
            Assert.Equal(response.Candidates.SelectMany(candidate => candidate.Steps).Select(step => step.StepId),
                loaded.Candidates.SelectMany(candidate => candidate.Steps).Select(step => step.StepId));
            foreach (var step in loaded.Candidates.SelectMany(candidate => candidate.Steps))
            {
                var node = loaded.Root;
                foreach (var index in step.StepId.Split('.').Skip(1)) node = node.Children[int.Parse(index)];
                Assert.Equal(step.StepId, node.StepId);
                Assert.Equal(step.Type, node.Type);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MatinluRetainsGrammarLocalMorphAndAllomorphIds()
    {
        var reading = TraceReadingBuilder.Build(PanGlossTraceDiagnosticReader.Read(ReadFixture()));
        var morph = reading.Attempts[0].RichMorphs[0];

        Assert.Equal(0, morph.MorphemeId);
        Assert.Equal(0, morph.AllomorphId);
        Assert.Null(Assert.Single(morph.SourceFormIds));
        Assert.Equal("grammar-local", morph.IdentityQuality);
    }

    private static string ReadFixture() => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v2-matinlu.json"));
}
