using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// What <c>forms explain</c> makes of PanGloss's generate answers: surfaces in a fixed order, a stop shown rather than
/// hidden, and a typed refusal for each way the parser cannot answer. The generator is a recording stand-in, so no
/// project or parser runs; the resident exchange is covered against the fake serve in the warm pool tests.
/// </summary>
public sealed class FormsExplainQueryTests
{
    private const string RootGuid = RecordedGenerate.RootGuid;
    private const string RootMsa = RecordedGenerate.RootMsa;
    private const string AffixGuid = RecordedGenerate.AffixGuid;
    private const string AffixMsa = RecordedGenerate.AffixMsa;

    private static readonly ResolvedMorpheme Root = new(RootGuid, RootMsa, "MoStemMsa", "ranna");
    private static readonly ResolvedMorpheme Affix = new(AffixGuid, AffixMsa, "MoInflAffMsa", "-s");

    // Two seeds out of visit order and a surface reached by both, with an affix in the real form: a rule, no shape.
    private static string Answer(bool complete, string? stop = null) => $$"""
        {"complete":{{(complete ? "true" : "false")}},"stopReason":{{(stop is null ? "null" : "\"" + stop + "\"")}},
         "seeds":[
          {"seedKey":"r10/p0","allomorphs":[{"allomorphId":10,"entryGuid":"{{RootGuid}}","msaGuid":"{{RootMsa}}","role":"root","ruleName":null,"shape":"ranna"}],
           "surfaces":[{"surface":"rannas","derivations":1,"steps":[],"stepsUnresolved":false,"alternatives":[]}],"traceTruncated":false},
          {"seedKey":"r2/p0","allomorphs":[{"allomorphId":2,"entryGuid":"{{RootGuid}}","msaGuid":"{{RootMsa}}","role":"root","ruleName":null,"shape":"rann"},
                                          {"allomorphId":null,"entryGuid":"{{AffixGuid}}","msaGuid":"{{AffixMsa}}","formGuid":null,"role":"rule","ruleName":"-s","shape":null}],
           "surfaces":[{"surface":"rannas","derivations":2,"stepsUnresolved":false,"alternatives":[],"steps":[
              {"stratum":"phonology","rule":{"kind":"phonological","id":"R1","name":"Vowel lowering","guid":"55555555-5555-5555-5555-555555555555"},"subrule":1,"input":"rann+s","output":"ranns"},
              {"stratum":"phonology","rule":{"kind":"phonological","name":"Boundary deletion"},"input":"ranns","output":"rannas"}]},
              {"surface":"rannos","derivations":1,"steps":[],"stepsUnresolved":true,"alternatives":[]}],"traceTruncated":false}]}
        """;

    private sealed class Generator(Func<ServeGenerateRequest, WarmGenerateResult> answer, string version = "0.28.0") : IFormsGenerator
    {
        public List<ServeGenerateRequest> Requests { get; } = [];

        public string ParserVersion { get; } = version;

