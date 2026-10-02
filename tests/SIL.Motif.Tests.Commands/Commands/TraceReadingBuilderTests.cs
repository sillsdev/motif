using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class TraceReadingBuilderTests
{
    [Fact]
    public void MatinluFailureEvidenceBelongsToTheEventThatSuppliesTheReason()
    {
        var reading = WordTraceQuery.LoadDiagnostic(ReadFixture()).Value!.Reading;
        var terminal = reading.Root.Children[2].Children[0].Children[3].Children[2];
        var rejection = reading.Root.Children[2].Children[0].Children[3].Children[1];
        Assert.Equal("0.2.0.3.2", terminal.StepId);
        Assert.Equal("PartialParse", terminal.FailureReason);
        Assert.Equal("0.2.0.3.1", rejection.StepId);
        Assert.Equal("NonPartialRuleProhibitedAfterFinalTemplate", rejection.FailureReason);
        Assert.Null(reading.Attempts.Single(attempt => attempt.AttemptId == terminal.StepId).StoppedByRefId);
        var failures = reading.Attempts.Where(attempt => !attempt.Succeeded).ToArray();

        Assert.NotEmpty(failures);
        Assert.All(failures, attempt =>
        {
            Assert.Equal("PartialParse", attempt.FailureReason);
            Assert.Equal(attempt.FailureReason, attempt.FailureEvidence!.ReasonCode);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATerminalEventNeverBorrowsSiblingRejectionOperands(bool hasContext)
    {
        var context = hasContext ? """
            {"reasonCode":"RequiredSyntacticFeatureStruct","reason":"rule context",
             "required":"rule required","actual":"rule actual","environment":"rule environment"}
            """ : "null";
        var tree = $$$"""
            {"type":"WordAnalysis","children":[
              {"type":"MorphologicalRuleSynthesis","source":"rule",
               "failureReason":"RequiredSyntacticFeatureStruct","failureContext":{{{context}}},"children":[]},
              {"type":"Failed","failureReason":"PartialParse","children":[],
               "failureContext":{"reasonCode":"PartialParse","reason":"terminal context",
                 "required":"terminal required","actual":"terminal actual","environment":"terminal environment"}}]}
            """;
        var reading = TraceReadingBuilder.Build(PanGlossTraceDiagnosticReader.Read(TraceEnvelope.Of("", tree)));
        var attempt = Assert.Single(reading.Attempts);

        Assert.Equal("PartialParse", attempt.FailureReason);
        Assert.Equal("terminal context", attempt.ContextualFailure);
        Assert.Equal("terminal required", attempt.FailureRequired);
        Assert.Equal("terminal actual", attempt.FailureActual);
        Assert.Equal("terminal environment", attempt.FailureEnvironment);
        Assert.Equal(attempt.FailureReason, attempt.FailureEvidence?.ReasonCode);
    }

    [Fact]
    public void AlternateGuidSpellingsShareOneRuleRef()
    {
        var reading = AlternateGuidReading();

        var rule = Assert.Single(reading.Refs, reference => reference.Kind != "morph" &&
            Guid.TryParse(reference.Identity, out var guid) && guid == Guid.Parse("12345678-1234-1234-abcd-123456789abc"));
        Assert.Equal("morphRule:12345678-1234-1234-abcd-123456789abc", rule.Id);
        Assert.Equal("12345678-1234-1234-abcd-123456789abc", rule.Identity);
    }

    [Fact]
    public void AlternateGuidSpellingsJoinAffixMorphsToTheirRuleTiming()
    {
        var reading = AlternateGuidReading(mixedSpellings: false);
        var morph = Assert.Single(reading.Analyses).Morphs[1];
        var reference = Assert.Single(reading.Refs, item => item.Id == morph.RefId);

        Assert.Equal(new TraceTimingKey("morph_rule", "12345678-1234-1234-abcd-123456789abc"), reference.TimingKey);
    }

    [Fact]
    public void AlternateGuidSpellingsShareOneMorphRef()
    {
        var morph = new TraceMorph(null, "form", null, null, null, null, null, null, null, null)
        {
            EntryId = "12345678-1234-1234-abcd-123456789abc",
            MsaId = "22345678-1234-1234-abcd-123456789abc",
            FormId = "32345678-1234-1234-abcd-123456789abc",
        };
        var alternate = morph with
        {
            EntryId = "{" + morph.EntryId.ToUpperInvariant() + "}",
            MsaId = morph.MsaId.ToUpperInvariant(),
            FormId = "{" + morph.FormId + "}",
        };

        Assert.Equal(morph.RefId, alternate.RefId);
        Assert.Equal("morphRule:local:Rule", TraceRefIds.ForSource("MorphologicalRuleAnalysis", null,
            "morphRule", "local:Rule"));
    }

    [Fact]
    public void GenericMorphologicalStepsDoNotClaimToBeAffixRules()
    {
        var reading = WordTraceQuery.LoadDiagnostic(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestFixtures", "trace-details-v2-kumata.json"))).Value!.Reading!;

        var compounds = reading.Refs.Where(reference => reference.Label.Contains("Compounding")).ToArray();
        Assert.Equal(2, compounds.Length);
        Assert.All(compounds, reference => Assert.Equal("morphologicalRule", reference.Kind));
        Assert.All(reading.RulesOnBestPath.Where(rule => rule.Kind.Contains("Affix rule")),
            rule => Assert.Fail("A generic morphological event cannot identify an affix rule."));
    }

    private static WordTraceReading AlternateGuidReading(bool mixedSpellings = true)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v2-kumata.json"));
        var json = JsonNode.Parse(text.Replace("00000000-0000-0000-0000-000000000109",
            "12345678-1234-1234-abcd-123456789abc", StringComparison.Ordinal))!;
        var rewritten = false;
        Rewrite(json["trace"]);
        return WordTraceQuery.LoadDiagnostic(json.ToJsonString()).Value!.Reading!;

        void Rewrite(JsonNode? node)
        {
            if (node is not JsonObject step) return;
            if ((!rewritten || !mixedSpellings) && step["sourceIdentity"] is JsonObject identity && identity["id"]?.GetValue<string>() ==
                "12345678-1234-1234-abcd-123456789abc")
            {
                identity["id"] = "{12345678-1234-1234-ABCD-123456789ABC}";
                rewritten = true;
            }
            if (step["children"] is JsonArray children)
                foreach (var child in children) Rewrite(child);
        }
    }

    [Fact]
    public void MatinluAttemptsKeepTheStemLookupAsRecordedTreeContext()
    {
        var response = WordTraceQuery.LoadDiagnostic(ReadFixture()).Value!;

        Assert.All(response.Reading.Attempts, candidate =>
            Assert.Contains(TraceTreeContextRange.Resolve(response.Reading.Root, candidate.TreeContext), step => step.Type == "LexicalLookup"));
    }

    [Fact]
    public void BestPathRulesReadInBuildingOrder()
    {
        var response = WordTraceQuery.LoadDiagnostic(ReadFixture()).Value!;

        Assert.Equal(["ma", "lu"], response.Reading!.RulesOnBestPath.Select(rule => rule.Rule));
    }

    [Fact]
    public void DuplicateMatinluAnalysesRemainSourceRecordsWithASeparateSummary()
    {
        var response = WordTraceQuery.LoadDiagnostic(ReadFixture()).Value!;

        Assert.Equal(["analysis-0", "analysis-1"], response.Reading.Analyses.Select(analysis => analysis.AnalysisId));
        Assert.Equal(2, response.Reading.LogicalAnalyses.Count);
        Assert.All(response.Reading.LogicalAnalyses, summary => Assert.Single(summary.SourcePositions));
    }

    [Fact]
    public void UnavailableFeaturesNeverBecomeRawJsonInAMorph()
    {
        var response = WordTraceQuery.LoadDiagnostic(ReadFixture()).Value!;
        var morphs = response.Reading.Attempts.SelectMany(candidate => candidate.RichMorphs).ToArray();

        Assert.NotEmpty(morphs);
        Assert.All(morphs.Where(morph => morph.FeaturesStatus == "unavailable"),
            morph => Assert.Null(morph.Features));
    }

    [Fact]
    public void EveryRecordedStepHasAChildIndexPath()
    {
        var response = WordTraceQuery.LoadDiagnostic(ReadFixture()).Value!;
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(response.Reading.Attempts));

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
            Assert.Contains(TraceTreeContextRange.Resolve(reading.Root, attempt.TreeContext), step => step.Type == "LexicalLookup" && step.Input == "kuma");
            Assert.Contains(TraceTreeContextRange.Resolve(reading.Root, attempt.TreeContext), step => step.Type == "TemplateSynthesisInput" && step.Source == "NounTemplate");
        });
        var stem = Assert.Single(reading.Analyses).Morphs[0];
        Assert.Equal("00000000-0000-0000-0000-000000000106", stem.FormSourceId);
        Assert.Equal("00000000-0000-0000-0000-000000000107", stem.GlossSourceId);
        Assert.Null(stem.Features);
    }

    [Fact]
    public void APhonologicalLeafSiblingRemainsTreeContextRatherThanAttemptLineage()
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

        Assert.Equal(["Harmony", "Assimilation"], TraceTreeContextRange.Resolve(reading.Root, reading.Attempts[0].TreeContext)
            .Where(step => step.Type == "PhonologicalRuleSynthesis").Select(step => step.Source));
        Assert.DoesNotContain(reading.Attempts[1].Steps, step => step.Type is "PhonologicalRuleSynthesis" or "LexicalLookup");
        Assert.DoesNotContain(reading.RulesOnBestPath, rule => rule.Rule == "Harmony");
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
            Assert.Equal(response.Reading.Attempts.SelectMany(candidate => candidate.Steps).Select(step => step.StepId),
                loaded.Reading.Attempts.SelectMany(candidate => candidate.Steps).Select(step => step.StepId));
            foreach (var step in loaded.Reading.Attempts.SelectMany(candidate => candidate.Steps))
            {
                var node = loaded.Reading.Root;
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
