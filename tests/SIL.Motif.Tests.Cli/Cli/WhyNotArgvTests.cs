using System.Text.Json;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary><c>why-not</c> through the released executable and its text rendering.</summary>
public sealed class WhyNotArgvTests : IDisposable
{
    private readonly string _workerRoot = Path.Combine(Path.GetTempPath(), "motif-why-not-worker-" + Guid.NewGuid().ToString("N"));

    public WhyNotArgvTests() => Directory.CreateDirectory(_workerRoot);

    public void Dispose()
    {
        try { Directory.Delete(_workerRoot, recursive: true); } catch (IOException) { }
    }

    private Task<CliProcessResult> Run(params string[] arguments) =>
        CliProcess.RunAsync(_workerRoot, null, false, ["why-not", .. arguments]);

    [Theory]
    [InlineData("--project", "p.fwdata")]
    [InlineData("kibak", "--expect", "kib,ak")]
    [InlineData("kibak", "--project", "p.fwdata")]
    [InlineData("kibak", "--project", "p.fwdata", "--expect", "kib,ak", "--bogus", "x")]
    public async Task AMissingWordProjectOrCandidateAndAnUnknownFlagPrintTheUsage(params string[] arguments)
    {
        var result = await Run(arguments);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("why-not <word> --project <fwdata> --expect <morphs>", result.Error + result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACandidateMotifCannotSendIsRefusedWithItsCodeBeforeAnyProjectOpens()
    {
        var result = await Run("kibak", "--project", "never-opened.fwdata", "--expect", "kib,,ak", "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("why-not.request-invalid", result.Error + result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTextNamesTheAnswerableKindsEachResultAndTheFieldWorksTarget()
    {
        var response = new WhyNotResponse("kibakek", "baseline", "pangloss.whynot.v1", ["no_morph_match", "environment_unmet"],
        [
            new(0, "kib,ak,ek", "rejected", null,
                new WhyNotRejection("no_morph_match", "No morph matches at position 1.", 1, "derived",
                    [new WhyNotMorph("ak", null)], 0, new WhyNotTarget("affix", "ak", null, "no FieldWorks target yet"), null, null,
                    [], 0, null), null, null),
            new(1, "kib,ak", "accepted", new WhyNotAcceptance([new WhyNotMorph("kib", "kib-1"), new WhyNotMorph("ak", "ak-1")], 0, "posN"),
                null, null, null),
            new(2, "kib,ik", "unknown", null, null, "work_cap", "The search reached its work cap."),
        ], 1, new WhyNotBound(3, 0, 0));

        var text = WhyNotRenderer.Render(response);

        Assert.Contains("can name these failure kinds: no_morph_match, environment_unmet", text, StringComparison.Ordinal);
        Assert.Contains("1. kib,ak,ek: rejected", text, StringComparison.Ordinal);
        Assert.Contains("no_morph_match at position 1 (derived).", text, StringComparison.Ordinal);
        Assert.Contains("Object: affix ak; FieldWorks: no FieldWorks target yet.", text, StringComparison.Ordinal);
        Assert.Contains("2. kib,ak: accepted (best)", text, StringComparison.Ordinal);
        Assert.Contains("Parses as kib (kib-1) + ak (ak-1), category posN.", text, StringComparison.Ordinal);
        Assert.Contains("Could not tell (work_cap). The search reached its work cap.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Left out", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTextShowsEnvironmentTextsAndProhibitionMembersAsTheObjectToEdit()
    {
        const string P = "00000000-0000-0000-0000-";
        WhyNotRejection Reject(string kind, WhyNotTarget target, WhyNotEnvironment? environment, IReadOnlyList<WhyNotMorph>? members) =>
            new(kind, "plain.", null, "hc_reported", [new WhyNotMorph("sal", "aSal", P + "100", P + "102", P + "111")], 0, target,
                environment, null, [], 0, null, members);
        var response = new WhyNotResponse("w", "baseline", "pangloss.whynot.v1", ["environment_unmet", "adhoc_prohibition"],
        [
            new(0, "sal", "rejected", null, Reject("environment_unmet", new WhyNotTarget("allomorph", "aSal", P + "111", P + "111"),
                new WhyNotEnvironment("aSal", 0, null, null, P + "017", P + "017", "/[V]_"), null), null, null),
            new(1, "sal", "rejected", null, Reject("environment_unmet", new WhyNotTarget("allomorph", "aSal", P + "101", P + "101"),
                new WhyNotEnvironment("aSal", null, null, null, null, "no FieldWorks target yet", null,
                    [new WhyNotEnvironmentChoice(0, P + "017", "/[V]_"), new WhyNotEnvironmentChoice(1, null, "/t_")]), null), null, null),
            new(2, "tal,sal", "rejected", null, Reject("adhoc_prohibition", new WhyNotTarget("rule", null, null, "no FieldWorks target yet"), null,
                [new WhyNotMorph("tal", "aTal", null, null, P + "201"), new WhyNotMorph("sal", "aSal", P + "100", P + "102", P + "101")]), null, null),
        ], null, new WhyNotBound(3, 0, 0));

        var text = WhyNotRenderer.Render(response);

        Assert.Contains("Environment 0 /[V]_ of allomorph aSal; FieldWorks: " + P + "017.", text, StringComparison.Ordinal);
        Assert.Contains("FieldWorks sal: entry " + P + "100, analysis " + P + "102, form " + P + "111.", text, StringComparison.Ordinal);
        Assert.Contains("None of the 2 environments of allomorph aSal is met:", text, StringComparison.Ordinal);
        Assert.Contains("environment 0 /[V]_; FieldWorks: " + P + "017.", text, StringComparison.Ordinal);
        Assert.Contains("environment 1 /t_; FieldWorks: no FieldWorks target yet.", text, StringComparison.Ordinal);
        Assert.Contains("Object: rule (no grammar id); FieldWorks: no FieldWorks target yet (the parser could not tell which rule fired).", text,
            StringComparison.Ordinal);
        Assert.Contains("The prohibition names:", text, StringComparison.Ordinal);
        Assert.Contains("tal (aTal); FieldWorks: form " + P + "201.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheJsonResponseIsCamelCaseWithTheDeclaredKinds()
    {
        var response = new WhyNotResponse("w", "baseline", "pangloss.whynot.v1", ["no_morph_match"], [], null, new WhyNotBound(0, 0, 0));

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"answerableKinds\":[\"no_morph_match\"]", json, StringComparison.Ordinal);
    }
}
