using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// The JSON and Markdown of <c>forms explain</c> are pinned against a synthetic generate answer shaped like PanGloss's
/// serve result (no grammar data), so a change to either prints a diff to review.
/// </summary>
public sealed class FormsExplainGoldenTests
{
    private const string Root = "11111111-1111-1111-1111-111111111111";
    private const string RootMsa = "22222222-2222-2222-2222-222222222222";
    private const string Affix = "33333333-3333-3333-3333-333333333333";
    private const string AffixMsa = "44444444-4444-4444-4444-444444444444";

    private static string Answer(bool complete) => $$"""
        {"id":"1","type":"result","op":"generate","complete":{{(complete ? "true" : "false")}},"stopReason":{{(complete ? "null" : "\"trace_limit\"")}},
         "seeds":[
          {"seedKey":"r2/p0","allomorphs":[{"allomorphId":2,"entryGuid":"{{Root}}","msaGuid":"{{RootMsa}}","role":"root","shape":"kaun"},
                                          {"allomorphId":7,"entryGuid":"{{Affix}}","msaGuid":"{{AffixMsa}}","role":"suffix","shape":"an"}],
           "surfaces":[{"surface":"kaonan","derivations":1,"steps":[
              {"stratum":"phonology","rule":{"kind":"phonological","id":"R1","name":"Vowel lowering","guid":"55555555-5555-5555-5555-555555555555"},"subrule":1,"input":"kaun+an","output":"kaon+an"},
              {"stratum":"phonology","rule":{"kind":"phonological","name":"Boundary | deletion"},"input":"kaon+an","output":"kaonan"}]},
              {"surface":"kaonon","derivations":2,"steps":[],"stepsUnresolved":true,"alternatives":[]},
              {"surface":"kaonun","derivations":2,"steps":[],"stepsUnresolved":false,"alternatives":[
                [{"stratum":"phonology","rule":{"kind":"phonological","name":"Raising"},"input":"kaun+an","output":"kaonun"}],
                [{"stratum":"phonology","rule":{"kind":"phonological","name":"Fusion"},"input":"kaun+an","output":"kaonun"}]]}],"traceTruncated":{{(complete ? "false" : "true")}}}]}
        """;

    private sealed class Fixed(string json) : IFormsGenerator
    {
        public string ParserVersion => "0.28.0";

        public Task<WarmGenerateResult> GenerateAsync(ServeGenerateRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<WarmGenerateResult>(new WarmGenerateResult.Completed(json));
    }

    private static FormsExplainResponse Explain(bool complete)
    {
        ResolvedMorpheme root = new(Root, RootMsa, "MoStemMsa", "kaon");
        ResolvedMorpheme affix = new(Affix, AffixMsa, "MoInflAffMsa", "-an");
        var plan = new FormsExplainQuery.ResolvedPlan("kaon", null, new ExplicitContextPlanner([affix]).Plan(root), []);
        return FormsExplainQuery.Run(new Fixed(Answer(complete)), plan, "sha256:0123", "baseline", default).Value!;
    }

    private static void Matches(string golden, string actual)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var path = Path.Combine(repositoryRoot, "tests", "SIL.Motif.Tests.Cli", "Golden", golden);
        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(path, actual);
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), actual);
    }

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
    public void TheCompleteJsonIsPinned()
    {
        var lines = new List<string>();
        using var document = System.Text.Json.JsonDocument.Parse(ProjectionJson.Serialize(Explain(true)));
        Flatten(document.RootElement, "$", lines);
        Matches("forms-explain.json-leaves.txt", string.Join("\n", lines) + "\n");
    }

    [Fact]
    public void TheCompleteMarkdownIsPinned() =>
        Matches("forms-explain.md", FormsExplainMarkdown.Render(Explain(true)).Replace("\r\n", "\n"));

    [Fact]
    public void TheIncompleteMarkdownIsPinnedAndCarriesTheBanner()
    {
        var text = FormsExplainMarkdown.Render(Explain(false)).Replace("\r\n", "\n");

        Assert.Contains("incomplete: trace_limit", text, StringComparison.Ordinal);
        Matches("forms-explain.incomplete.md", text);
    }

    [Fact]
    public void ACompleteMarkdownHasNoIncompleteBanner() =>
        Assert.DoesNotContain("incomplete:", FormsExplainMarkdown.Render(Explain(true)), StringComparison.Ordinal);
}
