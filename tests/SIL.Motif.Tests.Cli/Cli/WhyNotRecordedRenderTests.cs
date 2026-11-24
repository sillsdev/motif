using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Queries;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class WhyNotRecordedRenderTests
{
    private static SIL.Motif.Host.PanGloss.WhyNotCandidate Cand(string spec)
    {
        Assert.True(WhyNotCandidateSpec.TryParse(spec, out var candidate, out var error), error);
        return candidate!;
    }

    private static string Render(string name)
    {
        using var stream = typeof(WhyNotRecordedRenderTests).Assembly.GetManifestResourceStream("WhyNotRecorded." + name + ".json")!;
        var report = new StreamReader(stream).ReadToEnd();
        Assert.True(WhyNotProjection.TryProject(report, "w", "baseline", [Cand("a")],
            ["no_morph_match", "environment_unmet", "adhoc_prohibition"], out var response, out var error), error);
        return WhyNotRenderer.Render(response!);
    }

    [Fact]
    public void RealReportsRenderTheTargetsAnAuthorEdits()
    {
        var anyOf = Render("env-anyof");
        Assert.Contains("None of the 2 environments of allomorph", anyOf, StringComparison.Ordinal);
        Assert.Contains("environment 1 /t_; FieldWorks: 00000000-0000-0000-0000-000000000030.", anyOf, StringComparison.Ordinal);

        var single = Render("env-single");
        Assert.Contains(" /[V]_ of allomorph ", single, StringComparison.Ordinal);
        Assert.Contains("FieldWorks: 00000000-0000-0000-0000-000000000017.", single, StringComparison.Ordinal);

        var adhoc = Render("adhoc");
        Assert.Contains("Object: rule (no grammar id); FieldWorks: 00000000-0000-0000-0000-0000000009a1.", adhoc, StringComparison.Ordinal);
        Assert.Contains("The prohibition names:", adhoc, StringComparison.Ordinal);
        Assert.Contains("form 00000000-0000-0000-0000-000000000201", adhoc, StringComparison.Ordinal);

        var noMatch = Render("nomorph");
        Assert.Contains("no_morph_match about the whole word (derived).", noMatch, StringComparison.Ordinal);

        Assert.Contains("Parses as ", Render("accepted"), StringComparison.Ordinal);
    }

    [Fact]
    public void ARealOtherGateRendersItsKindAndGate()
    {
        var text = Render("disjunctive-gate");
        Assert.Contains("other_gate at position 1 (derived).", text, StringComparison.Ordinal);
        Assert.Contains("Gate: disjunctive_allomorph.", text, StringComparison.Ordinal);
    }
}
