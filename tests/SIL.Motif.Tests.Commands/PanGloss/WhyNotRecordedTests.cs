using Xunit;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// Output of a real <c>pangloss 0.27.0</c> built from the why-not-s3 branch (head b5dd2637a): <c>--describe</c>, and
/// <c>why-not --expect-json ... --json</c> on a Motif test project (accepted, no_morph_match) and on PanGloss's own
/// guid fixture project (environments, prohibition). Nothing is edited.
/// </summary>
public sealed class WhyNotRecordedTests
{
    private const string P = "00000000-0000-0000-0000-";

    private static SIL.Motif.Host.PanGloss.WhyNotCandidate Cand(string spec)
    {
        Assert.True(WhyNotCandidateSpec.TryParse(spec, out var candidate, out var error), error);
        return candidate!;
    }

    internal static string Recorded(string name)
    {
        using var stream = typeof(WhyNotRecordedTests).Assembly.GetManifestResourceStream("WhyNotRecorded." + name + ".json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static WhyNotRejection Rejection(string name)
    {
        Assert.True(WhyNotProjection.TryProject(Recorded(name), "w", "baseline", [Cand("a")], [], out var response, out var error), error);
        return response!.Candidates.Single().Rejection!;
    }

    [Fact]
    public void AMixedIdAndGuidCallIsWrittenAsTheDocumentPanGlossAnsweredAndItsReportProjects()
    {
        const string a = "70c12f3a-1dc4-41ab-8f89-5930878091ef:7a0b9e7f-bf4e-4779-b7a5-964e27797ff1";
        const string c = "a2d73c79-b176-4d30-8740-1876cae5e003:00a0c9ec-4803-4d2b-9965-efb93ab11569";
        var candidates = new[]
        {
            Cand(a + ",daa2c777-b036-418d-a77e-6fe4104ef36f"),
            Cand("00a0c9ec-4803-4d2b-9965-efb93ab11569,7a0b9e7f-bf4e-4779-b7a5-964e27797ff1"),
            Cand(c + "," + a),
        };
        using var written = System.Text.Json.JsonDocument.Parse(WhyNotInput.ToJson(candidates));
        using var recorded = System.Text.Json.JsonDocument.Parse(Recorded("mixed-expect"));
        Assert.Equal(recorded.RootElement.GetRawText().Replace(" ", "").Replace("\n", ""), written.RootElement.GetRawText());

        Assert.True(WhyNotProjection.TryProject(Recorded("mixed"), "ta", "baseline", candidates, [], out var response, out var error), error);
        Assert.Equal(["accepted", "rejected", "rejected"], response!.Candidates.Select(r => r.Result));
        Assert.Equal(0, response.Best);
    }

    [Fact]
    public void TheRealDescribeDeclaresWhyNotAndAcceptsTheRequestFlagsMotifSends()
    {
        var capabilities = PanGlossSurface.ReadDescription(Recorded("describe"), out var detail)!;
        Assert.NotNull(capabilities);
        Assert.Null(capabilities.WhyNotRefusal());
        Assert.Equal(["no_morph_match", "environment_unmet", "adhoc_prohibition"], capabilities.WhyNot!.Kinds.ToArray());
        var request = new PanGlossRequest.WhyNot("g.fwdata", "ta", [Cand("a")]);
        Assert.Null(PanGlossSurface.CheckRequest("pangloss", request, new PanGlossSurfaceCheck(true, string.Empty, capabilities)).Failure);
    }

    [Fact]
    public void ARealAcceptedAnswerKeepsItsMorphsGuidsAndCategory()
    {
        Assert.True(WhyNotProjection.TryProject(Recorded("accepted"), "ta", "baseline", [Cand("a,b")], [], out var response, out var error), error);
        var accepted = response!.Candidates.Single().Acceptance!;
        Assert.Equal(2, accepted.Morphs.Count);
        Assert.Equal("70c12f3a-1dc4-41ab-8f89-5930878091ef", accepted.Morphs[0].EntryGuid);
        Assert.Equal("ff188cef-8443-47a0-a054-cc545e55fa0e", accepted.Morphs[0].FormGuid);
        Assert.Equal("681f7f35-bbcf-4de8-b526-c21a39fbb2cf", accepted.Category);
    }

    [Fact]
    public void ARealNoMorphMatchHasNoPositionOrObjectAndIsStillShown()
    {
        var rejection = Rejection("nomorph");
        Assert.Equal("no_morph_match", rejection.Kind);
        Assert.Null(rejection.Position);
        Assert.Null(rejection.Target);
        Assert.Equal(2, rejection.Morphs.Count);
    }

    [Fact]
    public void ARealSingleEnvironmentFailureCarriesItsGuidTextAndIndex()
    {
        var rejection = Rejection("env-single");
        Assert.Equal(P + "000000000111", rejection.Target!.FieldWorksGuid);
        Assert.Equal(P + "000000000017", rejection.Environment!.FieldWorksGuid);
        Assert.Equal("/[V]_", rejection.Environment.Text);
        Assert.Equal(0, rejection.Environment.Index);
        Assert.Equal(P + "000000000111", rejection.Morphs.Single().FormGuid);
    }

    [Fact]
    public void ARealAnyOfEnvironmentFailureListsEachEnvironmentAndNamesNoneAtTheTop()
    {
        var rejection = Rejection("env-anyof");
        Assert.Null(rejection.Environment!.Text);
        Assert.Equal(["/[V]_", "/t_"], rejection.Environment.Choices!.Select(c => c.Text));
        Assert.Equal("see environments: 2 targets", rejection.Environment.FieldWorksTarget);
    }

    [Fact]
    public void ARealProhibitionKeepsItsRuleGuidAndBothMembers()
    {
        var rejection = Rejection("adhoc");
        Assert.Equal(P + "0000000009a1", rejection.Target!.FieldWorksGuid);
        Assert.Equal([P + "000000000201", P + "000000000101"], rejection.Members!.Select(m => m.FormGuid));
    }
}
