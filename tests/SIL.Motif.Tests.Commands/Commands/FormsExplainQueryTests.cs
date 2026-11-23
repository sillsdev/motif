using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// What <c>forms explain</c> makes of PanGloss's generate answers: surfaces in a fixed order, a stop shown rather than
/// hidden, and a typed refusal for each way the parser cannot answer. The generator is a recording stand-in, so no
/// project or parser runs; the resident exchange is covered against the fake serve in the warm pool tests.
/// </summary>
public sealed class FormsExplainQueryTests
{
    internal const string RootGuid = "11111111-1111-1111-1111-111111111111";
    internal const string RootMsa = "22222222-2222-2222-2222-222222222222";
    internal const string AffixGuid = "33333333-3333-3333-3333-333333333333";
    internal const string AffixMsa = "44444444-4444-4444-4444-444444444444";

    private static readonly ResolvedMorpheme Root = new(RootGuid, RootMsa, "MoStemMsa", "kaon");
    private static readonly ResolvedMorpheme Affix = new(AffixGuid, AffixMsa, "MoInflAffMsa", "-an");

    internal static string Answer(bool complete, string? stop = null) => $$"""
        {"id":"1","type":"result","op":"generate","complete":{{(complete ? "true" : "false")}},"stopReason":{{(stop is null ? "null" : "\"" + stop + "\"")}},
         "seeds":[
          {"seedKey":"r10/p0","allomorphs":[{"allomorphId":10,"entryGuid":"{{RootGuid}}","msaGuid":"{{RootMsa}}","role":"root","shape":"kaon"}],
           "surfaces":[{"surface":"kaonan","derivations":1,"steps":[]}],"traceTruncated":false},
          {"seedKey":"r2/p0","allomorphs":[{"allomorphId":2,"entryGuid":"{{RootGuid}}","msaGuid":"{{RootMsa}}","role":"root","shape":"kaun"},
                                          {"allomorphId":7,"entryGuid":"{{AffixGuid}}","msaGuid":"{{AffixMsa}}","role":"suffix","shape":"an"}],
           "surfaces":[{"surface":"kaonan","derivations":2,"steps":[
              {"stratum":"phonology","rule":{"kind":"phonological","id":"R1","name":"Vowel lowering","guid":"55555555-5555-5555-5555-555555555555"},"subrule":1,"input":"kaun+an","output":"kaon+an"},
              {"stratum":"phonology","rule":{"kind":"phonological","name":"Boundary deletion"},"input":"kaon+an","output":"kaonan"}]},
              {"surface":"kaonon","derivations":1,"steps":[]}],"traceTruncated":false}]}
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
        new("kaon", null, new ExplicitContextPlanner(with).Plan(Root), []);

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
        Assert.Equal(["kaonan", "kaonan", "kaonon"], surfaces.Select(s => s.Surface));
        Assert.Equal(["r2/p0", "r10/p0", "r2/p0"], surfaces.Select(s => s.Seed));
        Assert.Equal(["Vowel lowering", "Boundary deletion"], surfaces[0].Steps.Select(s => s.Rule.Name));
        Assert.Equal(1, surfaces[0].Steps[0].Subrule);
        Assert.Equal(["kaun", "an"], surfaces[0].Allomorphs.Select(a => a.Shape));
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
        var plan = new FormsExplainQuery.ResolvedPlan(null, "kaonan",
            [new FormsContextPlan([Root]), new FormsContextPlan([other])], []);

        var response = FormsExplainQuery.Run(generator, plan, "g", "baseline", default).Value!;

        Assert.Single(response.Contexts);
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
}
