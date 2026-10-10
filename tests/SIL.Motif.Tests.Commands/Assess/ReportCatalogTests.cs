using System.Globalization;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Assess;

/// <summary>
/// Covers the registry itself — resolving a registered kind, refusing an unregistered one, and listing
/// what is available — separately from what any one kind computes.
/// </summary>
public sealed class ReportCatalogTests
{
    [Fact]
    public void ListsEveryRegisteredKind()
    {
        var catalog = new ReportCatalog([new CoverageReportProducer(), new CorrectnessReportProducer()]);

        Assert.Equal(new[] { "correctness", "coverage" }, catalog.All.Select(p => p.Kind));
    }

    [Fact]
    public void ResolvesARegisteredKindByName()
    {
        var coverage = new CoverageReportProducer();
        var catalog = new ReportCatalog([coverage, new CorrectnessReportProducer()]);

        Assert.Same(coverage, catalog.Resolve("coverage"));
    }

    [Fact]
    public void RefusesAnUnknownKindNamingIt()
    {
        var catalog = new ReportCatalog([new CoverageReportProducer()]);

        var failure = Assert.Throws<KeyNotFoundException>(() => catalog.Resolve("nonexistent-kind"));

        Assert.Contains("nonexistent-kind", failure.Message, StringComparison.Ordinal);
        Assert.Contains("coverage", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesTwoKindsRegisteredUnderTheSameName()
    {
        var duplicate = Assert.Throws<ArgumentException>(() => new ReportCatalog(
        [
            new CoverageReportProducer(),
            new CoverageReportProducer(),
        ]));

        Assert.Contains("coverage", duplicate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryProducerAcceptsFrozenParsimonyBundleWithoutAnAssessment()
    {
        var producer = new BundleOnlyReportProducer();
        var catalog = new ReportCatalog([producer]);
        var input = ReportInput.FromParsimony(MakeInputs(), []);

        var rendered = catalog.Resolve("evidence-only").Produce(
            input, new ReportQuery(), new AssessorCatalog(Array.Empty<IAssessor>()));

        Assert.Equal("evidence-only", rendered.Kind);
        Assert.Contains("bundle/1", rendered.Text, StringComparison.Ordinal);
    }

    private static ParsimonyReportInputs MakeInputs() => new(
        "bundle/1",
        new BaselineToken("project/1", Hash('a'), "1", "2026-10-05T00:00:00Z", Hash('b')),
        "baseline",
        null,
        "model/1",
        new ParsimonyArtifactDigest(1, new string('c', 64)),
        new ParsimonyArtifactDigest(1, new string('d', 64)),
        null,
        null,
        []);

    private static string Hash(char digit) => "sha256:" + new string(digit, 64);

    private sealed class BundleOnlyReportProducer : IReportProducer
    {
        public string Kind => "evidence-only";
        public string Description => "Reads frozen grammar evidence.";

        public RenderedReport Produce(ReportInput input, ReportQuery query, IAssessorCatalog assessors)
        {
            if (input is not ReportInput.Parsimony parsimony || parsimony.Assessments.Count != 0)
                throw new ReportRefusalException(Kind, "This report needs only a frozen evidence bundle.");

            return new RenderedReport(Kind, parsimony.Inputs.BundleId);
        }
    }
}

/// <summary>
/// Covers what each registered kind actually computes and the conditions that stop it, plus that neither
/// one reaches into the Assessor catalog it is handed — proven with one that has nothing registered, not
/// by omitting the parameter.
/// </summary>
public sealed class ReportProducerTests
{
    private const string ScopeJson = """{"words":[],"collect":[],"perWordLimitMs":1000,"perWordStepLimit":{"steps":200000,"isUnbounded":false}}""";
    private const string GrammarSha = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    // "The Assessor may since be gone" simulated: nothing is registered, so a call to it would throw.
    private static readonly AssessorCatalog NoAssessorRegistered = new(Array.Empty<IAssessor>());

    [Fact]
    public void Coverage_RefusesAnAssessmentOfAnyOtherKindNamingTheReason()
    {
        var assessment = Build("Correctness", ("a", true));
        var producer = new CoverageReportProducer();

        var failure = Assert.Throws<ReportRefusalException>(
            () => producer.Produce(assessment, new ReportQuery(), NoAssessorRegistered));

        Assert.Equal("coverage", failure.Kind);
        Assert.Contains("Correctness", failure.Message, StringComparison.Ordinal);
        Assert.Contains("ParseTime", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Correctness_RefusesAnAssessmentOfAnyOtherKindNamingTheReason()
    {
        var assessment = Build("ParseTime", ("a", true));
        var producer = new CorrectnessReportProducer();

        var failure = Assert.Throws<ReportRefusalException>(
            () => producer.Produce(assessment, new ReportQuery(), NoAssessorRegistered));

        Assert.Equal("correctness", failure.Kind);
        Assert.Contains("ParseTime", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Correctness", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Correctness_RefusesMissingAssessmentNamingTheMissingCapability()
    {
        var failure = Assert.Throws<ReportRefusalException>(() =>
            new CorrectnessReportProducer().Produce(
                ReportInput.MissingCapability("Assessment"), new ReportQuery(), NoAssessorRegistered));

        Assert.Equal("correctness", failure.Kind);
        Assert.Contains("Assessment", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Coverage_ComputesFromStoredWordsAndOutcomes_WithNoAssessorRegistered()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var assessment = Build("ParseTime", ("motifa", true), ("motifb", false));
        var producer = new CoverageReportProducer();

        var rendered = producer.Produce(assessment, new ReportQuery(), NoAssessorRegistered);

        Assert.Equal("coverage", rendered.Kind);
        Assert.Contains("50.0%", rendered.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Correctness_ComputesAllApprovedMatchesFromStoredMorphology()
    {
        var assessment = Build("Correctness", ("motifa", true), ("motifb", false), ("motifc", false));
        var producer = new CorrectnessReportProducer();

        var rendered = producer.Produce(assessment, new ReportQuery(), NoAssessorRegistered);

        Assert.Equal("correctness", rendered.Kind);
        Assert.Contains("1/3 approved readings matched", rendered.Text, StringComparison.Ordinal);
        Assert.Equal(3, rendered.TotalSearches);
        Assert.Equal(3, rendered.CompletedSearches);
    }

    [Fact]
    public void CorrectnessReportNamesBothLimitsEvenWithEveryApprovedReadingMatched()
    {
        var assessment = Build("Correctness", ("motifa", true));
        var word = assessment.Words[0];
        assessment = assessment with
        {
            Words = [word with { Morphology = word.Morphology! with { Capped = true, TimedOut = true } }],
        };
        var rendered = new CorrectnessReportProducer().Produce(assessment, new ReportQuery(), NoAssessorRegistered);
        Assert.Contains("0 searches completed; 1 incomplete", rendered.Text);
        Assert.Contains("INCOMPLETE — parsing did not finish (search limit and time limit)", rendered.Text);
        Assert.Contains("1/1 approved readings matched", rendered.Text);
        Assert.Equal(1, rendered.TotalSearches);
        Assert.Equal(0, rendered.CompletedSearches);
    }

    [Fact]
    public void Coverage_OverAnEmptyAdjudicatedSet_DoesNotOverclaimAPercentage()
    {
        var words = new[]
        {
            new AssessedWord("motifa", "timed-out", Array.Empty<ParsedAnalysis>()),
        };
        var selection = Selection.Create("test", words.Select(w => w.Word));
        var assessment = new ReportableAssessment(
            "assessment/1", "pangloss", "ParseTime", ScopeJson,
            selection.Name, selection.Words, selection.Sha256, GrammarSha, words);

        var rendered = new CoverageReportProducer().Produce(assessment, new ReportQuery(), NoAssessorRegistered);

        Assert.Contains("not computable", rendered.Text, StringComparison.Ordinal);
    }

    private static ReportableAssessment Build(string kind, params (string Word, bool Analysed)[] words)
    {
        var assessedWords = words
            .Select(w => SIL.Motif.Tests.TestFixtures.CorrectnessFixture.Word(w.Word, w.Analysed))
            .ToArray();
        var selection = Selection.Create("test", words.Select(w => w.Word));
        return new ReportableAssessment(
            "assessment/1", "pangloss", kind, ScopeJson,
            selection.Name, selection.Words, selection.Sha256, GrammarSha, assessedWords);
    }
}
