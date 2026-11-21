using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>Motif's side of why-not: candidate spelling, the request's arguments, detection, the report and refusals.</summary>
public sealed class WhyNotTests : IDisposable
{
    private const string Entry = "11111111-1111-1111-1111-111111111111";
    private const string Msa = "22222222-2222-2222-2222-222222222222";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-why-not-" + Guid.NewGuid().ToString("N"));

    public WhyNotTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static WhyNotCandidate Parse(string spec)
    {
        Assert.True(WhyNotCandidateSpec.TryParse(spec, out var candidate, out var error), error);
        return candidate!;
    }

    private static readonly IReadOnlyList<WhyNotCandidate> OneCandidate = [Parse("kib,ak,ek")];

    [Fact]
    public void ACandidateKeepsPositionsChoicesAllomorphsAndGuids()
    {
        var candidate = Parse($"kib@kib-1, ak|ik ,{Entry}:{Msa}|ek={Entry}:{Msa}");
        Assert.Equal(3, candidate.Positions.Count);
        Assert.Equal(["ak", "ik"], candidate.Positions[1].Select(c => c.Morpheme));
        Assert.Equal("kib-1", candidate.Positions[0][0].Allomorph);
        Assert.Null(candidate.Positions[2][0].Morpheme);
        Assert.Equal(Entry, candidate.Positions[2][0].EntryGuid);
        Assert.Equal("ek", candidate.Positions[2][1].Morpheme);
        Assert.Equal($"kib@kib-1,ak|ik,{Entry}:{Msa}|ek={Entry}:{Msa}", candidate.Spelling);
    }

