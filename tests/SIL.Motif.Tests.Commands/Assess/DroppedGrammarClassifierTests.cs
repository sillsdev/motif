using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Assess;

public sealed class DroppedGrammarClassifierTests
{
    private static readonly Guid Environment = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Allomorph = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Other = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void ADroppedFindingAbsentFromTheBaselineIsIntroducedAndNamesItsObjectsByMotifId()
    {
        var candidate = Check(Finding("grammar.environment.invalid", Environment, Allomorph));

        var result = DroppedGrammarClassifier.Classify(candidate, Check(), new HashSet<Guid>());

        var finding = Assert.Single(result.Introduced);
        Assert.Equal("grammar.environment.invalid", finding.Code);
        Assert.Equal(CanonicalId.FromGuid(Environment).Value, finding.Objects[0].MotifId);
        Assert.Equal("PhEnvironment", finding.Objects[0].Kind);
        Assert.Equal(CanonicalId.FromGuid(Allomorph).Value, finding.Objects[1].MotifId);
        Assert.Equal(0, result.PreExisting);
    }

    [Fact]
    public void AFindingTheBaselineAlreadyHadIsPreExistingAndNotIntroduced()
    {
        var finding = Finding("grammar.allomorph.unsegmentable", Allomorph);

        var result = DroppedGrammarClassifier.Classify(Check(finding), Check(finding), new HashSet<Guid>());

        Assert.Empty(result.Introduced);
        Assert.Equal(1, result.PreExisting);
    }

    [Fact]
    public void APreExistingFindingOnAnObjectTheProposalEditsIsIntroduced()
    {
        var finding = Finding("grammar.allomorph.unsegmentable", Allomorph);

        var result = DroppedGrammarClassifier.Classify(Check(finding), Check(finding), new HashSet<Guid> { Allomorph });

        Assert.Single(result.Introduced);
        Assert.Equal(0, result.PreExisting);
    }

    [Fact]
    public void AFindingOnAnotherObjectWithTheSameCodeIsIntroduced()
    {
        var result = DroppedGrammarClassifier.Classify(
            Check(Finding("grammar.environment.invalid", Environment), Finding("grammar.environment.invalid", Other)),
            Check(Finding("grammar.environment.invalid", Environment)), new HashSet<Guid>());

        var introduced = Assert.Single(result.Introduced);
        Assert.Equal(CanonicalId.FromGuid(Other).Value, introduced.Objects.Single().MotifId);
        Assert.Equal(1, result.PreExisting);
    }

    [Fact]
    public void AWarningOutsideTheClosedListThatNamesAnObjectIsSurfacedGenerically()
    {
        var result = DroppedGrammarClassifier.Classify(Check(Finding("grammar.brand-new-code", Allomorph)),
            Check(), new HashSet<Guid>());

        var finding = Assert.Single(result.Introduced);
        Assert.False(finding.Listed);
        Assert.Equal("grammar.brand-new-code", finding.Code);
        Assert.Contains("PanGloss reported grammar.brand-new-code on", finding.Message);
        Assert.Contains(CanonicalId.FromGuid(Allomorph).Value, finding.Message);
    }

    [Fact]
    public void AnUnlistedWarningTheBaselineAlreadyHadIsPreExisting()
    {
        var finding = Finding("grammar.brand-new-code", Allomorph);

        var result = DroppedGrammarClassifier.Classify(Check(finding), Check(finding), new HashSet<Guid>());

        Assert.Empty(result.Introduced);
        Assert.Equal(1, result.PreExisting);
    }

    [Fact]
    public void InformationFindingsAndStoredAnalysisWordCountsAreNotGrammarDrops()
    {
        var info = Finding("grammar.brand-new-code", Allomorph) with { Severity = GrammarDiagnosticLevel.Information };
        var words = Finding("grammar.stored-analysis.incomplete", Allomorph);

        var result = DroppedGrammarClassifier.Classify(Check(info, words), Check(), new HashSet<Guid>());

        Assert.Empty(result.Introduced);
    }

