using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// The JSON and Markdown of <c>forms explain</c> are pinned against replies recorded from a real <c>pangloss serve</c>
/// <c>generate</c> on a public conformance fixture, so a change to either prints a diff to review. Only the surface
/// with several derivations is synthetic: the recorded fixture never reaches one.
/// </summary>
public sealed class FormsExplainGoldenTests
{
    private static readonly ResolvedMorpheme Root = new(RecordedGenerate.RootGuid, RecordedGenerate.RootMsa, "MoStemMsa", "ranna");
    private static readonly ResolvedMorpheme Affix = new(RecordedGenerate.AffixGuid, RecordedGenerate.AffixMsa, "MoInflAffMsa", "-s");

    private const string Derivations = """
        {"complete":true,"stopReason":null,"seeds":[{"seedKey":"r0/p0","allomorphs":[
          {"allomorphId":1,"entryGuid":"00000000-0000-0000-0000-000000000040","formGuid":"00000000-0000-0000-0000-000000000041","msaGuid":"00000000-0000-0000-0000-000000000042","role":"root","ruleName":null,"shape":"ranna"},
          {"allomorphId":null,"entryGuid":"00000000-0000-0000-0000-000000000050","formGuid":null,"msaGuid":"00000000-0000-0000-0000-000000000052","role":"rule","ruleName":"-s","shape":null}],
         "surfaces":[
          {"surface":"rannas","derivations":2,"steps":[],"stepsUnresolved":true,"alternatives":[]},
          {"surface":"ranns","derivations":2,"steps":[],"stepsUnresolved":false,"alternatives":[
            [{"stratum":"Phonology","rule":{"kind":"phonological","name":"Raising"},"input":"ranna+s","output":"ranns"}],
            [{"stratum":"Phonology","rule":{"kind":"phonological","name":"Fusion"},"input":"ranna+s","output":"ranns"}]]}],
         "traceTruncated":false}]}
        """;

    private sealed class Replies(Func<ServeGenerateRequest, WarmGenerateResult> answer) : IFormsGenerator
    {
        public string ParserVersion => "0.28.0";