        public Task<WarmGenerateResult> GenerateAsync(ServeGenerateRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static FormsExplainQuery.ResolvedPlan Plan(params ResolvedMorpheme[] with) =>
        new("ranna", null, new ExplicitContextPlanner(with).Plan(Root), []);

    [Fact]
    public void SurfacesComeBackInTextThenNumericSeedOrderWithTheirStepsAndAllomorphs()
    {
        var generator = new Generator(_ => new WarmGenerateResult.Completed(Answer(true)));

        var outcome = FormsExplainQuery.Run(generator, Plan(Affix), "sha256:ab", "baseline", default);

        var response = outcome.Value!;
        Assert.True(response.Complete);
        Assert.Null(response.StopReason);
        Assert.Equal("0.28.0", response.PanglossVersion);
        var surfaces = Assert.Single(response.Contexts).Surfaces;
        Assert.Equal(["rannas", "rannas", "rannos"], surfaces.Select(s => s.Surface));
        Assert.Equal(["r2/p0", "r10/p0", "r2/p0"], surfaces.Select(s => s.Seed));
        Assert.Equal(["Vowel lowering", "Boundary deletion"], surfaces[0].Steps.Select(s => s.Rule.Name));
        Assert.Equal(1, surfaces[0].Steps[0].Subrule);
        Assert.Equal(["rann", null], surfaces[0].Allomorphs.Select(a => a.Shape));
        Assert.Equal("-s", surfaces[0].Allomorphs[1].RuleName);
        Assert.Equal("rule", surfaces[0].Allomorphs[1].Role);
    }

    [Fact]
    public void UnresolvedStepsAndAlternativeDerivationsAreCarriedNotGuessed()
    {
        const string answer = """
            {"complete":true,"stopReason":null,"seeds":[{"seedKey":"r0/p0","allomorphs":[],"surfaces":[
              {"surface":"a","derivations":1,"steps":[],"stepsUnresolved":true,"alternatives":[]},
              {"surface":"b","derivations":2,"steps":[],"stepsUnresolved":false,"alternatives":[
                [{"stratum":"s","rule":{"kind":"k","name":"One"},"input":"x","output":"y"}],
                [{"stratum":"s","rule":{"kind":"k","name":"Two"},"input":"x","output":"y"}]]}]}]}
            """;
        var generator = new Generator(_ => new WarmGenerateResult.Completed(answer));

        var surfaces = FormsExplainQuery.Run(generator, Plan(), "g", "baseline", default).Value!.Contexts.Single().Surfaces;

        Assert.True(surfaces[0].StepsUnresolved);
        Assert.Empty(surfaces[0].Steps);
        Assert.False(surfaces[1].StepsUnresolved);
        Assert.Equal(["One", "Two"], surfaces[1].Alternatives.Select(steps => steps.Single().Rule.Name));
    }

    [Fact]
    public void TheRequestLineNamesTheWordDeadlineWordTimeoutMs()
    {
        var line = new ServeGenerateRequest(new GenerateMorpheme(RootGuid, RootMsa, "MoStemMsa"), [], WordTimeoutMs: 500).ToLine("1");

        Assert.Contains("\"wordTimeoutMs\":500", line);
        Assert.DoesNotContain("\"timeoutMs\"", line);
    }

    [Fact]
    public void TheRootAndWithMorphemesAreSentInOrderAsOneRequest()
    {
        var generator = new Generator(_ => new WarmGenerateResult.Completed(Answer(true)));

        FormsExplainQuery.Run(generator, Plan(Affix), "g", "baseline", default);

        var request = Assert.Single(generator.Requests);
        Assert.Equal(RootGuid, request.Root.EntryGuid);
        Assert.Equal(AffixMsa, Assert.Single(request.Morphemes).MsaGuid);
        Assert.True(request.Trace);
    }

    [Fact]
    public void AnIncompleteAnswerKeepsItsStopReasonAtEveryLevel()
    {
        var generator = new Generator(_ => new WarmGenerateResult.Completed(Answer(false, "work_limit")));

        var response = FormsExplainQuery.Run(generator, Plan(), "g", "baseline", default).Value!;

        Assert.False(response.Complete);
        Assert.Equal("work_limit", response.StopReason);
        Assert.Equal("work_limit", response.Contexts.Single().StopReason);
        Assert.Equal("work_limit", System.Text.Json.JsonDocument.Parse(ProjectionJson.Serialize(response))
            .RootElement.GetProperty("stopReason").GetString());
    }

    [Fact]
    public void ACompleteAnswerStillWritesAnExplicitNullStopReason()
    {
        var generator = new Generator(_ => new WarmGenerateResult.Completed(Answer(true)));

        var json = ProjectionJson.Serialize(FormsExplainQuery.Run(generator, Plan(), "g", "baseline", default).Value!);

        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("stopReason").ValueKind);
    }

    [Fact]
    public void AParserWithoutServeGenerateRefusesNamingTheVersionItHasAndWhatIsNeeded()
    {
        var generator = new Generator(_ => new WarmGenerateResult.Unsupported("its description declares no interfaces.serveOps.generate"), "0.27.0");

        var refusal = FormsExplainQuery.Run(generator, Plan(), "g", "baseline", default).Refusal!;

        Assert.Equal(RefusalCodes.FormsExplainParserUnsupported, refusal.Code);
        Assert.Contains("0.27.0", refusal.Message);
        Assert.Contains("interfaces.serveOps.generate", refusal.Message);
        Assert.Equal("0.27.0", refusal.Facts!["parserVersion"]);
    }

    [Fact]
    public void ARequestPanGlossRejectsIsAParserRefusalWithItsCode()
    {
        var generator = new Generator(_ => new WarmGenerateResult.Rejected("unknown_entry", "no such entry"));

        var refusal = FormsExplainQuery.Run(generator, Plan(), "g", "baseline", default).Refusal!;

        Assert.Equal(RefusalCodes.FormsExplainParserRefused, refusal.Code);
        Assert.Equal("unknown_entry", refusal.Facts!["parserCode"]);
    }

    [Fact]
    public void AnUnreadableAnswerIsMalformedNotAnEmptyResult()
    {
        var generator = new Generator(_ => new WarmGenerateResult.Completed("{\"complete\":true}"));

        Assert.Equal(RefusalCodes.FormsExplainMalformed,
            FormsExplainQuery.Run(generator, Plan(), "g", "baseline", default).Refusal!.Code);
    }

    [Fact]
    public void OneRejectedContextIsListedAndTheOthersStillAnswer()
    {
        var generator = new Generator(request => request.Root.EntryGuid == RootGuid
            ? new WarmGenerateResult.Completed(Answer(true))
            : new WarmGenerateResult.Rejected("unknown_msa", "gone"));
        var other = new ResolvedMorpheme("66666666-6666-6666-6666-666666666666", RootMsa, "MoStemMsa", "tama");
        var plan = new FormsExplainQuery.ResolvedPlan(null, "rannas",
            [new FormsContextPlan([Root]), new FormsContextPlan([other])], []);

        var response = FormsExplainQuery.Run(generator, plan, "g", "baseline", default).Value!;

        Assert.Single(response.Contexts);
        Assert.False(response.Complete);
        Assert.Equal("context_rejected", response.StopReason);
        Assert.Contains("unknown_msa", Assert.Single(response.Skipped!).Reason);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("kaon", "kaonan")]
    public void ExactlyOneOfMorphemeAndWordIsRequired(string? morpheme, string? word)
    {
        var refusal = FormsExplainQuery.Explain(new FormsExplainRequest("p", morpheme, [], word), "pangloss").Refusal!;

        Assert.Equal(RefusalCodes.FormsExplainRequestInvalid, refusal.Code);
    }