    [Theory]
    [InlineData(GrammarDiagnosticLevel.Warning)]
    [InlineData(GrammarDiagnosticLevel.Error)]
    public void AnUnlistedFindingThatNamesNoObjectIsReportedGenericallyNotReadAsClean(GrammarDiagnosticLevel level)
    {
        var noSubject = Finding("grammar.brand-new-code") with { Severity = level };

        var result = DroppedGrammarClassifier.Classify(Check(noSubject), Check(), new HashSet<Guid>());

        var finding = Assert.Single(result.Introduced);
        Assert.False(finding.Listed);
        Assert.Empty(finding.Objects);
        Assert.Contains("an object PanGloss did not identify", finding.Message);
    }

    [Fact]
    public void AnUnlistedFindingWithNoObjectThatTheBaselineAlsoHasIsPreExisting()
    {
        var noSubject = Finding("grammar.brand-new-code");

        var result = DroppedGrammarClassifier.Classify(Check(noSubject), Check(noSubject), new HashSet<Guid>());

        Assert.Empty(result.Introduced);
        Assert.Equal(1, result.PreExisting);
    }

    [Fact]
    public void LongTextIsCutSoAStoredResultStaysSmall()
    {
        var long_ = Finding("grammar.environment.invalid", Environment) with { Description = new string('x', 5000) };
        var named = Finding("grammar.environment.invalid", Allomorph);
        named = named with { Subject = [named.Subject[0] with { Title = new string('n', 5000) }] };

        var result = DroppedGrammarClassifier.Classify(Check(long_, named), Check(), new HashSet<Guid>());

        Assert.All(result.Introduced, finding => Assert.True(finding.Message.Length <= 400));
        Assert.True(result.Introduced[1].Objects[0].Name!.Length <= 120);
    }

    [Fact]
    public void EveryRealDropPanGlossEmitsAtErrorLevelIsOnTheList()
    {
        string[] realDrops =
        [
            "fwdata.no-usable-allomorphs", "grammar.msa.no-allomorphs", "grammar.msa.no-rule-form-allomorphs",
            "grammar.circumfix.missing-half", "grammar.environment.unresolved",
            "grammar.msa.exception-feature-unresolved", "grammar.msa.inflection-class-unresolved",
            "grammar.msa.lex-entry-infl-type-unresolved", "grammar.msa.stem-name-unresolved",
        ];

        Assert.All(realDrops, code => Assert.Contains(code, DroppedGrammarClassifier.Codes));
    }

    [Theory]
    [InlineData("grammar.stem-without-category", GrammarDiagnosticLevel.Error)]
    [InlineData("grammar.undeclared-segment", GrammarDiagnosticLevel.Warning)]
    public void AnUnlistedCheckFindingMarksAPartialRuleOrADataGapAndIsNotDroppedGrammar(string code,
        GrammarDiagnosticLevel level)
    {
        var finding = Finding(code, Allomorph) with { Severity = level, Origin = GrammarFindingOrigin.Check };

        var result = DroppedGrammarClassifier.Classify(Check(finding), Check(), new HashSet<Guid> { Allomorph });

        Assert.True(result.IsClean);
        Assert.Equal(0, result.PreExisting);
    }

    [Fact]
    public void AnUnlistedImportFindingStillReadsAsDroppedBesideACheckFinding()
    {
        var check = Finding("grammar.stem-without-category", Allomorph) with { Origin = GrammarFindingOrigin.Check };
        var import = Finding("grammar.brand-new-code", Other) with { Origin = GrammarFindingOrigin.Import };

        var result = DroppedGrammarClassifier.Classify(Check(check, import), Check(), new HashSet<Guid>());

        Assert.Equal("grammar.brand-new-code", Assert.Single(result.Introduced).Code);
    }

    [Fact]
    public void AnUnlistedFindingTheBaselineAlreadyHasStaysPreExistingOnAnObjectTheProposalEdits()
    {
        var finding = Finding("grammar.brand-new-code", Allomorph);

        var result = DroppedGrammarClassifier.Classify(Check(finding), Check(finding), new HashSet<Guid> { Allomorph });

        Assert.True(result.IsClean);
        Assert.Equal(1, result.PreExisting);
    }

