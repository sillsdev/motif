using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Which stored grammar findings name an inspector subject: by identity and stored reach, never by spelling.</summary>
public sealed class InspectQueryTests
{
    private static GrammarWarning Finding(string title, WarningReach reach, string? guid = null, string? code = null) =>
        new(GrammarDiagnosticLevel.Warning, title,
            [new GrammarWarningPart(title, GrammarWarningPartRole.Object) { SubjectGuid = guid, Reach = reach }], [], title)
        {
            Code = code,
        };

    private static readonly GrammarWarning[] Findings =
    [
        Finding("allomorph", new WarningReach(WarningWordsPath.Uses) { AllomorphIds = ["aaaaaaaa-0000-0000-0000-000000000001"] }),
        Finding("environment", new WarningReach(WarningWordsPath.ThroughAllomorphs)
            { AllomorphIds = ["other", "{aaaaaaaa-0000-0000-0000-000000000001}"] }),
        Finding("grammatical info", new WarningReach(WarningWordsPath.Uses) { GrammaticalInfoIds = ["b"] }),
        Finding("letter", new WarningReach(WarningWordsPath.Spelling) { Spellings = ["kat"] }),
        Finding("rule", new WarningReach(WarningWordsPath.RuleTimes) { TimingKeys = [new TraceTimingKey("phon_rule", "rule-1")] }),
        Finding("other rule", new WarningReach(WarningWordsPath.RuleTimes) { TimingKeys = [new TraceTimingKey("morph_rule", "rule-1")] }),
        Finding("unattributed", new WarningReach(WarningWordsPath.ProjectWide)
            { Reason = WarningAttributionReason.NoWordAttribution }, guid: "rule-1", code: "conversion.unsegmentable-form"),
    ];

    [Fact]
    public void AMorphemesWarningsNameItsAllomorphOrGrammaticalInfoByIdentityNeverBySpelling()
    {
        var kat = InspectorSubject.Morpheme("AAAAAAAA-0000-0000-0000-000000000001", "b", "kat")!;

        Assert.Equal(["allomorph", "environment", "grammatical info"],
            InspectQuery.WarningsNaming(Findings, kat, null).Select(finding => finding.CodeLabel));
    }

    [Fact]
    public void ARulesWarningsMatchItsKindAndKeyTogether()
    {
        var key = new TraceTimingKey("phon_rule", "rule-1");

        Assert.Equal(["rule"],
            InspectQuery.WarningsNaming(Findings, InspectorSubject.Rule(key, "Vowel harmony"), key).Select(finding => finding.CodeLabel));
    }

    [Theory]
    [InlineData(WarningWordsPath.MissingObject, WarningAttributionReason.StaleGuid)]
    [InlineData(WarningWordsPath.UnresolvedIdentity, WarningAttributionReason.UnsupportedKind)]
    [InlineData(WarningWordsPath.ProjectWide, WarningAttributionReason.NoWordAttribution)]
    public void AnAuthoredTimingGuidStillMatchesAWarningNamingThatObject(
        WarningWordsPath path, WarningAttributionReason reason)
    {
        const string id = "aaaaaaaa-0000-0000-0000-000000000001";
        var key = new TraceTimingKey("lex_entry", id);
        var finding = Finding("entry", new WarningReach(path) { Reason = reason }, guid: "{" + id.ToUpperInvariant() + "}");

        Assert.Equal([finding], InspectQuery.WarningsNaming([finding], InspectorSubject.Rule(key), key));
    }

    [Fact]
    public void AWarningSubjectMatchesItsCodeAndTheObjectItNames()
    {
        var warning = new InspectorSubject(InspectorSubjectKind.Warning)
            { WarningCode = "conversion.unsegmentable-form", ObjectId = "rule-1" };

        Assert.Equal(["unattributed"], InspectQuery.WarningsNaming(Findings, warning, null).Select(finding => finding.CodeLabel));
        Assert.Empty(InspectQuery.WarningsNaming(Findings, warning with { ObjectId = "rule-2" }, null));
    }

    [Fact]
    public void MembershipReferencesRetainCandidateStrengthAndOwnerLimitsWhenInspectingTheirMember()
    {
        var finding = Finding("feature", new WarningReach(WarningWordsPath.ThroughFeatureOwners)
        {
            AllomorphIds = ["exact"],
            MembershipAllomorphIds = ["a"],
            MembershipGrammaticalInfoIds = ["b"],
            MembershipTimingKeys = [new TraceTimingKey("phon_rule", "rule-1")],
            AttributionLimits = [WarningAttributionReason.UnsupportedKind],
        });
        var word = new AssessmentWordResult("kat", "no-analysis", false, "Search completed", 1, null)
        {
            StoredAnalyses = [new ParserReading([new ParserReadingMorph("kat", "cat", "noun", null, false, null)
                { AllomorphId = "a", GrammaticalInfoId = "b" }]) { StoredAnalysisOpinion = ReadingGrade.Approved }],
        };
        finding = finding with { YourWords = WarningWordsQuery.YourWordsOf(finding, [word], []) };
        var key = new TraceTimingKey("phon_rule", "rule-1");
        var subjects = new[] { InspectorSubject.Morpheme("a", null)!, InspectorSubject.Morpheme(null, "b")!,
            InspectorSubject.Rule(key) };
        foreach (var subject in subjects)
        {
            var result = Assert.Single(InspectQuery.WarningsNaming([finding], subject, subject.TimingKey));
            Assert.Same(finding, result);
            Assert.Equal(WarningAttributionState.MembershipCandidates, result.AttributionState);
            Assert.Empty(result.YourWords!.Words);
            Assert.Equal("kat", Assert.Single(result.YourWords.MembershipCandidates).Row.Word);
            Assert.Equal([WarningAttributionReason.UnsupportedKind], result.AttributionLimits);
            Assert.Equal(0, WarningWordsQuery.Touched([result])!.Words);
        }
        Assert.Empty(InspectQuery.WarningsNaming([finding], InspectorSubject.Rule(key with { Kind = "morph_rule" }),
            key with { Kind = "morph_rule" }));
    }

    [Theory]
    [InlineData(WarningWordsPath.MissingObject)]
    [InlineData(WarningWordsPath.UnresolvedIdentity)]
    [InlineData(WarningWordsPath.ProjectWide)]
    public void AnUnattributedReachDoesNotImplyAnObjectReference(WarningWordsPath path)
    {
        var key = new TraceTimingKey("phon_rule", "rule-1");
        var finding = Finding("unattributed", new WarningReach(path)
        {
            AllomorphIds = ["a"], GrammaticalInfoIds = ["b"], TimingKeys = [key],
            MembershipAllomorphIds = ["a"], MembershipGrammaticalInfoIds = ["b"], MembershipTimingKeys = [key],
        });
        Assert.Empty(InspectQuery.WarningsNaming([finding], InspectorSubject.Morpheme("a", "b")!, null));
        Assert.Empty(InspectQuery.WarningsNaming([finding], InspectorSubject.Rule(key), key));
    }
}