    [Fact]
    public void WithAndWordCannotBeCombined()
    {
        var refusal = FormsExplainQuery.Explain(new FormsExplainRequest("p", null, ["-an"], "kaonan"), "pangloss").Refusal!;

        Assert.Equal(RefusalCodes.FormsExplainRequestInvalid, refusal.Code);
    }

    [Fact]
    public void ABadAtIsRefusedBeforeAnyWork()
    {
        var refusal = FormsExplainQuery.Explain(new FormsExplainRequest("p", "kaon", [], null, "nonsense"), "pangloss").Refusal!;

        Assert.Equal(RefusalCodes.FormsExplainRequestInvalid, refusal.Code);
    }

    [Fact]
    public void TheExplicitPlannerMakesOneContextOfTheRootThenTheWithMorphemes()
    {
        var context = Assert.Single(new ExplicitContextPlanner([Affix]).Plan(Root));

        Assert.Equal([Root, Affix], context.Morphemes);
    }

    [Fact]
    public void ARecordedAffixContextProjectsTheAffixByItsRuleName()
    {
        var generator = new Generator(_ => new WarmGenerateResult.Completed(RecordedGenerate.RootAffix));

        var response = FormsExplainQuery.Run(generator, Plan(Affix), "g", "baseline", default).Value!;

        var surface = Assert.Single(Assert.Single(response.Contexts).Surfaces);
        Assert.Equal("rannas", surface.Surface);
        Assert.Equal(("root", "ranna", null), (surface.Allomorphs[0].Role, surface.Allomorphs[0].Shape, surface.Allomorphs[0].RuleName));
        Assert.Equal(("rule", null, "-s"), (surface.Allomorphs[1].Role, surface.Allomorphs[1].Shape, surface.Allomorphs[1].RuleName));
        Assert.Null(surface.Allomorphs[1].AllomorphId);
        Assert.Equal("-s", Assert.Single(surface.Steps).Rule.Name);
        Assert.True(response.Complete);
    }

    [Fact]
    public void ARecordedRootAloneIsCompleteWithNoSurface()
    {
        var generator = new Generator(_ => new WarmGenerateResult.Completed(RecordedGenerate.RootAlone));

        var response = FormsExplainQuery.Run(generator, Plan(), "g", "baseline", default).Value!;

        Assert.True(response.Complete);
        Assert.Empty(Assert.Single(response.Contexts).Surfaces);
    }

    [Theory]
    [InlineData("Deadline", "deadline")]
    [InlineData("WorkLimit", "work_limit")]
    public void ARecordedStopIsAnIncompleteResultNotARefusal(string recording, string stop)
    {
        var json = recording == "Deadline" ? RecordedGenerate.Deadline : RecordedGenerate.WorkLimit;
        var generator = new Generator(_ => new WarmGenerateResult.Completed(json));

        var outcome = FormsExplainQuery.Run(generator, Plan(Affix), "g", "baseline", default);

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.Value!.Complete);
        Assert.Equal(stop, outcome.Value.StopReason);
    }

    [Fact]
    public void EveryRequestCarriesTheDeadlineAndOnlyTheWorkCapTheCallerGave()
    {
        var generator = new Generator(_ => new WarmGenerateResult.Completed(RecordedGenerate.RootAlone));

        FormsExplainQuery.Run(generator, Plan(), "g", "baseline", default);
        FormsExplainQuery.Run(generator, Plan(), "g", "baseline", default, 1500, 99);

        Assert.Equal((30000L, (long?)null), (generator.Requests[0].WordTimeoutMs!.Value, generator.Requests[0].MaxWork));
        Assert.Equal((1500L, (long?)99), (generator.Requests[1].WordTimeoutMs!.Value, generator.Requests[1].MaxWork));
        Assert.Contains("\"wordTimeoutMs\":30000", generator.Requests[0].ToLine("1"));
        Assert.DoesNotContain("maxWork", generator.Requests[0].ToLine("1"));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(240001, null)]
    [InlineData(null, 0L)]
    public void ADeadlineOrWorkCapOutOfRangeIsARequestRefusal(int? timeout, long? work)
    {
        var refusal = FormsExplainQuery.Explain(new FormsExplainRequest("p", "ranna", [], null, null, timeout, work), "pangloss").Refusal!;

        Assert.Equal(RefusalCodes.FormsExplainRequestInvalid, refusal.Code);
    }
}
