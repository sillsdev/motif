using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>Each measure decides over a plain input record: no session, no SQLite file, no parser.</summary>
public sealed class MeasureDecisionTests
{
    private const string Bundle = "bundle-under-test";
    private const string AbstentionLead = "Abstention counts by reason: ";

    private static MeasureDefinition Measure(string id) => MeasureCatalog.Find(id)!;

    private static MeasureContext Context(IReadOnlyDictionary<string, string>? names = null) =>
        MeasureContext.For(Bundle, new MeasureDigestContext(1, 1, ParsimonyEvidenceScopeKind.ProjectApproved, null, null),
            names);

    [Fact]
    public void ACancellingContextStopsTheNextNameLookupAndLeavesLookupsAloneUntilThen()
    {
        using var source = new CancellationTokenSource();
        var context = Context(new Dictionary<string, string> { ["guid-1"] = "name one" }).Cancelling(source.Token);

        Assert.Equal("name one", context.Describe("guid-1"));
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => context.Describe("guid-1"));
    }

    private static AdhocProhibitionFact Prohibition(string guid, bool? loaded = true, bool disabled = false,
        string primary = "msa-p", string other = "msa-q", string adjacency = "anywhere") =>
        new(guid, "morpheme", disabled, adjacency, primary, "msa", [new AdhocTargetFact(other, "msa")], loaded);

    private static AllomorphMeasureFact Allomorph(string guid, string entry = "entry-1", string morphType = "suffix",
        string form = "ka", params string[] environments) =>
        new(guid, entry, 0, morphType, false,
            new Dictionary<string, IReadOnlyList<string>> { ["qaa"] = [form] },
            environments.Select((environment, index) => new AllomorphEnvironmentFact("phone", index, environment))
                .ToArray());

    // ---- P-adhoc-duplicate

    [Fact]
    public void IdenticalEnabledLoadedProhibitionsFormOneDuplicateFinding()
    {
        var result = MeasureRunner.DecideDuplicateProhibitions(Measure("P-adhoc-duplicate"),
            new DuplicateProhibitionInput([Prohibition("a"), Prohibition("b"), Prohibition("c", primary: "msa-x")],
                true), Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(3, result.Run.EligibleItems);
        Assert.Equal(1, result.Run.FindingItems);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("P-adhoc-duplicate:b", finding.FindingId);
        Assert.Equal(new ParsimonyMeasureNumber(1, 2, finding.Number.Unit), finding.Number);
        Assert.All(finding.EvidenceRefs, reference => Assert.Equal(Bundle, reference.BundleId));
        Assert.DoesNotContain("Grouped ad hoc provenance is unavailable in this facts artifact.", finding.Limitations);
    }

    [Fact]
    public void DisabledAndUnloadedProhibitionsAreNotCompared()
    {
        var result = MeasureRunner.DecideDuplicateProhibitions(Measure("P-adhoc-duplicate"),
            new DuplicateProhibitionInput(
                [Prohibition("a"), Prohibition("b", disabled: true), Prohibition("c", loaded: false)], false),
            Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(1, result.Run.EligibleItems);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void AProhibitionWithUnknownLoaderStateMakesTheRunInconclusiveAndSaysSo()
    {
        var result = MeasureRunner.DecideDuplicateProhibitions(Measure("P-adhoc-duplicate"),
            new DuplicateProhibitionInput([Prohibition("a"), Prohibition("b", loaded: null)], true), Context());

        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, result.Run.Status);
        Assert.Equal(1, result.Run.EligibleItems);
        Assert.Equal(AbstentionLead + "unknown-loader-state=1. Some enabled prohibitions have unknown loader state; " +
            "they were excluded from the comparison.", result.Run.Detail);
        Assert.Equal("the loader state of some enabled prohibitions is unknown, so they were excluded from the comparison.",
            result.NoteReason);
    }

    [Fact]
    public void MissingGroupedProvenanceIsNamedOnEveryDuplicateFinding()
    {
        var result = MeasureRunner.DecideDuplicateProhibitions(Measure("P-adhoc-duplicate"),
            new DuplicateProhibitionInput([Prohibition("a"), Prohibition("b")], false), Context());

        Assert.Contains("Grouped ad hoc provenance is unavailable in this facts artifact.",
            Assert.Single(result.Findings).Limitations);
    }

    // ---- P-allo-duplicate-form

    public static TheoryData<string, int, long, long> DuplicateAllomorphCases => new()
    {
        { "same entry, type, form and environments", 1, 2, 1 },
        { "different environments", 0, 2, 0 },
        { "different morph type", 0, 2, 0 },
        { "different entry", 0, 2, 0 },
        { "three identical", 1, 3, 2 },
    };

    [Theory]
    [MemberData(nameof(DuplicateAllomorphCases))]
    public void AllomorphsAreDuplicatesOnlyWhenEntryTypeFormAndEnvironmentsAllMatch(string name, int findings,
        long eligible, long redundant)
    {
        var allomorphs = name switch
        {
            "same entry, type, form and environments" => new[] { Allomorph("a", environments: "env-1"),
                Allomorph("b", environments: "env-1") },
            "different environments" => new[] { Allomorph("a", environments: "env-1"),
                Allomorph("b", environments: "env-2") },
            "different morph type" => new[] { Allomorph("a"), Allomorph("b", morphType: "prefix") },
            "different entry" => new[] { Allomorph("a"), Allomorph("b", entry: "entry-2") },
            _ => new[] { Allomorph("a"), Allomorph("b"), Allomorph("c") },
        };

        var result = MeasureRunner.DecideDuplicateAllomorphs(Measure("P-allo-duplicate-form"), allomorphs, Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(eligible, result.Run.EligibleItems);
        Assert.Equal(redundant, result.Run.FindingItems);
        Assert.Equal(findings, result.Findings.Count);
    }

    [Fact]
    public void DuplicateAllomorphFindingIdSurvivesAFormEditButNotAMemberChange()
    {
        var measure = Measure("P-allo-duplicate-form");
        string Id(params AllomorphMeasureFact[] members) => Assert.Single(
            MeasureRunner.DecideDuplicateAllomorphs(measure, members, Context()).Findings).FindingId;

        var original = Id(Allomorph("a"), Allomorph("b"));
        Assert.Equal(original, Id(Allomorph("a", form: "xa"), Allomorph("b", form: "xa")));
        Assert.NotEqual(original, Id(Allomorph("a"), Allomorph("c")));
    }

    // ---- P-statement-unused

    private static ParsimonyStatementUsageViewRow Statement(string kind, string guid, string classification) =>
        new(kind, guid, "name-" + guid, 0, null, null, null, classification);

    [Fact]
    public void OnlyUnreferencedAndUnappliedStatementsAreFindingsAndEachNamesItsReferrers()
    {
        var statements = new[]
        {
            Statement("environment", "env-unused", "defined-but-unreferenced"),
            Statement("environment", "env-ignored", "referenced-but-not-applied"),
            Statement("natural-class", "class-used", "referenced-and-applied"),
        };
        var referrers = new Dictionary<(string, string), IReadOnlyList<(string, string, string)>>
        {
            [("environment", "env-unused")] = [],
            [("environment", "env-ignored")] = [("msa", "msa-1", "ignored"), ("adhocProhibition", "adhoc-9", "unresolved")],
        };

        var result = MeasureRunner.DecideUnusedStatements(Measure("P-statement-unused"),
            new StatementUsageInput(statements, referrers),
            Context(new Dictionary<string, string> { ["msa-1"] = "-ka" }));

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(3, result.Run.EligibleItems);
        Assert.Equal(2, result.Run.FindingItems);
        var unused = Assert.Single(result.Findings, item => item.FindingId.EndsWith(":env-unused"));
        Assert.Contains("No grammar object references this statement.", unused.Limitations);
        var ignored = Assert.Single(result.Findings, item => item.FindingId.EndsWith(":env-ignored"));
        Assert.Contains("Referenced only by grammatical info \"-ka\" (ignored by the parser), " +
            "ad hoc prohibition adhoc-9 (its reference could not be resolved); none of these applies it to parsing.",
            ignored.Limitations);
    }

    // ---- R-word-disapproved-produced and R-word-negative-accepted

    [Fact]
    public void WithoutAnAssessmentTheParserOverlayMeasuresAreNotAvailable()
    {
        var disapproved = MeasureRunner.DecideDisapprovedProduced(Measure("R-word-disapproved-produced"),
            new ParserOverlayMeasureFacts(false, false, 0, 0, 0, 0, ParsimonyNotes.NoAssessmentReason, []), Context());
        var negatives = MeasureRunner.DecideReviewedNegativeAccepted(Measure("R-word-negative-accepted"),
            new ParserReviewedNegativeMeasureFacts(false, false, 0, 0, 0, ParsimonyNotes.NoAssessmentReason, []),
            Context());

        Assert.All(new[] { disapproved, negatives }, result =>
        {
            Assert.Equal(ParsimonyMeasureStatus.NotAvailable, result.Run.Status);
            Assert.Equal(ParsimonyNotes.NoAssessmentReason, result.NoteReason);
        });
    }

    [Fact]
    public void ADisapprovedReadingTheParserProducedIsOneFindingAndIncompleteEvidenceAbstains()
    {
        var match = new ParserDisapprovedMatchFact("word-1", "sig-1", "assessment-1", "case-1", 0, "[\"analysis-1\"]");
        var clean = MeasureRunner.DecideDisapprovedProduced(Measure("R-word-disapproved-produced"),
            new ParserOverlayMeasureFacts(true, true, 4, 1, 1, 0, null, [match]), Context());
        var partial = MeasureRunner.DecideDisapprovedProduced(Measure("R-word-disapproved-produced"),
            new ParserOverlayMeasureFacts(true, true, 4, 1, 1, 2,
                "parser evidence for 2 item(s) is incomplete or cannot be attributed, so those items do not count as passes.",
                [match]), Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, clean.Run.Status);
        Assert.Equal("R-word-disapproved-produced:word-1:sig-1", Assert.Single(clean.Findings).FindingId);
        Assert.Null(clean.Run.Detail);
        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, partial.Run.Status);
        Assert.Equal(AbstentionLead + "incomplete-parser-evidence=2. Parser evidence for 2 item(s) is incomplete or " +
            "cannot be attributed, so those items do not count as passes.", partial.Run.Detail);
        Assert.Single(partial.Findings);
    }

    [Fact]
    public void ReviewedNegativesWithoutACompletedCaseAbstainWithTheirCount()
    {
        var detail = "the parser case for 3 reviewed negative(s) is not completed and exact, so they were not counted.";
        var result = MeasureRunner.DecideReviewedNegativeAccepted(Measure("R-word-negative-accepted"),
            new ParserReviewedNegativeMeasureFacts(true, true, 5, 0, 3, detail, []), Context());

        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, result.Run.Status);
        Assert.Equal(AbstentionLead + "incomplete-parser-case=3. The parser case for 3 reviewed negative(s) is not " +
            "completed and exact, so they were not counted.", result.Run.Detail);
        Assert.Equal(detail, result.NoteReason);
    }

    // ---- R-allo-unconditioned

    private static UnconditionedAllomorphFamily Family(params (string Guid, int Order, bool Final)[] siblings) =>
        new("family-1", "entry-1", "msa-1", "Morphology", siblings.Select(item => new UnconditionedAllomorphSibling(
            Allomorph(item.Guid), item.Order, item.Final, UnconditionedAllomorphPhoneConditionState.Unconditioned,
            [])).ToArray());

    [Fact]
    public void AnUnconditionedAllomorphBeforeTheFinalElsewhereCaseIsAFinding()
    {
        var result = MeasureRunner.DecideUnconditionedAllomorphs(Measure("R-allo-unconditioned"),
            new UnconditionedAllomorphDiscovery([Family(("a", 0, false), ("b", 1, true))], 2, 0, 0), Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(2, result.Run.EligibleItems);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(new ParsimonyMeasureNumber(1, 2, finding.Number.Unit), finding.Number);
        Assert.Contains("Compiler order in the Morphology bucket (0 to 1): ", finding.Limitations[0]);
    }

    [Fact]
    public void FamiliesWithIncompleteFactsAbstainAndInvalidRestrictionsAreCounted()
    {
        var measure = Measure("R-allo-unconditioned");
        var invalidOnly = MeasureRunner.DecideUnconditionedAllomorphs(measure,
            new UnconditionedAllomorphDiscovery([], 0, 0, 2), Context());
        var unknown = MeasureRunner.DecideUnconditionedAllomorphs(measure,
            new UnconditionedAllomorphDiscovery([], 0, 3, 2), Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, invalidOnly.Run.Status);
        Assert.StartsWith("Excluded 2 invalid phone restriction(s)", invalidOnly.Run.Detail);
        Assert.DoesNotContain("Abstention counts", invalidOnly.Run.Detail);
        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, unknown.Run.Status);
        Assert.StartsWith(AbstentionLead + "incomplete-family-facts=3, invalid-phone-restriction=2. ",
            unknown.Run.Detail);
        Assert.Contains("Excluded 3 family/families with incomplete condition or compiler-order facts.",
            unknown.Run.Detail);
        Assert.Equal("the condition or compiler-order facts for some allomorph families are incomplete, " +
            "so they were not compared.", unknown.NoteReason);
    }

    // ---- B-affix-unslotted and B-affix-null-vs-optional

    private static ParsimonyUnslottedAffixViewRow Unslotted(string msa, params string[] exclusions) =>
        new(msa, "entry-1", "inflectional MSA for -ka", ["cat"], [], 2, ["analysis-1"],
            [new ParsimonyAffixPositionCount("suffix", 1, 2, 2, 2)], [], exclusions);

    [Fact]
    public void EachUnslottedAffixIsOneFindingOutOfTheLoadedInflectionalAffixes()
    {
        var result = MeasureRunner.DecideUnslottedAffixes(Measure("B-affix-unslotted"),
            new UnslottedAffixInput([Unslotted("msa-2", "One Approved analysis repeats this MSA."), Unslotted("msa-1")], 5),
            Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(5, result.Run.EligibleItems);
        Assert.Equal(2, result.Run.FindingItems);
        Assert.Equal(new[] { "B-affix-unslotted:msa-1", "B-affix-unslotted:msa-2" },
            result.Findings.Select(item => item.FindingId));
        Assert.All(result.Findings, item => Assert.Equal(5, item.Number.Denominator));
        Assert.Contains("One Approved analysis repeats this MSA.", result.Findings[1].Limitations);
        Assert.StartsWith("No affix slot is assigned; rooted Approved positions were suffix +1 (2 analyses, ",
            result.Findings[0].Limitations[0]);
    }

    private static ParsimonyNullOptionalViewRow NullOptional(string msa, bool loadedZeroOnly, string classification) =>
        new(msa, "entry-1", "zero suffix", classification, [], [], [], [], [], [], [], [], [], 0,
            loadedZeroOnly, false, []);

    [Fact]
    public void OnlyLoadedZeroOnlyAffixesAreEligibleAndTheirClassificationPicksTheSentence()
    {
        var result = MeasureRunner.DecideNullOptionalAffixes(Measure("B-affix-null-vs-optional"),
        [
            NullOptional("msa-1", true, "optional-slot-candidate"),
            NullOptional("msa-2", true, "obligatory-slot-alternative"),
            NullOptional("msa-3", false, "optional-slot-candidate"),
        ], Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(2, result.Run.EligibleItems);
        Assert.Equal(2, result.Run.FindingItems);
        Assert.Contains("The zero is loaded in an optional slot; inspect its contributions and references before any cleanup.",
            result.Findings[0].Limitations);
        Assert.Contains("A zero in a required slot represents an absence alternative, not optionality.",
            result.Findings[1].Limitations);
    }

    // ---- R-tmpl-precedence and R-slot-blocking

    private const string Category = "cat-1";
    private const string Template = "template-1";

    private static TemplateMeasureSlot Slot(string guid, int order, bool? optional, params string[] msas) =>
        new(guid, "slot-" + guid, "suffix", order, order, optional, msas.ToHashSet(StringComparer.Ordinal));

    private static ParsimonyApprovedMorph Morph(int ordinal, string? msa, string kind, string? type) =>
        new(ordinal, "morph-" + ordinal, msa, "entry-" + ordinal, type, kind,
            new Dictionary<string, string> { ["qaa"] = "m" + ordinal }, new Dictionary<string, string>());

    private static TemplateMeasureAnalysis Analysis(string guid, params ParsimonyApprovedMorph[] morphs) =>
        new(new ParsimonyApprovedMorphSequenceViewRow(guid, "word-" + guid, "word", morphs), [Category]);

    private static ParsimonyApprovedMorph Stem(int ordinal = 0) => Morph(ordinal, "msa-stem", "stem", "stem");

    private static ParsimonyApprovedMorph Suffix(int ordinal, string msa) =>
        Morph(ordinal, msa, "inflectional", "suffix");

    private static TemplateSequenceMeasureFacts Facts(IReadOnlyList<TemplateMeasureSlot> slots,
        params TemplateMeasureAnalysis[] analyses) =>
        new([new TemplateMeasure(Template, Category, "template", false, slots)],
            new Dictionary<string, string?> { [Category] = null }, new Dictionary<string, IReadOnlySet<string>>(),
            new Dictionary<string, string> { ["msa-1"] = "inflectional", ["msa-2"] = "inflectional" },
            new Dictionary<string, IReadOnlyList<string>>(), analyses);

    private static readonly TemplateMeasureSlot[] OrderedSlots =
        [Slot("slot-1", 0, true, "msa-1"), Slot("slot-2", 1, true, "msa-2")];

    [Fact]
    public void AnApprovedSequenceInTheOppositeOrderOfTheTemplateIsOneFindingOnTheTemplate()
    {
        var facts = Facts(OrderedSlots,
            Analysis("in-order", Stem(), Suffix(1, "msa-1"), Suffix(2, "msa-2")),
            Analysis("reversed", Stem(), Suffix(1, "msa-2"), Suffix(2, "msa-1")));

        var result = MeasureRunner.DecideTemplatePrecedence(Measure("R-tmpl-precedence"), facts, Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(2, result.Run.EligibleItems);
        Assert.Equal(1, result.Run.FindingItems);
        var finding = Assert.Single(result.Findings);
        Assert.Equal($"R-tmpl-precedence:{Template}:reversed", finding.FindingId);
        Assert.Equal(Template, finding.AttachesTo.Identity);
        Assert.Contains("Pair-order variability: ", result.Run.Detail);
    }

    public static TheoryData<string, string, string> TemplateAbstentionCases => new()
    {
        { "two roots", "unsupported-analysis-shape=1. Abstained for 1 Approved analysis case(s) with unsupported roots, " +
            "morph types, or category ancestry.",
          "some Approved analysis cases could not be checked, because their roots, morph types, or category ancestry " +
            "are unsupported." },
        { "unknown category", "unsupported-analysis-shape=1. Abstained for 1 Approved analysis case(s) with unsupported roots, " +
            "morph types, or category ancestry.",
          "some Approved analysis cases could not be checked, because their roots, morph types, or category ancestry " +
            "are unsupported." },
    };

    [Theory]
    [MemberData(nameof(TemplateAbstentionCases))]
    public void ACaseTheTemplateMeasuresCannotCheckIsCountedByReason(string name, string detailTail, string note)
    {
        var analysis = name == "two roots"
            ? Analysis("odd", Stem(), Stem(1), Suffix(2, "msa-1"), Suffix(3, "msa-2"))
            : Analysis("odd", Stem(), Suffix(1, "msa-1"), Suffix(2, "msa-2")) with { RootCategoryGuids = ["cat-unknown"] };
        var facts = Facts(OrderedSlots, analysis);

        var precedence = MeasureRunner.DecideTemplatePrecedence(Measure("R-tmpl-precedence"), facts, Context());
        var blocking = MeasureRunner.DecideSlotBlocking(Measure("R-slot-blocking"), facts, Context());

        foreach (var result in new[] { precedence, blocking })
        {
            Assert.Equal(ParsimonyMeasureStatus.Inconclusive, result.Run.Status);
            Assert.Contains(AbstentionLead + detailTail, result.Run.Detail);
            Assert.Equal(note, result.NoteReason);
        }
    }

    [Fact]
    public void ARequiredSlotLeftEmptyByEveryCompatibleTemplateBlocksTheForm()
    {
        var slots = new[]
        {
            Slot("slot-1", 0, true, "msa-1"),
            Slot("slot-required", 1, false, "msa-3"),
        };
        var facts = Facts(slots, Analysis("lacks-required", Stem(), Suffix(1, "msa-1")));

        var result = MeasureRunner.DecideSlotBlocking(Measure("R-slot-blocking"), facts, Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(1, result.Run.EligibleItems);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("R-slot-blocking:slot-required", finding.FindingId);
        Assert.Equal(ParsimonyVerification.Inconclusive, finding.Verification);
        Assert.Equal(new ParsimonyMeasureNumber(1, 1, finding.Number.Unit), finding.Number);
    }

    [Fact]
    public void ASlotWhoseOptionalityIsUnknownAbstainsWithItsOwnReason()
    {
        var slots = new[]
        {
            Slot("slot-1", 0, true, "msa-1"),
            Slot("slot-unknown", 1, null, "msa-3"),
        };
        var facts = Facts(slots, Analysis("unknown", Stem(), Suffix(1, "msa-1")));

        var result = MeasureRunner.DecideSlotBlocking(Measure("R-slot-blocking"), facts, Context());

        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, result.Run.Status);
        Assert.Empty(result.Findings);
        Assert.Contains(AbstentionLead + "unknown-slot-optionality=1. Obligation is unknown for 1 order-compatible " +
            "case(s) with an unresolved slot optionality fact.", result.Run.Detail);
        Assert.Equal("the slot optionality of some order-compatible cases is unknown.", result.NoteReason);
    }

    // ---- B-adhoc-is-slot-order

    private static AdhocSlotOrderInput SlotOrderInput(IReadOnlyList<AdhocProhibitionFact> rules,
        bool grouped = true) =>
        new(rules, new TemplateSequenceMeasureFacts(
                [new TemplateMeasure(Template, Category, "template", false,
                    [Slot("slot-primary", 0, true, "msa-p"), Slot("slot-other", 1, true, "msa-q")])],
                new Dictionary<string, string?> { [Category] = null }, new Dictionary<string, IReadOnlySet<string>>(),
                new Dictionary<string, string> { ["msa-p"] = "inflectional", ["msa-q"] = "inflectional" },
                new Dictionary<string, IReadOnlyList<string>>
                    { ["msa-p"] = [Category], ["msa-q"] = [Category] }, []),
            new HashSet<string>(["msa-p", "msa-q"], StringComparer.Ordinal), new Dictionary<string, string>(), grouped);

    [Fact]
    public void ARuleThatTemplateOrderAlreadyEntailsIsOneCleanupCandidate()
    {
        var result = MeasureRunner.DecideAdhocSlotOrder(Measure("B-adhoc-is-slot-order"),
            SlotOrderInput([Prohibition("rule-1", adjacency: "somewhereToLeft")]), Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(1, result.Run.EligibleItems);
        Assert.Equal(1, result.Run.FindingItems);
        var finding = Assert.Single(result.Findings);
        Assert.Contains("One already-entailed prohibition is an individual cleanup candidate.", finding.Limitations);
        Assert.Contains("every applicable placement excludes the prohibited direction", result.Run.Detail);
    }

    [Fact]
    public void ADisabledRuleIsIneligibleAndNoRulesIsSaidInTheDetail()
    {
        var disabled = MeasureRunner.DecideAdhocSlotOrder(Measure("B-adhoc-is-slot-order"),
            SlotOrderInput([Prohibition("rule-1", disabled: true)]), Context());
        var none = MeasureRunner.DecideAdhocSlotOrder(Measure("B-adhoc-is-slot-order"), SlotOrderInput([], false),
            Context());

        Assert.Equal(0, disabled.Run.EligibleItems);
        Assert.Contains("disabled prohibition is outside the measured catalog", disabled.Run.Detail);
        Assert.Contains("No ad hoc prohibitions are present in this facts artifact.", none.Run.Detail);
        Assert.Contains("Grouped ad hoc provenance is unavailable", none.Run.Detail);
    }

    [Fact]
    public void ARuleWithUnknownLoaderStateAbstainsWithItsOwnReason()
    {
        var result = MeasureRunner.DecideAdhocSlotOrder(Measure("B-adhoc-is-slot-order"),
            SlotOrderInput([Prohibition("rule-1", loaded: null), Prohibition("rule-2", adjacency: "somewhereToLeft")]),
            Context());

        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, result.Run.Status);
        Assert.StartsWith(AbstentionLead + "unknown-loader-state=1. The final loader state of some ad hoc " +
            "prohibitions is unknown, so their slot order was not checked.", result.Run.Detail);
        Assert.Equal("the loader state of some ad hoc prohibitions is unknown, so their slot order was not checked.",
            result.NoteReason);
        Assert.Single(result.Findings);
    }

    // ---- R-env-broad and R-nc-excess

    private static EnvironmentExcessSite EnvironmentSite(string allomorph = "allomorph-1", int wordTypes = 3,
        int stems = 2, string[]? missing = null) =>
        new(allomorph, "owner-1", "msa-1", "Morphology", ["env-1"], ["env one"], "left", ["a", "b", "c"], ["a", "b", "c"],
            ["a"], ["b", "c"], missing ?? [], 2, wordTypes, stems, ["word"], [],
            new Dictionary<string, string> { ["a"] = "a", ["b"] = "b", ["c"] = "c" }, [], [], [], []);

    [Fact]
    public void ABroadEnvironmentWithEnoughSupportIsAFindingAndASparseOneIsOnlyCounted()
    {
        var measure = Measure("R-env-broad");
        var supported = MeasureRunner.DecideEnvironmentExcess(measure,
            new EnvironmentExcessDiscovery([EnvironmentSite()], 0, 0), Context());
        var sparse = MeasureRunner.DecideEnvironmentExcess(measure,
            new EnvironmentExcessDiscovery([EnvironmentSite(wordTypes: 2)], 0, 0), Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, supported.Run.Status);
        Assert.Single(supported.Findings);
        Assert.Equal(ParsimonyMeasureStatus.Computed, sparse.Run.Status);
        Assert.Empty(sparse.Findings);
        Assert.Equal("Abstained on 0 unsupported site(s), 0 ambiguous alignment(s), and 0 site(s) that miss attested " +
            "triggers; 1 site(s) did not meet the support floor.", sparse.Run.Detail);
    }

    [Fact]
    public void EnvironmentSitesThatCannotBeJudgedAreCountedByReason()
    {
        var result = MeasureRunner.DecideEnvironmentExcess(Measure("R-env-broad"),
            new EnvironmentExcessDiscovery([EnvironmentSite(missing: ["z"]), EnvironmentSite("allomorph-2", 1, 1)], 4, 2),
            Context());

        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, result.Run.Status);
        Assert.Equal(AbstentionLead + "ambiguous-alignment=2, below-support-floor=1, missing-attested-trigger=1, " +
            "unsupported-site=4. 4 unsupported site(s) have an environment shape this check does not measure. 2 ambiguous alignment(s) of a " +
            "morph to its word were set aside. 1 site(s) miss attested triggers. 1 site(s) did not meet the support floor.",
            result.Run.Detail);
        Assert.Equal("some sites have unsupported shapes, some sites have ambiguous alignments, and some sites miss " +
            "attested triggers, so they were not checked.", result.NoteReason);
    }

    [Fact]
    public void ANaturalClassMeasureNamesOnlyTheReasonsThatArose()
    {
        var measure = Measure("R-nc-excess");
        var clean = MeasureRunner.DecideNaturalClassExcess(measure, new NaturalClassExcessDiscovery([], 0, 0), Context());
        var ambiguous = MeasureRunner.DecideNaturalClassExcess(measure,
            new NaturalClassExcessDiscovery([], 0, 3), Context());
        var both = MeasureRunner.DecideNaturalClassExcess(measure, new NaturalClassExcessDiscovery([], 2, 3), Context());

        Assert.Equal(ParsimonyMeasureStatus.Computed, clean.Run.Status);
        Assert.Null(clean.Run.Detail);
        Assert.Equal(AbstentionLead + "ambiguous-alignment=3. 3 ambiguous alignment(s) of a morph to its word were set aside.",
            ambiguous.Run.Detail);
        Assert.Equal("some usage sites have ambiguous alignments, so they were not checked.", ambiguous.NoteReason);
        Assert.StartsWith(AbstentionLead + "ambiguous-alignment=3, unsupported-usage-site=2. ", both.Run.Detail);
        Assert.Equal("some usage sites have unsupported shapes and some usage sites have ambiguous alignments, " +
            "so they were not checked.", both.NoteReason);
    }

    // ---- the contract every Inconclusive run keeps

    [Fact]
    public void EveryInconclusiveRunStatesItsAbstentionCountsAndHasANote()
    {
        var results = new[]
        {
            MeasureRunner.DecideDuplicateProhibitions(Measure("P-adhoc-duplicate"),
                new DuplicateProhibitionInput([Prohibition("a", loaded: null)], true), Context()),
            MeasureRunner.DecideUnconditionedAllomorphs(Measure("R-allo-unconditioned"),
                new UnconditionedAllomorphDiscovery([], 0, 1, 0), Context()),
            MeasureRunner.DecideTemplatePrecedence(Measure("R-tmpl-precedence"),
                Facts(OrderedSlots, Analysis("odd", Stem(), Stem(1), Suffix(2, "msa-1"))), Context()),
            MeasureRunner.DecideSlotBlocking(Measure("R-slot-blocking"),
                Facts(OrderedSlots, Analysis("odd", Stem(), Stem(1), Suffix(2, "msa-1"))), Context()),
            MeasureRunner.DecideEnvironmentExcess(Measure("R-env-broad"),
                new EnvironmentExcessDiscovery([], 1, 0), Context()),
            MeasureRunner.DecideNaturalClassExcess(Measure("R-nc-excess"),
                new NaturalClassExcessDiscovery([], 1, 0), Context()),
            MeasureRunner.DecideAdhocSlotOrder(Measure("B-adhoc-is-slot-order"),
                SlotOrderInput([Prohibition("rule-1", loaded: null)]), Context()),
            MeasureRunner.DecideDisapprovedProduced(Measure("R-word-disapproved-produced"),
                new ParserOverlayMeasureFacts(true, true, 1, 0, 0, 1, "parser evidence for 1 item(s) is incomplete.", []),
                Context()),
            MeasureRunner.DecideReviewedNegativeAccepted(Measure("R-word-negative-accepted"),
                new ParserReviewedNegativeMeasureFacts(true, true, 1, 0, 1, "one case is incomplete.", []), Context()),
        };

        Assert.All(results, result =>
        {
            Assert.Equal(ParsimonyMeasureStatus.Inconclusive, result.Run.Status);
            Assert.Contains(AbstentionLead, result.Run.Detail);
            Assert.False(string.IsNullOrWhiteSpace(result.NoteReason));
            Assert.Matches("^[a-z].*\\.$", result.NoteReason);
        });
    }
}
