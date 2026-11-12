using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>The lexicon measures decide over plain records: no session, no SQLite file.</summary>
public sealed class LexiconMeasureDecisionTests
{
    private const string Bundle = "bundle-under-test";

    private static MeasureDefinition Measure(string id) => MeasureCatalog.Find(id)!;

    private static MeasureContext Context(IReadOnlyDictionary<string, string>? names = null) =>
        MeasureContext.For(Bundle, new MeasureDigestContext(1, 1, ParsimonyEvidenceScopeKind.ProjectApproved, null, null),
            names);

    // ---- P-msa-no-category

    [Fact]
    public void NoStemMsasMeansNothingToCheckAndNoFindings()
    {
        var result = MeasureRunner.DecideStemMsaWithoutCategory(Measure("P-msa-no-category"),
            new StemMsaInput(0, []), Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(0, result.Run.EligibleItems);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void AnEntryTheFactsCannotNameIsNamedByItsGuidAndTheFindingIdIsStable()
    {
        var input = new StemMsaInput(5, [new UncategorisedStemMsa("msa-1", "entry-1", false)]);

        var first = MeasureRunner.DecideStemMsaWithoutCategory(Measure("P-msa-no-category"), input, Context());
        var named = MeasureRunner.DecideStemMsaWithoutCategory(Measure("P-msa-no-category"), input,
            Context(new Dictionary<string, string> { ["entry-1"] = "xa" }));

        Assert.Contains("Entry entry-1", Assert.Single(first.Findings).Limitations[0]);
        Assert.Contains("Entry \"xa\"", Assert.Single(named.Findings).Limitations[0]);
        Assert.Equal(first.Findings[0].FindingId, named.Findings[0].FindingId);
        Assert.Equal("entry/entry-1", first.Findings[0].GroupKey);
    }

    [Fact]
    public void FindingsComeOutInMsaOrderWhateverOrderTheFactsGaveThem()
    {
        var input = new StemMsaInput(3, [new("msa-b", "e2", true), new("msa-a", "e1", true)]);

        var result = MeasureRunner.DecideStemMsaWithoutCategory(Measure("P-msa-no-category"), input, Context());

        Assert.Equal(["msa-a", "msa-b"], result.Findings.Select(finding => finding.AttachesTo.Identity.Split('/')[^1]));
    }
}
