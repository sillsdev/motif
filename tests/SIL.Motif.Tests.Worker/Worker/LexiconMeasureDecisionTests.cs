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

    // ---- P-gloss-echoes-form

    private static LexiconEntryFact Entry(string guid, string[] forms, params (string Sense, string[] Glosses)[] senses) =>
        new(guid, "suffix", guid + "-lexeme", [], forms,
            senses.Select(item => new LexiconSenseFact(item.Sense,
                item.Glosses.Select(gloss => new LexiconTextFact("en", gloss)).ToArray())).ToArray(), []);

    [Theory]
    [InlineData("XYZ", "xyz", true)]
    [InlineData("xyz.V", "XYZ", true)]
    [InlineData("XYZ.PANGA", "xyz", true)]
    [InlineData("XYZ.", "xyz", false)]
    [InlineData("XYZW", "xyz", false)]
    [InlineData("A.XYZ", "xyz", false)]
    [InlineData("\u00D1U", "\u00F1u", true)]
    public void AGlossEchoesAFormOnlyWhenItIsTheFormOrTheFormAndADottedSuffix(string gloss, string form, bool expected) =>
        Assert.Equal(expected, MeasureRunner.GlossEchoes(gloss, form));

    [Fact]
    public void HyphensAndEqualsSignsAroundAGlossOrFormAreIgnored()
    {
        var input = new LexiconInput([Entry("e1", ["-xyz-", "=abc"], ("s1", ["xyz-"]), ("s2", ["=ABC.V"]),
            ("s3", ["meaning"]))]);

        var result = MeasureRunner.DecideGlossEchoesForm(Measure("P-gloss-echoes-form"), input, Context());

        Assert.Equal(3, result.Run.EligibleItems);
        Assert.Equal(["s1", "s2"], result.Findings.Select(finding => finding.AttachesTo.Identity.Split('/')[^1]));
    }

    [Fact]
    public void ASenseWithNoGlossIsNotCountedAndAnEntryWithNoFormIsNeverFlagged()
    {
        var input = new LexiconInput([Entry("e1", ["xyz"], ("s1", [" "]), ("s2", [])),
            Entry("e2", [], ("s3", ["XYZ"]))]);

        var result = MeasureRunner.DecideGlossEchoesForm(Measure("P-gloss-echoes-form"), input, Context());

        Assert.Equal(1, result.Run.EligibleItems);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ASenseWithSeveralEchoingGlossesGivesOneFinding()
    {
        var input = new LexiconInput([Entry("e1", ["xyz"], ("s1", ["XYZ", "xyz.V"]))]);

        var result = MeasureRunner.DecideGlossEchoesForm(Measure("P-gloss-echoes-form"), input, Context());

        Assert.Single(result.Findings);
    }

    // ---- P-entry-duplicate

    private static LexiconEntryFact Lexeme(string guid, string morphType, string[] forms, string[] glosses,
        string[]? msas = null) =>
        new(guid, morphType, guid + "-lexeme", forms.Select(form => new LexiconTextFact("qaa", form)).ToArray(), forms,
            glosses.Length == 0 ? [] : [new LexiconSenseFact(guid + "-sense",
                glosses.Select(gloss => new LexiconTextFact("en", gloss)).ToArray())], msas ?? []);

    private static string[][] DuplicateGroups(string kind, params LexiconEntryFact[] entries) =>
        MeasureRunner.DecideDuplicateEntries(Measure("P-entry-duplicate"), new LexiconInput(entries), Context())
            .Findings.Where(finding => finding.AttachesTo.Identity.StartsWith($"entries/{kind}/", StringComparison.Ordinal))
            .Select(finding => finding.EvidenceRefs.Select(reference =>
                reference.Arguments.GetProperty("objectGuid").GetString()!.Replace("-lexeme", "")).ToArray()).ToArray();

    [Fact]
    public void FormsThatDifferOnlyByHyphensOrEqualsSignsAreOneForm()
    {
        var groups = DuplicateGroups("duplicate",
            Lexeme("a", "prefix", ["xy"], ["AV"], ["inflectional"]),
            Lexeme("b", "prefix", ["xy-"], ["AV"], ["inflectional"]),
            Lexeme("c", "prefix", ["=xy"], ["AV"], ["inflectional"]));

        Assert.Equal([["a", "b", "c"]], groups);
    }

    [Fact]
    public void DifferentMorphTypeClassesWritingSystemsGlossesOrMsasAreNotDuplicates()
    {
        var other = Lexeme("e", "suffix", ["xy"], ["AV"], ["inflectional"]) with
        {
            LexemeForms = [new LexiconTextFact("qab", "xy")],
        };

        Assert.Empty(DuplicateGroups("duplicate",
            Lexeme("a", "prefix", ["xy"], ["AV"], ["inflectional"]),
            Lexeme("b", "suffix", ["xy"], ["AV"], ["inflectional"]),
            Lexeme("c", "prefix", ["xy"], ["PV"], ["inflectional"]),
            Lexeme("d", "prefix", ["xy"], ["AV"], ["derivational"]),
            other with { EntryGuid = "f", MorphType = "prefix" }));
    }

    [Fact]
    public void StemRootAndBoundStemShareAClassAndPrefixAndSuffixDoNot()
    {
        var groups = DuplicateGroups("duplicate",
            Lexeme("a", "stem", ["xy"], ["go"], ["stem|"]), Lexeme("b", "root", ["xy"], ["go"], ["stem|"]),
            Lexeme("c", "boundStem", ["xy"], ["go"], ["stem|"]), Lexeme("d", "prefix", ["xy"], ["go"], ["stem|"]));

        Assert.Equal([["a", "b", "c"]], groups);
    }

    [Fact]
    public void PureVariantEntriesAndBlankEntriesNeverGroup()
    {
        Assert.Empty(DuplicateGroups("duplicate",
            Lexeme("a", "stem", ["xy"], []), Lexeme("b", "stem", ["xy"], []),
            Lexeme("c", "suffix", [], []),
            Lexeme("d", "suffix", [], [])));
    }

    [Fact]
    public void EntriesWithNoFormTextGroupByClassGlossAndMsa()
    {
        var groups = DuplicateGroups("duplicate",
            Lexeme("a", "suffix", [], ["REDUP"], ["derivational|"]), Lexeme("b", "suffix", [], ["REDUP"], ["derivational|"]),
            Lexeme("c", "suffix", [], ["OTHER"], ["derivational|"]));

        Assert.Equal([["a", "b"]], groups);
    }

    [Fact]
    public void ACloneNeedsEveryGlossToExtendAHomographGlossOrTheCapitalisedForm()
    {
        var clones = DuplicateGroups("clone",
            Lexeme("a", "suffix", ["ka"], ["go", "stay"]), Lexeme("b", "suffix", ["ka"], ["go.V", "stay.N"]),
            Lexeme("c", "suffix", ["ka"], ["go.V", "other"]), Lexeme("f", "suffix", ["ka"], ["ka.V"]),
            Lexeme("d", "suffix", ["pa"], ["KA.V"]),
            Lexeme("e", "suffix", ["xe"], ["XE.V"]), Lexeme("g", "suffix", ["xe"], ["other"]));

        var groups = clones.Select(group => string.Join("", group.Order())).Order().ToArray();
        Assert.Equal(["ab", "eg"], groups);
    }

    [Fact]
    public void AnExactGlossMatchOrAnEmptyDottedSuffixIsNotAClone()
    {
        Assert.Empty(DuplicateGroups("clone",
            Lexeme("a", "suffix", ["ka"], ["go"]), Lexeme("b", "suffix", ["ka"], ["go"]),
            Lexeme("c", "suffix", ["ka"], ["go."]), Lexeme("d", "suffix", ["ka"], [".go"])));
    }

    [Fact]
    public void ACloneChainFormsOneGroupAndCostsOneEntryPerExtraMember()
    {
        var result = MeasureRunner.DecideDuplicateEntries(Measure("P-entry-duplicate"),
            new LexiconInput([Lexeme("a", "suffix", ["ka"], ["go"]), Lexeme("b", "suffix", ["ka"], ["go.V"]),
                Lexeme("c", "suffix", ["ka"], ["go.V.PL"])]), Context());

        Assert.Single(result.Findings);
        Assert.Equal(2, result.Run.FindingItems);
    }

    [Fact]
    public void NoEntriesMeansNothingToCheck()
    {
        var result = MeasureRunner.DecideDuplicateEntries(Measure("P-entry-duplicate"), new LexiconInput([]), Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Empty(result.Findings);
    }
}