    [Fact]
    public void AnUnlistedFindingWithNoObjectMatchesTheBaselineOnItsCodeAloneWhateverTheFileStemInItsText()
    {
        var inBaseline = Finding("grammar.brand-new-code") with { Description = "cannot read C:\\base\\proj.fwdata" };
        var inCandidate = Finding("grammar.brand-new-code") with { Description = "cannot read C:\\scratch\\copy.fwdata" };

        var result = DroppedGrammarClassifier.Classify(Check(inCandidate), Check(inBaseline), new HashSet<Guid>());

        Assert.True(result.IsClean);
        Assert.Equal(1, result.PreExisting);
    }

    [Fact]
    public void AListedFindingWithNoObjectStillMatchesTheBaselineOnItsText()
    {
        var listed = Finding("grammar.environment.invalid") with { Description = "one" };
        var other = listed with { Description = "two" };

        var result = DroppedGrammarClassifier.Classify(Check(other), Check(listed), new HashSet<Guid>());

        Assert.Single(result.Introduced);
    }

    [Fact]
    public void ADifferentCompileFailureInTheCandidateIsIntroducedEvenWhenTheBaselineFailedToCompile()
    {
        var inBaseline = CompileFailure("cannot compile the Baseline grammar");
        var inCandidate = CompileFailure("cannot compile the candidate grammar");

        var result = DroppedGrammarClassifier.Classify(Check(inCandidate), Check(inBaseline), new HashSet<Guid>());

        var introduced = Assert.Single(result.Introduced);
        Assert.Equal(PanGlossFindingCodes.CompileFailed, introduced.Code);
        Assert.Equal(0, result.PreExisting);
    }

    [Fact]
    public void TheSameCompileFailureInTheBaselineAndTheCandidateIsPreExisting()
    {
        var failure = CompileFailure("cannot compile the grammar");

        var result = DroppedGrammarClassifier.Classify(Check(failure), Check(failure), new HashSet<Guid>());

        Assert.True(result.IsClean);
        Assert.Equal(1, result.PreExisting);
    }

    [Fact]
    public void TheRecordedCodeListIsTheOneOfTheReleaseTheProjectPins()
    {
        using var pinned = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestFixtures", "GrammarHealth", "import-warning-codes-v0.13.0.json")));
        using var release = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(),
            "pangloss-release.json")));

        Assert.True(pinned.RootElement.GetProperty("tag").GetString() == release.RootElement.GetProperty("tag").GetString(),
            "pangloss-release.json pins another PanGloss tag: capture import-warning-codes again from that tag " +
            "(rust/crates/pg-snapshot/src/warning.rs) and update this fixture.");
    }

    [Fact]
    public void EveryDroppedGrammarCodeIsACodeOfThePinnedParserRelease()
    {
        using var pinned = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestFixtures", "GrammarHealth", "import-warning-codes-v0.13.0.json")));
        var released = pinned.RootElement.GetProperty("codes").EnumerateArray()
            .Select(code => code.GetString()!).ToHashSet(StringComparer.Ordinal);

        var missing = DroppedGrammarClassifier.Codes.Where(code => !released.Contains(code)).Order().ToArray();

        Assert.True(missing.Length == 0,
            "The pinned PanGloss release no longer has these dropped-grammar codes: " + string.Join(", ", missing));
        Assert.Equal(48, DroppedGrammarClassifier.Codes.Count);
    }

    private static GrammarWarning CompileFailure(string text) =>
        Finding(PanGlossFindingCodes.CompileFailed) with
        {
            Severity = GrammarDiagnosticLevel.Error,
            Description = text,
        };

    private static GrammarCheckResponse Check(params GrammarWarning[] findings) => new(findings, HasBaseline: true);

    private static GrammarWarning Finding(string code, params Guid[] subjects) =>
        new(GrammarDiagnosticLevel.Warning, code,
            subjects.Select(guid => new GrammarWarningPart("subject", GrammarWarningPartRole.Object, guid.ToString())
            {
                SubjectGuid = guid.ToString(),
                SourceClass = guid == Environment ? "PhEnvironment" : "MoForm",
                Title = "title " + guid.ToString()[..2],
            }).ToArray(),
            [], "text")
        {
            Code = code,
            Origin = GrammarFindingOrigin.Import,
            Description = "description of " + code,
        };
}
