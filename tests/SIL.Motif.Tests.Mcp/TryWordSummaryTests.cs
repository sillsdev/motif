using System.Text;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

/// <summary>The concise Try a Word answer is bounded by the word, not by the size of the project it was tried in.</summary>
[Trait("MotifTestLevel", "Unit")]
public sealed class TryWordSummaryTests
{
    [Fact]
    public void ConciseIsBoundedWhateverTheProjectHoldsAndKeepsTheReadings()
    {
        var small = Response(parsed: true, writingSystems: 2, attempts: 3);
        var large = Response(parsed: true, writingSystems: 400, attempts: 3000);

        var smallText = TryWordSummary.Concise(small).ToJsonString();
        var largeText = TryWordSummary.Concise(large).ToJsonString();

        Assert.Equal(smallText.Length, largeText.Length);
        Assert.True(largeText.Length < 4_000, $"Concise answer was {largeText.Length} characters.");
        Assert.DoesNotContain("StyleFonts", largeText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("writingSystems", largeText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"parsed\":true", largeText);
        Assert.Contains("sag-ed", largeText);
        Assert.Contains("detail=detailed", largeText);
    }

    [Fact]
    public void ANonWordKeepsWhyItFailedAndTheStoppingRules()
    {
        var response = Response(parsed: false, writingSystems: 300, attempts: 2000) with
        {
            Reading = Response(parsed: false, 0, 2000).Reading with
            {
                NoParseReasons = ["No lexical root matched xyz."],
                Analyses = [],
            },
        };

        var text = TryWordSummary.Concise(response).ToJsonString();

        Assert.Contains("\"parsed\":false", text);
        Assert.Contains("No lexical root matched xyz.", text);
        Assert.Contains("ed_suffix", text);
        Assert.Contains("NonPartialRuleProhibitedAfterFinalTemplate", text);
        Assert.True(Encoding.UTF8.GetByteCount(text) < 4_000);
    }

    [Fact]
    public void ANamedRuleShowsItsFieldWorksNameBeforeTheParsersId()
    {
        var response = Response(parsed: false, 0, 1);
        var group = response.Reading.StopGroups[0];
        var reference = new TraceRef("rule-ref", "morphRule", "mrule#2") { CapturedFieldWorksLabel = "mu- ‘ACT.PUNC.FUT’" };
        var named = response with
        {
            DeepestRule = "mrule#2",
            Reading = response.Reading with
            {
                Refs = [reference],
                StopGroups = [group with { Rule = "mrule#2", RuleRefId = "rule-ref" }],
            },
        };

        var text = TryWordSummary.Concise(named).ToJsonString(new System.Text.Json.JsonSerializerOptions
            { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        Assert.Contains("\"rule\":\"mu- ‘ACT.PUNC.FUT’ (mrule#2)\"", text);
        Assert.Contains("\"deepestRule\":\"mu- ‘ACT.PUNC.FUT’ (mrule#2)\"", text);
    }

    [Fact]
    public void AWordTracedAgainstAProposalKeepsTheParsersIdsAndNeverReadsTheBaseline()
    {
        var failed = Response(parsed: false, 0, 0);
        var ruleGuid = "12345678-1234-1234-abcd-123456789abc";
        var response = failed with
        {
            Reading = failed.Reading with
            {
                Refs = [new TraceRef("rule", "morphRule", "mrule#2") { Identity = ruleGuid, IdentityQuality = "authored" }],
            },
        };

        // The path names no project: a Baseline read would refuse it, so only an early return can pass.
        var result = TryWordRuleNames.Named("/no/such/project.fwdata", response, "my-proposal");

        Assert.Same(response, result);
    }

    [Fact]
    public void ManyReadingsAreCountedAndTruncatedWithANote()
    {
        var analyses = Enumerable.Range(0, 20).Select(i => new TraceAnalysis("a" + i, i, "w", "available",
            [new TraceMorph(null, "m" + i, null, "g", "n", null, null, null, null, null)]) { Signature = "m" + i }).ToArray();
        var response = Response(parsed: true, 0, 0) with
        {
            Reading = Response(true, 0, 0).Reading with { Analyses = analyses },
        };

        var node = TryWordSummary.Concise(response);

        Assert.Equal(20, node["readingCount"]!.GetValue<int>());
        Assert.Equal(TryWordSummary.MaxReadings, node["readings"]!.AsArray().Count);
        Assert.Contains("detail=detailed", node["readingsNote"]!.GetValue<string>());
    }

    private static WordTraceResponse Response(bool parsed, int writingSystems, int attempts)
    {
        var step = new TraceStep("Failed", null, null, null, "PartialParse", []);
        var candidates = Enumerable.Range(0, attempts).Select(i => new TraceCandidate([], false, "PartialParse",
            "expl " + i, [step])).ToArray();
        var morph = new TraceMorph(null, "sag", null, "walk", "v", null, null, null, null, null);
        var reading = new WordTraceReading("sagd", step, candidates,
            parsed ? [new TraceAnalysis("a0", 0, "sagd", "available", [morph, morph with { Form = "-ed" }])
                { Signature = "sag-ed" }] : [],
            [new TraceStopGroup("ed_suffix", null, "NonPartialRuleProhibitedAfterFinalTemplate", "not allowed", candidates)],
            candidates, []);
        var systems = Enumerable.Range(0, writingSystems).Select(i => new TraceWritingSystem("ws" + i, "Name " + i,
            true, false, "ltr", "Font")
        {
            StyleFonts = Enumerable.Range(0, 40).ToDictionary(n => "style" + n,
                n => new WritingSystemStyleFont("Font", "")),
        }).ToArray();
        return new WordTraceResponse("sagd", parsed, true, null, attempts, "ed_suffix", 5, reading)
        {
            HostCapture = new TraceHostCapture("id", null, null, null, null, null, systems) { LexiconEntryCount = 22000 },
            DiagnosticJson = new string('x', 200_000),
        };
    }
}