    [Fact]
    public void AMotifIdNamesAGuidThroughItsCanonicalForm()
    {
        var id = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(Guid.Parse(Entry)).Suffix;
        var msa = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(Guid.Parse(Msa)).Suffix;
        var candidate = Parse($"{id}:{msa}");
        Assert.Equal(Entry, candidate.Positions[0][0].EntryGuid);
        Assert.Equal(Msa, candidate.Positions[0][0].MsaGuid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kib,,ek")]
    [InlineData("kib,ak|")]
    [InlineData("kib@")]
    [InlineData("kib@a@b")]
    [InlineData("kib ak")]
    [InlineData("11111111-1111-1111-1111-111111111111:")]
    [InlineData("zzz:yyy")]
    public void AMalformedCandidateIsRefusedWithASentence(string spec)
    {
        Assert.False(WhyNotCandidateSpec.TryParse(spec, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void CandidatesAreCappedByPositionsAndChoices()
    {
        Assert.False(WhyNotCandidateSpec.TryParse(string.Join(',', Enumerable.Repeat("a", WhyNotCandidateSpec.MaxPositions + 1)), out _, out _));
        Assert.False(WhyNotCandidateSpec.TryParse(string.Join('|', Enumerable.Repeat("a", WhyNotCandidateSpec.MaxChoices + 1)), out _, out _));
    }

    [Fact]
    public void IdCandidatesGoAsOneJsonDocumentWithGuidsAndAllomorphs()
    {
        var candidates = new[] { Parse("kib,ak@ak-1|ik"), Parse($"kib=11111111-1111-1111-1111-111111111111:{Msa}") };
        Assert.True(WhyNotInput.UsesJson(candidates));
        using var json = JsonDocument.Parse(WhyNotInput.ToJson(candidates));
        var first = json.RootElement[0];
        Assert.Equal("ak-1", first[1][0].GetProperty("allomorph").GetString());
        Assert.False(first[1][1].TryGetProperty("allomorph", out _));
        Assert.Equal("ik", first[1][1].GetProperty("morpheme").GetString());
        var guid = json.RootElement[1][0][0].GetProperty("guid");
        Assert.Equal(Entry, guid.GetProperty("entry_guid").GetString());
        Assert.Equal(Msa, guid.GetProperty("msa_guid").GetString());
        Assert.Null(WhyNotInput.Problem(candidates));
    }

    [Fact]
    public void GuidOnlyCandidatesGoAsExpectGuidAndCannotBeMixedWithIds()
    {
        var guidOnly = new[] { Parse($"{Entry}:{Msa},{Entry}:{Msa}|{Msa}:{Entry}") };
        Assert.False(WhyNotInput.UsesJson(guidOnly));
        Assert.Null(WhyNotInput.Problem(guidOnly));
        Assert.Equal($"{Entry}:{Msa},{Entry}:{Msa}|{Msa}:{Entry}", WhyNotInput.ToGuidSpec(guidOnly[0]));
        Assert.NotNull(WhyNotInput.Problem([guidOnly[0], Parse("kib,ak")]));
        Assert.NotNull(WhyNotInput.Problem([Parse($"{Entry}:{Msa}@a1")]));
    }

    [Fact]
    public async Task TheRequestWritesTheCandidatesAndAsksForTheJsonReportWithBounds()
    {
        var request = new PanGlossRequest.WhyNot("g.fwdata", "kibakek", OneCandidate);
        var startInfo = new System.Diagnostics.ProcessStartInfo();
        request.AddArguments(startInfo, _root);
        Assert.Equal(["why-not", "g.fwdata", "kibakek", "--expect-json", Path.Combine(_root, "expect.json"), "--json",
            "--work-cap", "20000000", "--word-timeout-ms", "30000"], startInfo.ArgumentList);
        await request.PrepareAsync(_root, CancellationToken.None);
        Assert.Contains("\"kib\"", File.ReadAllText(Path.Combine(_root, "expect.json")));

        var byGuid = new PanGlossRequest.WhyNot("g.fwdata", "kibakek", [Parse($"{Entry}:{Msa}"), Parse($"{Msa}:{Entry}")]);
        var guidArguments = new System.Diagnostics.ProcessStartInfo();
        byGuid.AddArguments(guidArguments, _root);
        Assert.Equal(["--expect-guid", $"{Entry}:{Msa}", "--expect-guid", $"{Msa}:{Entry}"],
            guidArguments.ArgumentList.Skip(3).Take(4));
    }

    [Fact]
    public void ADeclaredCapabilityIsReadWithItsKinds()
    {
        var capabilities = PanGlossSurface.ReadDescription(WhyNotFixtures.Describe(WhyNotFixtures.DeclaredWhyNot), out _)!;
        Assert.Null(capabilities.WhyNotRefusal());
        Assert.Equal(["no_morph_match", "environment_unmet", "adhoc_prohibition"], capabilities.WhyNot!.Kinds.ToArray());
        Assert.True(capabilities.WhyNot.SuiteRows);
        Assert.Null(PanGlossSurface.CheckRequest("pangloss",
            new PanGlossRequest.WhyNot("g.fwdata", "kibakek", OneCandidate),
            new PanGlossSurfaceCheck(true, string.Empty, capabilities)).Failure);
    }

    [Theory]
    [InlineData(null, true, false, "declares no interfaces.whyNot")]
    [InlineData("{ \"schema\": \"pangloss.whynot.v2\", \"ids\": \"grammar-xml\", \"kinds\": [] }", true, false, "pangloss.whynot.v2")]
    [InlineData("{ \"schema\": \"pangloss.whynot.v1\", \"ids\": \"ordinals\", \"kinds\": [] }", true, false, "ordinals")]
    [InlineData("{ \"schema\": \"pangloss.whynot.v1\", \"ids\": \"grammar-xml\", \"kinds\": [] }", false, false, "no why-not command")]
    [InlineData("{ \"schema\": \"pangloss.whynot.v1\", \"ids\": \"grammar-xml\", \"kinds\": [] }", true, true, "no why-not command")]
    public void ACapabilityMissingOrDifferentIsNamed(string? whyNot, bool command, bool hidden, string expected)
    {
        var capabilities = PanGlossSurface.ReadDescription(WhyNotFixtures.Describe(whyNot, command, hidden), out _)!;
        Assert.Contains(expected, capabilities.WhyNotRefusal(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheInvokerRefusesAParserWithoutTheCapabilityBeforeItRunsAnything()
    {
        var grammar = Path.Combine(_root, "g.fwdata");
        File.WriteAllText(grammar, "x");
        var executable = FakeParser.Copy(Path.Combine(_root, "fake"));
        using var invoker = new PanGlossInvoker(executable);
        var outcome = await invoker.RunAsync(new PanGlossRequest.WhyNot(grammar, "kibakek", OneCandidate), "why-not", CancellationToken.None);
        var unavailable = Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Equal("why-not", unavailable.MissingCapability);
        Assert.Contains("cannot answer why-not", unavailable.Detail, StringComparison.Ordinal);
        var shaped = WhyNotQuery.Shape(outcome, "p.fwdata", "kibakek", OneCandidate, "baseline");
        Assert.Equal(RefusalCodes.WhyNotParserUnsupported, shaped.Refusal!.Code);
        Assert.Contains("interfaces.whyNot", shaped.Refusal.Message, StringComparison.Ordinal);
    }

    private static WhyNotResponse Project(string report, IReadOnlyList<WhyNotCandidate> candidates, params string[] kinds)
    {
        Assert.True(WhyNotProjection.TryProject(report, "kibakek", "baseline", candidates, kinds, out var response, out var error), error);
        return response!;
    }

    private static readonly IReadOnlyList<WhyNotCandidate> Four = [.. Enumerable.Repeat(Parse("kib,ak,ek"), 4)];

    [Fact]
    public void EachAnswerKeepsItsKindPlainPositionMorphsAndTarget()
    {
        var response = Project(WhyNotFixtures.Report(WhyNotFixtures.Accepted, WhyNotFixtures.NoMorphMatch,
            WhyNotFixtures.EnvironmentUnmet, WhyNotFixtures.Unknown), Four, "no_morph_match", "environment_unmet");
        Assert.Equal(["no_morph_match", "environment_unmet"], response.AnswerableKinds);
        Assert.Equal(["accepted", "rejected", "rejected", "unknown"], response.Candidates.Select(c => c.Result));
        Assert.Equal(0, response.Best);

        var accepted = response.Candidates[0].Acceptance!;
        Assert.Equal(["kib", "ak", "ek"], accepted.Morphs.Select(m => m.Morpheme));
        Assert.Equal("posN", accepted.Category);

        var rejection = response.Candidates[1].Rejection!;
        Assert.Equal("no_morph_match", rejection.Kind);
        Assert.Equal(1, rejection.Position);
        Assert.StartsWith("No morph matches at position 1", rejection.Plain, StringComparison.Ordinal);
        Assert.Equal("ak", rejection.Morphs.Single().Morpheme);
        Assert.Equal(new WhyNotTarget("affix", "ak", "00000000-0000-0000-0000-000000000015", "00000000-0000-0000-0000-000000000015"),
            rejection.Target);

        var environment = response.Candidates[2].Rejection!;
        Assert.Equal("hc_reported", environment.Provenance);
        Assert.Equal(1, environment.Alternatives);
        Assert.Equal("no FieldWorks target yet", environment.Target!.FieldWorksTarget);
        Assert.Null(environment.Target.FieldWorksGuid);
        Assert.Equal("ek-a", environment.Environment!.AllomorphId);
        Assert.Equal(0, environment.Environment.Index);
        Assert.Equal("kib", environment.Environment.Left);
        Assert.Equal("no FieldWorks target yet", environment.Environment.FieldWorksTarget);

        var unknown = response.Candidates[3];
        Assert.Equal("unclassified_failure", unknown.Stop);
        Assert.StartsWith("HC stopped", unknown.StopNote, StringComparison.Ordinal);
        Assert.Null(unknown.Rejection);
    }

    private const string Prefix = "00000000-0000-0000-0000-";

    [Fact]
    public void ASnapshotRouteEnvironmentKeepsItsGuidTextFormAndEntryGuids()
    {
        var rejection = Project(WhyNotFixtures.Report(WhyNotFixtures.EnvironmentUnmetOnSnapshot), OneCandidate)
            .Candidates[0].Rejection!;
        Assert.Equal(Prefix + "000000000111", rejection.Target!.FieldWorksGuid);
        var environment = rejection.Environment!;
        Assert.Equal(Prefix + "000000000017", environment.FieldWorksGuid);
        Assert.Equal("/[V]_", environment.Text);
        Assert.Equal(0, environment.Index);
        Assert.Empty(environment.Choices!);
        var morph = rejection.Morphs.Single();
        Assert.Equal(Prefix + "000000000111", morph.FormGuid);
        Assert.Equal(Prefix + "000000000100", morph.EntryGuid);
        Assert.Equal(Prefix + "000000000102", morph.MsaGuid);
    }

    [Fact]
    public void AnAnyOfEnvironmentFailureListsEveryFailedEnvironment()
    {
        var environment = Project(WhyNotFixtures.Report(WhyNotFixtures.EnvironmentAnyOfUnmet), OneCandidate)
            .Candidates[0].Rejection!.Environment!;
        Assert.Equal("aSal", environment.AllomorphId);
        Assert.Null(environment.Text);
        Assert.Equal([("/[V]_", 0, Prefix + "000000000017"), ("/t_", 1, Prefix + "000000000030")],
            environment.Choices!.Select(c => (c.Text!, c.Index!.Value, c.FieldWorksGuid!)));
    }

    [Fact]
    public void AProhibitionKeepsItsRuleGuidAndEveryMember()
    {
        var rejection = Project(WhyNotFixtures.Report(WhyNotFixtures.AdhocProhibition), OneCandidate).Candidates[0].Rejection!;
        Assert.Equal(new WhyNotTarget("rule", null, Prefix + "0000000009a1", Prefix + "0000000009a1"), rejection.Target);
        Assert.Equal([Prefix + "000000000201", Prefix + "000000000101"], rejection.Members!.Select(m => m.FormGuid));
        Assert.Equal([Prefix + "000000000202", Prefix + "000000000102"], rejection.Members!.Select(m => m.MsaGuid));
        Assert.Equal(["tal", "sal"], rejection.Members!.Select(m => m.Morpheme));
    }

    [Fact]
    public void AnAmbiguousProhibitionHasNoRuleGuidButStillListsItsMembers()
    {
        var rejection = Project(WhyNotFixtures.Report(WhyNotFixtures.AdhocProhibitionAmbiguous), OneCandidate)
            .Candidates[0].Rejection!;
        Assert.Null(rejection.Target!.FieldWorksGuid);
        Assert.Equal("no FieldWorks target yet", rejection.Target.FieldWorksTarget);
        Assert.Equal(2, rejection.Members!.Count);
    }

    [Fact]
    public void AFieldWorksGuidOnAnEnvironmentIsRenderedWhenPresent()
    {
        var report = WhyNotFixtures.Report(WhyNotFixtures.EnvironmentUnmet.Replace(
            "\"index\": 0", "\"index\": 0, \"guid\": \"00000000-0000-0000-0000-0000000000aa\""));
        var environment = Project(report, OneCandidate).Candidates[0].Rejection!.Environment!;
        Assert.Equal("00000000-0000-0000-0000-0000000000aa", environment.FieldWorksTarget);
    }

    [Fact]
    public void TheOutputIsBounded()
    {
        var morphs = string.Join(',', Enumerable.Range(0, 40).Select(i => $"{{\"morpheme\":\"m{i}\"}}"));
        var features = string.Join(',', Enumerable.Range(0, 12).Select(i => $"{{\"path\":\"p{i}\",\"required\":\"a\",\"actual\":\"b\"}}"));
        var plain = new string('x', 900);
        var report = WhyNotFixtures.Report(WhyNotFixtures.NoMorphMatch
            .Replace("\"morphs\": [ {\"morpheme\":\"ak\"} ]", $"\"morphs\": [{morphs}]")
            .Replace("\"features\": []", $"\"features\": [{features}]")
            .Replace("No morph matches at position 1: nothing in the word can be the expected suffix `ak`.", plain));
        var response = Project(report, OneCandidate);
        var rejection = response.Candidates[0].Rejection!;
        Assert.Equal(WhyNotLimits.MaxMorphs, rejection.Morphs.Count);
        Assert.Equal(40 - WhyNotLimits.MaxMorphs, rejection.MorphsOmitted);
        Assert.Equal(WhyNotLimits.MaxFeatures, rejection.Features.Count);
        Assert.Equal(12 - WhyNotLimits.MaxFeatures, rejection.FeaturesOmitted);
        Assert.Equal(WhyNotLimits.MaxSentence, rejection.Plain.Length);
        Assert.Equal(new WhyNotBound(1, 40 - WhyNotLimits.MaxMorphs, 1), response.Bound);
    }

    [Theory]
    [InlineData("{\"schema\":\"pangloss.whynot.v2\",\"word\":\"w\",\"results\":[],\"best\":null}")]
    [InlineData("not json")]
    [InlineData("{\"schema\":\"pangloss.whynot.v1\",\"word\":\"w\",\"results\":[],\"best\":null}")]
    public void AReportThatDoesNotMatchTheQuestionIsMalformed(string report)
    {
        Assert.False(WhyNotProjection.TryProject(report, "w", "baseline", OneCandidate, [], out var response, out var error));
        Assert.Null(response);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void ARejectionWithoutAFailureAndAnUnknownWithoutAStopAreMalformed()
    {
        var rejected = WhyNotFixtures.Report("{\"expect\":[],\"result\":\"rejected\",\"failure\":null,\"stop\":null}");
        var unknown = WhyNotFixtures.Report("{\"expect\":[],\"result\":\"unknown\",\"failure\":null,\"stop\":null}");
        var outOfRange = WhyNotFixtures.Report(WhyNotFixtures.Accepted).Replace("\"best\":0", "\"best\":3");
        var acceptedWithoutAnalysis = WhyNotFixtures.Report("{\"expect\":[],\"result\":\"accepted\",\"analysis\":null,\"failure\":null,\"stop\":null}");
        var unmetWithoutTarget = WhyNotFixtures.Report("{\"expect\":[],\"result\":\"rejected\",\"failure\":{\"kind\":\"environment_unmet\",\"plain\":\"x.\",\"object\":null,\"environment\":null}}");
        foreach (var report in new[] { rejected, unknown, outOfRange, acceptedWithoutAnalysis, unmetWithoutTarget })
            Assert.False(WhyNotProjection.TryProject(report, "kibakek", "baseline", OneCandidate, [], out _, out _));
    }

    [Fact]
    public void AnAnyOfFailurePointsItsOwnTargetAtTheListAndAnAbsentProvenanceStaysNull()
    {
        var anyOf = Project(WhyNotFixtures.Report(WhyNotFixtures.EnvironmentAnyOfUnmet), OneCandidate).Candidates[0].Rejection!;
        Assert.Equal("see environments: 2 targets", anyOf.Environment!.FieldWorksTarget);
        var bare = WhyNotFixtures.NoMorphMatch.Replace("\"provenance\": \"derived\",", string.Empty, StringComparison.Ordinal);
        Assert.Null(Project(WhyNotFixtures.Report(bare), OneCandidate).Candidates[0].Rejection!.Provenance);
    }

    [Fact]
    public void ParserOutcomesBecomeRegisteredRefusals()
    {
        CommandOutcome<WhyNotResponse> Shape(PanGlossOutcome outcome) =>
            WhyNotQuery.Shape(outcome, "p.fwdata", "kibakek", OneCandidate, "baseline");
        var report = WhyNotFixtures.Report(WhyNotFixtures.NoMorphMatch);

        var good = Shape(new PanGlossOutcome.Completed(report, string.Empty, TimeSpan.Zero) { WhyNotKinds = ["no_morph_match"] });
        Assert.True(good.Succeeded);
        Assert.Equal(["no_morph_match"], good.Value!.AnswerableKinds);

        var input = Shape(new PanGlossOutcome.Refused(1, "unknown morpheme id `zzz`", string.Empty, "unknown morpheme id `zzz`"));
        Assert.Equal(RefusalCodes.WhyNotInputRefused, input.Refusal!.Code);
        Assert.Contains("zzz", input.Refusal.Message, StringComparison.Ordinal);

        Assert.Equal(RefusalCodes.WhyNotMalformed, Shape(new PanGlossOutcome.Completed("{}", string.Empty, TimeSpan.Zero)).Refusal!.Code);
        Assert.Equal(RefusalCodes.WhyNotUnfinished, Shape(new PanGlossOutcome.TimedOut(TimeSpan.FromMinutes(10), "timed out")).Refusal!.Code);
        Assert.Equal(RefusalCodes.WhyNotUnfinished, Shape(new PanGlossOutcome.Incomplete("no report", string.Empty)).Refusal!.Code);
        Assert.Equal(RefusalCodes.WordTraceCancelled, Shape(new PanGlossOutcome.Cancelled()).Refusal!.Code);
        Assert.Equal(RefusalCodes.WordTraceParserUnavailable,
            Shape(new PanGlossOutcome.Unavailable("no parser") { ExecutableMissing = true }).Refusal!.Code);
    }

    [Theory]
    [InlineData("", "kib,ak")]
    [InlineData("two words", "kib,ak")]
    [InlineData("-kib", "kib,ak")]
    [InlineData("kibak", "")]
    [InlineData("kibak", "kib,,ak")]
    public void ARequestMotifCannotSendIsRefusedBeforeAnyProjectIsOpened(string word, string expect)
    {
        var outcome = WhyNotQuery.Explain(new WhyNotRequest("never-opened.fwdata", word, expect.Length == 0 ? [] : [expect]), parserPath: null);
        Assert.Equal(RefusalCodes.WhyNotRequestInvalid, outcome.Refusal!.Code);
    }

    [Fact]
    public void TooManyCandidatesAndAMixedCallAreRefused()
    {
        var many = Enumerable.Repeat("kib,ak", WhyNotLimits.MaxCandidates + 1).ToArray();
        Assert.Equal(RefusalCodes.WhyNotRequestInvalid,
            WhyNotQuery.Explain(new WhyNotRequest("p.fwdata", "kibak", many), parserPath: null).Refusal!.Code);
        Assert.Equal(RefusalCodes.WhyNotRequestInvalid,
            WhyNotQuery.Explain(new WhyNotRequest("p.fwdata", "kibak", ["kib,ak", $"{Entry}:{Msa}"]), parserPath: null).Refusal!.Code);
    }
}