        public Task<WarmGenerateResult> GenerateAsync(ServeGenerateRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    private static WarmGenerateResult Completed(string json) => new WarmGenerateResult.Completed(json);

    private static FormsExplainResponse Run(FormsExplainQuery.ResolvedPlan plan, Func<ServeGenerateRequest, WarmGenerateResult> answer) =>
        Succeeded(FormsExplainQuery.Run(new Replies(answer), plan, "sha256:0123", "baseline", default));

    private static FormsExplainResponse Succeeded(SIL.Motif.Contract.Commands.CommandOutcome<FormsExplainResponse> outcome)
    {
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!;
    }

    private static FormsExplainQuery.ResolvedPlan Explicit() =>
        new("ranna", null, new ExplicitContextPlanner([Affix]).Plan(Root), []);

    private static FormsExplainQuery.ResolvedPlan Word() =>
        new(null, "rannas", [new FormsContextPlan([Root]), new FormsContextPlan([Root, Affix])], []);

    private static FormsExplainResponse Recorded() =>
        Run(Explicit(), _ => Completed(RecordedGenerate.RootAffix));

    private static void Matches(string golden, string actual)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var path = Path.Combine(repositoryRoot, "tests", "SIL.Motif.Tests.Cli", "Golden", golden);
        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(path, actual);
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), actual);
    }

    private static string Markdown(FormsExplainResponse response) =>
        FormsExplainMarkdown.Render(response).Replace("\r\n", "\n");

    // One "path = value" line per JSON leaf, so the guard reads the golden as text.
    private static void Flatten(System.Text.Json.JsonElement element, string path, List<string> lines)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) Flatten(property.Value, path + "." + property.Name, lines);
                break;
            case System.Text.Json.JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray()) Flatten(item, path + "[" + index++ + "]", lines);
                if (index == 0) lines.Add(path + " = []");
                break;
            default:
                lines.Add(path + " = " + element.GetRawText());
                break;
        }
    }

    [Fact]
    public void TheRecordedRootAndAffixJsonIsPinned()
    {
        var lines = new List<string>();
        using var document = System.Text.Json.JsonDocument.Parse(ProjectionJson.Serialize(Recorded()));
        Flatten(document.RootElement, "$", lines);
        Matches("forms-explain.json-leaves.txt", string.Join("\n", lines) + "\n");
    }

    [Fact]
    public void TheRecordedRootAndAffixMarkdownIsPinned() =>
        Matches("forms-explain.md", Markdown(Recorded()));

    [Fact]
    public void ARecordedWordExplainsEachAnalysisFromItsOwnMorphemes() =>
        Matches("forms-explain.word.md", Markdown(Run(Word(), request =>
            Completed(request.Morphemes.Count == 0 ? RecordedGenerate.RootAlone : RecordedGenerate.RootAffix))));

    [Fact]
    public void ARecordedDeadlineStopIsPinnedWithTheBanner()
    {
        var text = Markdown(Run(Explicit(), _ => Completed(RecordedGenerate.Deadline)));

        Assert.Contains("incomplete: deadline", text, StringComparison.Ordinal);
        Matches("forms-explain.incomplete.md", text);
    }

    [Fact]
    public void ARecordedWorkLimitStopIsPinnedWithTheBanner()
    {
        var text = Markdown(Run(Explicit(), _ => Completed(RecordedGenerate.WorkLimit)));

        Assert.Contains("incomplete: work_limit", text, StringComparison.Ordinal);
        Matches("forms-explain.work-limit.md", text);
    }

    [Fact]
    public void ARejectedContextMakesTheWordIncompleteAndIsListed()
    {
        var text = Markdown(Run(Word(), request => request.Morphemes.Count == 0
            ? new WarmGenerateResult.Rejected("unknown_msa", "msa not owned by entry")
            : Completed(RecordedGenerate.RootAffix)));

        Assert.Contains("incomplete: context_rejected", text, StringComparison.Ordinal);
        Matches("forms-explain.rejected.md", text);
    }

    [Fact]
    public void UnresolvedStepsAndAlternativeDerivationsAreShownNotGuessed()
    {
        var text = Markdown(Run(Explicit(), _ => Completed(Derivations)));

        Assert.Contains("rule steps unresolved", text, StringComparison.Ordinal);
        Assert.Contains("Derivation 2 of 2", text, StringComparison.Ordinal);
        Matches("forms-explain.derivations.md", text);
    }

    [Fact]
    public void ACompleteMarkdownHasNoIncompleteBanner() =>
        Assert.DoesNotContain("incomplete:", Markdown(Recorded()), StringComparison.Ordinal);

    // PanGloss 0.29.0 on an unnamed template-slot affix: the allomorph and the morph step both carry a null name.
    private static readonly string Unnamed = RecordedGenerate.RootAffix
        .Replace("\"role\":\"rule\",\"ruleName\":\"-s\"", "\"role\":\"rule\",\"ruleName\":null", StringComparison.Ordinal)
        .Replace("\"name\":\"-s\"", "\"name\":null", StringComparison.Ordinal);

    [Fact]
    public void AnUnnamedAffixIsLabelledByItsMorphemeAndNeverRefused()
    {
        var response = Run(Explicit(), _ => Completed(Unnamed));
        var surface = response.Contexts[0].Surfaces[0];

        Assert.Equal(("-s", "morpheme"), (surface.Allomorphs[1].Label, surface.Allomorphs[1].LabelSource));
        Assert.Equal(("-s", "morpheme"), (surface.Steps[0].Rule.Label, surface.Steps[0].Rule.LabelSource));
        Assert.Null(surface.Allomorphs[0].LabelSource);
        var json = ProjectionJson.Serialize(response);
        Assert.Contains("\"labelSource\": \"morpheme\"", json.Replace("\":\"", "\": \""), StringComparison.Ordinal);
        Assert.Contains("| rannas | ranna, -s | -s |", Markdown(response), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnnamedAffixWithNoMorphemeFormFallsBackToAShortIdThenToUnnamed()
    {
        var formless = new ResolvedMorpheme(RecordedGenerate.AffixGuid, RecordedGenerate.AffixMsa, "MoInflAffMsa", null);
        var plan = new FormsExplainQuery.ResolvedPlan("ranna", null, new ExplicitContextPlanner([formless]).Plan(Root), []);
        var response = Run(plan, _ => Completed(Unnamed));
        var surface = response.Contexts[0].Surfaces[0];

        Assert.Equal(("00000000", "id"), (surface.Allomorphs[1].Label, surface.Allomorphs[1].LabelSource));
        Assert.Contains("| rannas | ranna, 00000000 | 00000000 |", Markdown(response), StringComparison.Ordinal);

        var bare = Unnamed.Replace("\"msaGuid\":\"00000000-0000-0000-0000-000000000052\",\"role\"", "\"role\"", StringComparison.Ordinal);
        var none = Run(new("ranna", null, [new FormsContextPlan([Root])], []), _ => Completed(bare)).Contexts[0].Surfaces[0];
        Assert.Equal(("unnamed affix", "none"), (none.Allomorphs[1].Label, none.Allomorphs[1].LabelSource));
    }

    [Fact]
    public void ANamedAffixIsLabelledByItsRuleName()
    {
        var allomorph = Recorded().Contexts[0].Surfaces[0].Allomorphs[1];
        Assert.Equal(("-s", "ruleName"), (allomorph.Label, allomorph.LabelSource));
    }
}
