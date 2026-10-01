using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
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
        Finding("cant tell", new WarningReach(WarningWordsPath.CantTell), guid: "rule-1", code: "conversion.unsegmentable-form"),
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

    [Fact]
    public void AnAuthoredTimingGuidStillMatchesAWarningNamingThatObject()
    {
        const string id = "aaaaaaaa-0000-0000-0000-000000000001";
        var key = new TraceTimingKey("lex_entry", id);
        var finding = Finding("entry", new WarningReach(WarningWordsPath.CantTell), guid: "{" + id.ToUpperInvariant() + "}");

        Assert.Equal([finding], InspectQuery.WarningsNaming([finding], InspectorSubject.Rule(key), key));
    }

    [Fact]
    public void AWarningSubjectMatchesItsCodeAndTheObjectItNames()
    {
        var warning = new InspectorSubject(InspectorSubjectKind.Warning)
            { WarningCode = "conversion.unsegmentable-form", ObjectId = "rule-1" };

        Assert.Equal(["cant tell"], InspectQuery.WarningsNaming(Findings, warning, null).Select(finding => finding.CodeLabel));
        Assert.Empty(InspectQuery.WarningsNaming(Findings, warning with { ObjectId = "rule-2" }, null));
    }
}
