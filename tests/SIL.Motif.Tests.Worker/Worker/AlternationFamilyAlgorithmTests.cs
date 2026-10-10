using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class AlternationFamilyAlgorithmTests
{
    [Fact]
    public void TokenizerUsesPhonemeMappingsAndDoesNotGuessUnknownCharacters()
    {
        var mappings = new[]
        {
            new PhonemeGraphemeFact("phoneme-a", "qaa", "a"),
            new PhonemeGraphemeFact("phoneme-b", "qaa", "b"),
        };

        var known = PhonemeTokenizer.Tokenize("ab", "qaa", mappings);
        var unknown = PhonemeTokenizer.Tokenize("ac", "qaa", mappings);

        Assert.Equal(PhonemeTokenizationStatus.Unique, known.Status);
        Assert.Equal(new[] { "phoneme-a", "phoneme-b" }, known.PhonemeGuids);
        Assert.Equal(PhonemeTokenizationStatus.Unmapped, unknown.Status);
    }

    [Theory]
    [InlineData("^0")]
    [InlineData("*0")]
    [InlineData("&0")]
    [InlineData("∅")]
    public void TokenizerTreatsNullAllomorphMarkersAsEmptyForms(string form)
    {
        var result = PhonemeTokenizer.Tokenize(form, "qaa", []);

        Assert.Equal(PhonemeTokenizationStatus.Unique, result.Status);
        Assert.Empty(result.PhonemeGuids);
        Assert.Equal(1, result.SegmentationCount);
    }

    [Fact]
    public void TokenizerAbstainsWhenSeveralSegmentationsRemainOrTheBoundIsExceeded()
    {
        var ambiguousMappings = new[]
        {
            new PhonemeGraphemeFact("phoneme-a", "qaa", "a"),
            new PhonemeGraphemeFact("phoneme-b", "qaa", "b"),
            new PhonemeGraphemeFact("phoneme-ab", "qaa", "ab"),
        };
        var boundedMappings = new[]
        {
            new PhonemeGraphemeFact("phoneme-a", "qaa", "a"),
            new PhonemeGraphemeFact("phoneme-aa", "qaa", "aa"),
            new PhonemeGraphemeFact("phoneme-aaa", "qaa", "aaa"),
        };

        var ambiguous = PhonemeTokenizer.Tokenize("ab", "qaa", ambiguousMappings);
        var overBound = PhonemeTokenizer.Tokenize("aaaaaaaa", "qaa", boundedMappings);

        Assert.Equal(PhonemeTokenizationStatus.Ambiguous, ambiguous.Status);
        Assert.Equal(2, ambiguous.SegmentationCount);
        Assert.Equal(PhonemeTokenizationStatus.OverBound, overBound.Status);
        Assert.Equal(17, overBound.SegmentationCount);
    }

    [Fact]
    public void AlignerUsesFeatureWeightedSubstitutionAndTwoUnitIndels()
    {
        var features = new Dictionary<string, PhonemeFeatureVector>(StringComparer.Ordinal)
        {
            ["n"] = Vector(("nasal", "yes"), ("place", "alveolar")),
            ["m"] = Vector(("nasal", "yes"), ("place", "bilabial")),
            ["a"] = Vector(("nasal", "no"), ("place", "vowel")),
        };

        var substitution = PhonemeAligner.Align(["n"], ["m"], features, 2);
        var insertion = PhonemeAligner.Align(["n"], ["n", "a"], features, 2);

        Assert.Equal(PhonemeAlignmentStatus.Unique, substitution.Status);
        Assert.Equal(3, substitution.CostNumerator);
        Assert.Equal(2, substitution.CostDenominator);
        Assert.Equal(PhonemeEditKind.Substitution, Assert.Single(substitution.Steps).Kind);
        Assert.Equal(PhonemeAlignmentStatus.Unique, insertion.Status);
        Assert.Equal(4, insertion.CostNumerator);
        Assert.Equal(2, insertion.CostDenominator);
        Assert.Equal(PhonemeEditKind.Insertion, insertion.SingleEdit!.Kind);
    }

    [Fact]
    public void AlignerRetainsTiesAndWithholdsRewriteEvidenceWhenFeaturesAreMissing()
    {
        var incomplete = new Dictionary<string, PhonemeFeatureVector>(StringComparer.Ordinal)
        {
            ["n"] = Vector(("nasal", "yes")),
            ["m"] = Vector(("nasal", "yes"), ("place", "bilabial")),
            ["a"] = Vector(("nasal", "no"), ("place", "vowel")),
        };
        var complete = new Dictionary<string, PhonemeFeatureVector>(StringComparer.Ordinal)
        {
            ["a"] = Vector(("nasal", "no"), ("place", "vowel")),
        };

        var tied = PhonemeAligner.Align(["a", "a"], ["a"], complete, 2);
        var unavailable = PhonemeAligner.Align(["n"], ["m"], incomplete, 2);

        Assert.Equal(PhonemeAlignmentStatus.Tied, tied.Status);
        Assert.Equal(4, tied.CostNumerator);
        Assert.Equal(PhonemeAlignmentStatus.RewriteUnavailable, unavailable.Status);
    }

    [Fact]
    public void AlignerOffersARewriteOnlyForOneUniqueSingleSegmentEdit()
    {
        var features = new Dictionary<string, PhonemeFeatureVector>(StringComparer.Ordinal)
        {
            ["a"] = Vector(("nasal", "no"), ("place", "vowel")),
            ["n"] = Vector(("nasal", "yes"), ("place", "alveolar")),
            ["m"] = Vector(("nasal", "yes"), ("place", "bilabial")),
            ["p"] = Vector(("nasal", "no"), ("place", "bilabial")),
        };

        var oneEdit = PhonemeAligner.Align(["a", "n"], ["a", "m"], features, 2);
        var severalEdits = PhonemeAligner.Align(["a", "n"], ["m", "p"], features, 2);

        var edit = Assert.IsType<PhonemeAlignmentStep>(oneEdit.SingleEdit);
        Assert.Equal("n", edit.InputPhonemeGuid);
        Assert.Equal("m", edit.OutputPhonemeGuid);
        Assert.Null(severalEdits.SingleEdit);
    }

    [Fact]
    public void OppositeUndirectedInsertionAndDeletionDirectionsDoNotFormOneFamily()
    {
        var phonemes = new[]
        {
            new PhonemeFacts("a", "a", [new PhonemeGraphemeFact("a", "qaa", "a")],
                Vector(("class", "vowel"))),
            new PhonemeFacts("n", "n", [new PhonemeGraphemeFact("n", "qaa", "n")],
                Vector(("class", "nasal"))),
        };
        var knownConditions = new AlternationConditionFacts(true, false, false, []);
        var entries = new[]
        {
            Entry("entry-insertion-one", "a-insertion-one", "a", "z-insertion-one", "na"),
            Entry("entry-insertion-two", "a-insertion-two", "a", "z-insertion-two", "na"),
            Entry("entry-deletion-one", "a-deletion-one", "na", "z-deletion-one", "a"),
            Entry("entry-deletion-two", "a-deletion-two", "na", "z-deletion-two", "a"),
        };
        var result = AlternationDiscovery.Analyze(new AlternationInput(1, phonemes, entries, [], true));

        Assert.Equal(2, result.Families.Count);
        Assert.All(result.Families, family => Assert.Equal(2, family.Members.Count));

        AlternationEntryFacts Entry(string guid, string firstGuid, string firstForm,
            string secondGuid, string secondForm) => new(guid, guid, ["shared-gate"],
            [Allomorph(firstGuid, firstForm), Allomorph(secondGuid, secondForm)]);

        AlternationAllomorphFacts Allomorph(string guid, string form) => new(guid, "prefix",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["qaa"] = [form],
            }, knownConditions, NoGate);
    }

    [Fact]
    public void DiscoveryKeepsUnconditionedVariationUndirectedAndSeparatesMorphologicalGates()
    {
        var entries = new[]
        {
            IdentityEntry("entry-one", "shared-gate"),
            IdentityEntry("entry-two", "shared-gate"),
            IdentityEntry("entry-other-gate", "other-gate"),
        };

        var result = AlternationDiscovery.Analyze(new AlternationInput(0, IdentityPhonemes(), entries, [], true));

        var family = Assert.Single(result.Families);
        Assert.False(family.Directed);
        Assert.Equal("condition:unspecified", family.ContextKey);
        Assert.Equal("shared-gate", family.GateKey);
        Assert.Equal(2, family.Members.Count);
        Assert.DoesNotContain(family.Members, member => member.EntryGuid == "entry-other-gate");
    }

    [Fact]
    public void ANonExactRuleWithAnUnrelatedSoundChangeDoesNotMakeTheFamilyInconclusive()
    {
        var unrelatedRule = new ExistingRewriteRule("phoneme-r", "phoneme-t", false, true);
        var unresolvedRuleWithUnrelatedInput = new ExistingRewriteRule("phoneme-r", null, false, true);
        var result = AlternationDiscovery.Analyze(new AlternationInput(0, IdentityPhonemes(),
            [IdentityEntry("entry-one", "shared-gate"), IdentityEntry("entry-two", "shared-gate")],
            [unrelatedRule, unresolvedRuleWithUnrelatedInput], true));

        Assert.False(result.Inconclusive);
        Assert.Single(result.Families);
    }

    [Fact]
    public void DiscoveryDirectsAnAlternationWhenOneFormHasAResolvedPhonologicalCondition()
    {
        var unconditioned = new AlternationConditionFacts(true, false, false, []);
        var conditioned = new AlternationConditionFacts(true, true, false,
            [new ResolvedEnvironmentFacts("environment-before-p", "before p")]);
        var entries = new[]
        {
            DirectedEntry("entry-one"),
            DirectedEntry("entry-two"),
        };

        var result = AlternationDiscovery.Analyze(new AlternationInput(0, IdentityPhonemes(), entries, [], true));

        var family = Assert.Single(result.Families);
        Assert.True(family.Directed);
        Assert.Equal("before p", family.ContextDescription);
        Assert.Equal(["phoneme-n"], family.InputPhonemeGuids);
        Assert.Equal(["phoneme-m"], family.OutputPhonemeGuids);

        AlternationEntryFacts DirectedEntry(string guid) => new(guid, guid, ["shared-gate"],
        [
            IdentityAllomorph(guid + "-n", "n", unconditioned),
            IdentityAllomorph(guid + "-m", "m", conditioned),
        ]);
    }

    [Fact]
    public void FamilyDigestIsStableWhenFactsArriveInDifferentOrders()
    {
        var phonemes = IdentityPhonemes();
        var entries = new[]
        {
            IdentityEntry("entry-one", "shared-gate"),
            IdentityEntry("entry-two", "shared-gate"),
        };
        var forward = AlternationDiscovery.Analyze(new AlternationInput(0, phonemes, entries, [], true));
        var reversed = AlternationDiscovery.Analyze(new AlternationInput(0, phonemes.Reverse().ToArray(),
            entries.Reverse().Select(entry => entry with
            {
                Allomorphs = entry.Allomorphs.Reverse().ToArray(),
            }).ToArray(), [], true));

        var forwardFamily = Assert.Single(forward.Families);
        var reversedFamily = Assert.Single(reversed.Families);
        Assert.Equal(forwardFamily.Key, reversedFamily.Key);
        Assert.Equal("alternation-family/f98f891709eea11ef8e952c40b41ce2ffd257e0596881f41ff5c8d807c2b4961",
            forwardFamily.Key);
    }

    [Fact]
    public void DiscoveryTreatsNullMarkersAsEmptyFormsForRecurringInsertion()
    {
        var phonemes = new[]
        {
            new PhonemeFacts("a", "a", [new PhonemeGraphemeFact("a", "qaa", "a")],
                Vector(("class", "vowel"))),
        };
        var entries = new[]
        {
            Entry("entry-null-one", "^0"),
            Entry("entry-null-two", "∅"),
        };

        var result = AlternationDiscovery.Analyze(new AlternationInput(1, phonemes, entries, [], true));
        var family = Assert.Single(result.Families);

        Assert.Equal(2, family.Members.Count);
        Assert.Contains("∅ ~ a", family.ChangeDescription, StringComparison.Ordinal);

        AlternationEntryFacts Entry(string guid, string nullForm) => new(guid, guid, ["shared-gate"],
        [
            Allomorph(guid + "-null", nullForm),
            Allomorph(guid + "-vowel", "a"),
        ]);

        AlternationAllomorphFacts Allomorph(string guid, string form) => new(guid, "prefix",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["qaa"] = [form],
            }, new AlternationConditionFacts(true, false, false, []), NoGate);
    }

    [Fact]
    public void DiscoveryUsesSegmentIdentityLaneForOnePositionSubstitutionWithoutFeatures()
    {
        var phonemes = new[]
        {
            new PhonemeFacts("phoneme-m", "m", [new PhonemeGraphemeFact("phoneme-m", "qaa", "m")],
                new PhonemeFeatureVector(false, new Dictionary<string, string>(StringComparer.Ordinal))),
            new PhonemeFacts("phoneme-n", "n", [new PhonemeGraphemeFact("phoneme-n", "qaa", "n")],
                new PhonemeFeatureVector(false, new Dictionary<string, string>(StringComparer.Ordinal))),
        };
        var entries = new[]
        {
            Entry("entry-one"),
            Entry("entry-two"),
        };

        var result = AlternationDiscovery.Analyze(new AlternationInput(2, phonemes, entries, [], true));
        var family = Assert.Single(result.Families);

        Assert.Equal("segment-identity", family.Lane);
        Assert.Equal(["phoneme-m"], family.InputPhonemeGuids);
        Assert.Equal(["phoneme-n"], family.OutputPhonemeGuids);
        Assert.Empty(family.ChangedFeatures);
        Assert.Empty(family.SharedFeatures);
        Assert.Equal(2, family.Members.Count);

        AlternationEntryFacts Entry(string guid) => new(guid, guid, ["shared-gate"],
        [
            Allomorph(guid + "-n", "n"),
            Allomorph(guid + "-m", "m"),
        ]);

        AlternationAllomorphFacts Allomorph(string guid, string form) => new(guid, "prefix",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["qaa"] = [form],
            }, new AlternationConditionFacts(true, false, false, []), NoGate);
    }

    [Fact]
    public void DiscoveryReportsAbstentionCountsByReasonInItsLimitations()
    {
        var phonemes = new[]
        {
            new PhonemeFacts("phoneme-a", "a", [new PhonemeGraphemeFact("phoneme-a", "qaa", "a")],
                Vector(("class", "vowel"))),
        };
        var entry = new AlternationEntryFacts("entry-one", "entry-one", ["shared-gate"],
        [
            Allomorph("allomorph-a", "a"),
            Allomorph("allomorph-unmapped", "x"),
        ]);

        var result = AlternationDiscovery.Analyze(new AlternationInput(1, phonemes, [entry], [], true));

        Assert.Equal(1, result.AbstentionsByReason["unmapped-phoneme-form-pair"]);
        Assert.Contains(result.Limitations,
            limitation => limitation.Contains("unmapped-phoneme-form-pair=1", StringComparison.Ordinal));

        AlternationAllomorphFacts Allomorph(string guid, string form) => new(guid, "prefix",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["qaa"] = [form],
            }, new AlternationConditionFacts(true, false, false, []), NoGate);
    }

    private static readonly AlternationGateFacts NoGate = new(string.Empty, string.Empty);

    [Fact]
    public void AFamilyStillFormsWhenOneAllomorphIsNotCompiled()
    {
        var entries = new[]
        {
            new AlternationEntryFacts("entry-one", "entry-one", ["shared-gate"],
            [
                GatedAllomorph("entry-one-n", "n", NoGate),
                GatedAllomorph("entry-one-m", "m", NoGate),
                GatedAllomorph("entry-one-x", "x", null),
            ]),
            IdentityEntry("entry-two", "shared-gate"),
        };

        var result = AlternationDiscovery.Analyze(new AlternationInput(0, IdentityPhonemes(), entries, [], true));

        Assert.Single(result.Families);
        Assert.False(result.Inconclusive);
        Assert.Single(result.Limitations, limitation =>
            limitation == "1 allomorphs the parser does not load were left out.");
    }

    [Fact]
    public void ConflictingCompiledGatesAbstain()
    {
        var entries = new[]
        {
            new AlternationEntryFacts("entry-one", "entry-one", ["shared-gate"],
            [
                GatedAllomorph("entry-one-n", "n", NoGate),
                GatedAllomorph("entry-one-m", "m", new AlternationGateFacts(string.Empty, string.Empty, true)),
            ]),
            IdentityEntry("entry-two", "shared-gate"),
        };

        var result = AlternationDiscovery.Analyze(new AlternationInput(0, IdentityPhonemes(), entries, [], true));

        Assert.Equal(1, result.AbstentionsByReason["conflicting-compiled-gates-pair"]);
        Assert.True(result.Inconclusive);
        Assert.Empty(result.Families);
    }

    private static AlternationAllomorphFacts GatedAllomorph(string guid, string form, AlternationGateFacts? gate) =>
        new(guid, "prefix", new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["qaa"] = [form],
        }, new AlternationConditionFacts(true, false, false, []), gate);


    private static PhonemeFeatureVector Vector(params (string Feature, string Value)[] assignments) =>
        new(true, assignments.ToDictionary(pair => pair.Feature, pair => pair.Value, StringComparer.Ordinal));

    private static IReadOnlyList<PhonemeFacts> IdentityPhonemes() =>
    [
        IdentityPhoneme("phoneme-m", "m"),
        IdentityPhoneme("phoneme-n", "n"),
    ];

    private static PhonemeFacts IdentityPhoneme(string guid, string name) => new(guid, name,
        [new PhonemeGraphemeFact(guid, "qaa", name)],
        new PhonemeFeatureVector(false, new Dictionary<string, string>(StringComparer.Ordinal)));

    private static AlternationEntryFacts IdentityEntry(string guid, string gate) => new(guid, guid, [gate],
    [
        IdentityAllomorph(guid + "-n", "n"),
        IdentityAllomorph(guid + "-m", "m"),
    ]);

    private static AlternationAllomorphFacts IdentityAllomorph(string guid, string form,
        AlternationConditionFacts? conditions = null) => new(guid, "prefix",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["qaa"] = [form],
        }, conditions ?? new AlternationConditionFacts(true, false, false, []), NoGate);
}
