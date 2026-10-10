using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SIL.Motif.Host.Parsimony;

internal sealed record AlternationInput(int RelevantFeatureCount, IReadOnlyList<PhonemeFacts> Phonemes,
    IReadOnlyList<AlternationEntryFacts> Entries, IReadOnlyList<ExistingRewriteRule> Rules,
    bool RuleInventoryKnown);

internal sealed record PhonemeFacts(string Guid, string Name, IReadOnlyList<PhonemeGraphemeFact> Graphemes,
    PhonemeFeatureVector Features);

internal sealed record AlternationEntryFacts(string Guid, string Description, IReadOnlyList<string> GateKeys,
    IReadOnlyList<AlternationAllomorphFacts> Allomorphs);

// Null when the compiler did not represent the allomorph, so the parser never loads it and it is left out.
internal sealed record AlternationAllomorphFacts(string Guid, string MorphType,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Forms, AlternationConditionFacts Conditions,
    AlternationGateFacts? Gate);

// Key is gate_signature (applied classes are MPRs there); Conflicting marks disagreeing compiled outputs.
internal sealed record AlternationGateFacts(string Key, string Description, bool Conflicting = false);

internal sealed record AlternationConditionFacts(bool Known, bool HasPhoneCondition, bool HasPositionCondition,
    IReadOnlyList<ResolvedEnvironmentFacts> PhoneEnvironments);

internal sealed record ResolvedEnvironmentFacts(string Key, string Description);

internal sealed record ExistingRewriteRule(string? InputPhonemeGuid, string? OutputPhonemeGuid, bool IsExact,
    bool? Loaded, IReadOnlyList<string>? ConditionKeys = null, string? Guid = null,
    bool CanAffectAlternation = true);

internal sealed record AlternationFeatureChange(string FeatureGuid, string InputValueGuid, string OutputValueGuid);

internal sealed record AlternationMemberFacts(string EntryGuid, string EntryDescription,
    IReadOnlyList<string> AllomorphGuids, IReadOnlyList<string> WritingSystems,
    IReadOnlyList<string> Forms, IReadOnlyList<string> LocalContexts, string AlignmentEvidence);

internal sealed record AlternationFamilyFacts(string Key, string ChangeKey, string ChangeDescription,
    string Lane, bool Directed, string ContextKey, IReadOnlyList<string>? ConditionKeys,
    string ContextDescription, string GateKey, string GateDescription, string SideDescription,
    IReadOnlyList<string> InputPhonemeGuids, IReadOnlyList<string> OutputPhonemeGuids,
    IReadOnlyList<AlternationFeatureChange> ChangedFeatures,
    IReadOnlyList<AlternationFeatureChange> SharedFeatures,
    IReadOnlyList<AlternationMemberFacts> Members)
{
    internal string Description =>
        $"{ChangeDescription} {ContextDescription} in {Members.Count} {SideDescription}" +
        (GateDescription.Length == 0 ? string.Empty : $", gated by {GateDescription}");
}

internal sealed record AlternationDiscoveryResult(int EligibleMorphemes, IReadOnlyList<AlternationFamilyFacts> Families,
    IReadOnlyList<string> Limitations, IReadOnlyDictionary<string, int> AbstentionsByReason, bool Inconclusive);

internal static class AlternationDiscovery
{
    internal static AlternationDiscoveryResult Analyze(AlternationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var limitations = new SortedSet<string>(StringComparer.Ordinal);
        var abstentions = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var phonemes = input.Phonemes.ToDictionary(item => item.Guid, StringComparer.Ordinal);
        var graphemes = input.Phonemes.SelectMany(item => item.Graphemes).ToArray();
        var features = input.Phonemes.ToDictionary(item => item.Guid, item => item.Features, StringComparer.Ordinal);
        var familyMembers = new Dictionary<string, FamilyBuilder>(StringComparer.Ordinal);
        var eligibleEntries = new HashSet<string>(StringComparer.Ordinal);
        var uncompiledAllomorphs = new HashSet<string>(StringComparer.Ordinal);
        var uncertain = false;

        void Abstain(string reason, string limitation)
        {
            abstentions[reason] = abstentions.GetValueOrDefault(reason) + 1;
            limitations.Add(limitation);
            uncertain = true;
        }

        foreach (var entry in input.Entries.OrderBy(item => item.Guid, StringComparer.Ordinal))
        {
            if (entry.GateKeys.Count == 0)
            {
                if (HasSiblingForms(entry))
                    Abstain("incomplete-msa-gate-entry",
                        "Some sibling allomorphs have no complete MSA gate facts and were excluded.");
                continue;
            }

            foreach (var unsupportedGroup in entry.Allomorphs.Where(item => UnsupportedMorphType(item.MorphType))
                         .GroupBy(item => item.MorphType, StringComparer.Ordinal).Where(group => group.Count() > 1))
                Abstain("unsupported-morph-type-group",
                    "Sibling allomorph groups with unsupported morph types were excluded.");

            foreach (var siblingGroup in entry.Allomorphs.Where(item => !UnsupportedMorphType(item.MorphType))
                         .GroupBy(item => item.MorphType, StringComparer.Ordinal))
            {
                var siblings = siblingGroup.OrderBy(item => item.Guid, StringComparer.Ordinal).ToArray();
                if (siblings.Length < 2) continue;
                foreach (var sibling in siblings.Where(item => item.Gate is null))
                    uncompiledAllomorphs.Add(sibling.Guid);
                var writingSystems = siblings.SelectMany(item => item.Forms.Keys)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                foreach (var writingSystem in writingSystems)
                {
                    var candidates = siblings.Where(item => item.Forms.ContainsKey(writingSystem)).ToArray();
                    if (candidates.Length < 2) continue;
                    eligibleEntries.Add(entry.Guid);
                    for (var leftIndex = 0; leftIndex < candidates.Length; leftIndex++)
                    for (var rightIndex = leftIndex + 1; rightIndex < candidates.Length; rightIndex++)
                    {
                        var left = candidates[leftIndex];
                        var right = candidates[rightIndex];
                        if (left.Gate?.Conflicting == true || right.Gate?.Conflicting == true)
                        {
                            Abstain("conflicting-compiled-gates-pair",
                                "Some allomorphs have compiled gates that disagree; they were excluded from pairs.");
                            continue;
                        }
                        // Uncompiled allomorphs are counted once and left out; they are not a reason to abstain.
                        if (left.Gate is null || right.Gate is null) continue;
                        // Allomorphs under different compiled gate signatures are never one family.
                        if (left.Gate.Key != right.Gate.Key) continue;
                        if (!TrySingleForm(left, writingSystem, out var leftForm) ||
                            !TrySingleForm(right, writingSystem, out var rightForm))
                        {
                            Abstain("multiple-forms-per-writing-system-pair",
                                "Allomorphs with multiple forms in one writing system were excluded.");
                            continue;
                        }
                        var leftTokens = PhonemeTokenizer.Tokenize(leftForm, writingSystem, graphemes);
                        var rightTokens = PhonemeTokenizer.Tokenize(rightForm, writingSystem, graphemes);
                        if (leftTokens.Status != PhonemeTokenizationStatus.Unique ||
                            rightTokens.Status != PhonemeTokenizationStatus.Unique)
                        {
                            var reason = leftTokens.Status != PhonemeTokenizationStatus.Unique
                                ? TokenizationReason(leftTokens.Status)
                                : TokenizationReason(rightTokens.Status);
                            Abstain(reason, "Some allomorph forms could not be uniquely tokenized through their phonemes.");
                            continue;
                        }
                        var weightedAlignment = PhonemeAligner.Align(leftTokens.PhonemeGuids, rightTokens.PhonemeGuids,
                            features, input.RelevantFeatureCount);
                        var alignment = weightedAlignment;
                        var lane = "feature-weighted";
                        if (PhonemeAligner.TryAlignHammingOne(leftTokens.PhonemeGuids, rightTokens.PhonemeGuids,
                                out var identityAlignment))
                        {
                            var identityEdit = identityAlignment.SingleEdit!;
                            var weightedEdit = weightedAlignment.SingleEdit;
                            var changedPairHasFeatures = ChangedPhonemesHaveFeatures(identityEdit, features,
                                input.RelevantFeatureCount);
                            var weightedEditMatchesIdentity = weightedEdit is not null &&
                                weightedEdit.Kind == identityEdit.Kind &&
                                weightedEdit.InputPhonemeGuid == identityEdit.InputPhonemeGuid &&
                                weightedEdit.OutputPhonemeGuid == identityEdit.OutputPhonemeGuid;
                            if (!changedPairHasFeatures || weightedAlignment.Status != PhonemeAlignmentStatus.Unique ||
                                !weightedEditMatchesIdentity)
                            {
                                alignment = identityAlignment;
                                lane = "segment-identity";
                            }
                        }
                        if (alignment.Status != PhonemeAlignmentStatus.Unique)
                        {
                            Abstain(alignment.Status == PhonemeAlignmentStatus.RewriteUnavailable
                                    ? "missing-feature-weighted-alignment-pair"
                                    : alignment.Status == PhonemeAlignmentStatus.Tied
                                        ? "tied-minimum-cost-alignment-pair"
                                        : "unavailable-alignment-pair",
                                alignment.Status == PhonemeAlignmentStatus.RewriteUnavailable
                                    ? "Some substitutions have missing phonological feature values; rewrite evidence is unavailable."
                                    : "Some sibling forms have tied or unavailable minimum-cost alignments.");
                            continue;
                        }
                        var edit = alignment.SingleEdit;
                        if (edit is null)
                        {
                            if (alignment.Steps.Any(step => step.Kind != PhonemeEditKind.Equal))
                            {
                                Abstain("multi-segment-change-pair",
                                    "Multi-segment changes are outside this measure's supported shape.");
                            }
                            continue;
                        }
                        if (lane == "feature-weighted" && edit.Kind == PhonemeEditKind.Substitution &&
                            !ChangedPhonemesHaveFeatures(edit, features, input.RelevantFeatureCount))
                        {
                            Abstain("missing-changed-phoneme-features-pair",
                                "A changed phoneme lacks a complete feature vector; rewrite evidence is unavailable.");
                            continue;
                        }

                        var pairContext = ResolveContext(left.Conditions, right.Conditions, left.Guid, right.Guid);
                        if (pairContext.Unknown)
                        {
                            Abstain("unresolved-condition-pair",
                                "Some allomorph conditions are unresolved or outside the supported context shape.");
                            continue;
                        }
                        if (left.Conditions.HasPositionCondition || right.Conditions.HasPositionCondition)
                        {
                            Abstain("position-environment-pair",
                                "Position environments were not treated as phonological conditioning.");
                            continue;
                        }

                        var change = ResolveChange(edit, lane, left, right, pairContext, phonemes, features,
                            input.RelevantFeatureCount);
                        var localContext = LocalContext(alignment.Steps, edit);
                        var familyContext = pairContext.Key;
                        if (edit.Kind is PhonemeEditKind.Insertion or PhonemeEditKind.Deletion)
                            familyContext += "/boundary:" + JsonSerializer.Serialize(localContext);
                        foreach (var gateKey in entry.GateKeys)
                        {
                            var familyKey = JsonSerializer.Serialize(new
                            {
                                change.Directed,
                                lane = change.Lane,
                                change = change.Key,
                                change.InputPhonemeGuids,
                                change.OutputPhonemeGuids,
                                side = siblingGroup.Key,
                                context = familyContext,
                                gate = gateKey,
                                allomorphGate = left.Gate.Key,
                            });
                            if (!familyMembers.TryGetValue(familyKey, out var family))
                            {
                                family = new FamilyBuilder(familyKey, change, pairContext, gateKey,
                                    left.Gate.Description, SideDescription(siblingGroup.Key));
                                familyMembers.Add(familyKey, family);
                            }
                            family.AddMember(entry, left, right, writingSystem, leftForm, rightForm, localContext,
                                alignment, change);
                        }
                    }
                }
            }
        }

        if (!input.RuleInventoryKnown)
            limitations.Add("Existing loaded rule effects could not be fully determined from the available evidence.");
        if (uncompiledAllomorphs.Count > 0)
            limitations.Add($"{uncompiledAllomorphs.Count} allomorphs the parser does not load were left out.");
        var families = new List<AlternationFamilyFacts>();
        var ruleInconclusive = false;
        foreach (var family in familyMembers.Values.Where(item => item.Members.Count >= 2)
                     .Select(item => item.Build()).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var expressed = false;
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            if (!input.RuleInventoryKnown)
                reasons.Add("the loaded rule inventory is incomplete");
            foreach (var rule in input.Rules)
            {
                var assessment = AssessRule(family, rule);
                if (assessment.Expressed) expressed = true;
                if (assessment.Reason is not null) reasons.Add(assessment.Reason);
            }

            if (reasons.Count > 0)
            {
                ruleInconclusive = true;
                limitations.Add($"Family {family.Key} is Inconclusive because {string.Join("; ", reasons)}.");
            }
            if (!expressed || reasons.Count > 0) families.Add(family);
        }

        var abstentionSummary = abstentions.Count == 0
            ? "none"
            : string.Join(", ", abstentions.Select(item => $"{item.Key}={item.Value}"));
        limitations.Add("Abstention counts by reason: " + abstentionSummary + ".");

        return new AlternationDiscoveryResult(eligibleEntries.Count, Array.AsReadOnly(families.ToArray()),
            Array.AsReadOnly(limitations.ToArray()),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(abstentions),
            uncertain || !input.RuleInventoryKnown || ruleInconclusive);
    }

    private static bool HasSiblingForms(AlternationEntryFacts entry) => entry.Allomorphs
        .GroupBy(item => item.MorphType, StringComparer.Ordinal).Any(group => group.Count() > 1);

    private static bool UnsupportedMorphType(string morphType) => morphType is
        "circumfix" or "discontigPhrase" or "infixingInterfix" or "prefixingInterfix" or "suffixingInterfix";

    private static bool TrySingleForm(AlternationAllomorphFacts allomorph, string writingSystem, out string form)
    {
        if (allomorph.Forms.TryGetValue(writingSystem, out var forms) && forms.Count == 1)
        {
            form = forms[0];
            return true;
        }
        form = string.Empty;
        return false;
    }

    private static string TokenizationReason(PhonemeTokenizationStatus status) => status switch
    {
        PhonemeTokenizationStatus.Unmapped => "unmapped-phoneme-form-pair",
        PhonemeTokenizationStatus.Ambiguous => "ambiguous-phoneme-form-pair",
        PhonemeTokenizationStatus.OverBound => "over-bound-phoneme-form-pair",
        _ => "unavailable-tokenization-pair",
    };

    private static bool ChangedPhonemesHaveFeatures(PhonemeAlignmentStep edit,
        IReadOnlyDictionary<string, PhonemeFeatureVector> features, int relevantFeatureCount)
    {
        if (edit.Kind != PhonemeEditKind.Substitution || relevantFeatureCount <= 0) return false;
        foreach (var guid in new[] { edit.InputPhonemeGuid, edit.OutputPhonemeGuid }.OfType<string>().Distinct())
            if (!features.TryGetValue(guid, out var vector) || !vector.Complete ||
                vector.Values.Count != relevantFeatureCount) return false;
        return true;
    }

    private static PairContext ResolveContext(AlternationConditionFacts left, AlternationConditionFacts right,
        string leftGuid, string rightGuid)
    {
        if (!left.Known || !right.Known)
            return new PairContext("condition:unknown", "under unresolved conditions", false, null, null, true);
        var leftKeys = left.PhoneEnvironments.Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
        var rightKeys = right.PhoneEnvironments.Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
        var leftHas = left.HasPhoneCondition;
        var rightHas = right.HasPhoneCondition;
        if (!leftHas && !rightHas)
            return new PairContext("condition:unspecified", "with conditioning not stated", false, null, [], false);
        if (leftHas != rightHas)
        {
            var conditionedGuid = leftHas ? leftGuid : rightGuid;
            var environments = leftHas ? left.PhoneEnvironments : right.PhoneEnvironments;
            var conditionedDescriptions = environments.Select(item => item.Description).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            var keys = environments.Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
            return new PairContext(JsonSerializer.Serialize(keys),
                conditionedDescriptions.Length == 1 ? conditionedDescriptions[0] :
                    "in contexts " + string.Join(" and ", conditionedDescriptions),
                true, conditionedGuid, Array.AsReadOnly(keys), false);
        }
        var combined = JsonSerializer.Serialize(new { left = leftKeys, right = rightKeys });
        var common = leftKeys.SequenceEqual(rightKeys, StringComparer.Ordinal);
        var descriptions = left.PhoneEnvironments.Concat(right.PhoneEnvironments)
            .Select(item => item.Description).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new PairContext(combined, descriptions.Length == 1 ? descriptions[0] :
            "in contexts " + string.Join(" and ", descriptions), false, null,
            common ? Array.AsReadOnly(leftKeys) : null, !common);
    }

    private static ResolvedChange ResolveChange(PhonemeAlignmentStep edit, string lane, AlternationAllomorphFacts left,
        AlternationAllomorphFacts right, PairContext context, IReadOnlyDictionary<string, PhonemeFacts> phonemes,
        IReadOnlyDictionary<string, PhonemeFeatureVector> features, int featureCount)
    {
        var leftIsConditioned = context.ConditionedAllomorphGuid == left.Guid;
        var directed = context.DirectionSupported;
        var input = edit.InputPhonemeGuid;
        var output = edit.OutputPhonemeGuid;
        if (directed && leftIsConditioned)
            (input, output) = (output, input);
        if (!directed && edit.Kind == PhonemeEditKind.Substitution &&
            StringComparer.Ordinal.Compare(input, output) > 0)
            (input, output) = (output, input);
        if (!directed && edit.Kind == PhonemeEditKind.Insertion)
            input = null;
        if (!directed && edit.Kind == PhonemeEditKind.Deletion)
        {
            output = input;
            input = null;
        }
        var from = input is null ? "∅" : phonemes[input].Name;
        var to = output is null ? "∅" : phonemes[output].Name;
        var operation = directed ? " → " : " ~ ";
        var changed = new List<AlternationFeatureChange>();
        var shared = new List<AlternationFeatureChange>();
        if (lane == "feature-weighted" && input is not null && output is not null &&
            edit.Kind == PhonemeEditKind.Substitution &&
            features[input].Complete && features[output].Complete && featureCount > 0)
        {
            foreach (var featureGuid in features[input].Values.Keys.Order(StringComparer.Ordinal))
            {
                var inputValue = features[input].Values[featureGuid];
                var outputValue = features[output].Values[featureGuid];
                var item = new AlternationFeatureChange(featureGuid, inputValue, outputValue);
                if (inputValue == outputValue) shared.Add(item);
                else changed.Add(item);
            }
        }
        var key = JsonSerializer.Serialize(new { lane, edit.Kind, input, output, directed });
        return new ResolvedChange(key, from + operation + to, lane, directed,
            input is null ? [] : [input], output is null ? [] : [output],
            Array.AsReadOnly(changed.ToArray()), Array.AsReadOnly(shared.ToArray()));
    }

    private static string[] LocalContext(IReadOnlyList<PhonemeAlignmentStep> steps, PhonemeAlignmentStep edit)
    {
        var before = steps.TakeWhile(step => !ReferenceEquals(step, edit))
            .Where(step => step.Kind == PhonemeEditKind.Equal).Select(step => step.InputPhonemeGuid!).ToArray();
        var editIndex = steps.ToList().IndexOf(edit);
        var after = steps.Skip(editIndex + 1).Where(step => step.Kind == PhonemeEditKind.Equal)
            .Select(step => step.InputPhonemeGuid!).ToArray();
        return [string.Join("/", before), string.Join("/", after)];
    }

    private static RuleAssessment AssessRule(AlternationFamilyFacts family, ExistingRewriteRule rule)
    {
        if (!rule.CanAffectAlternation || rule.Loaded == false || !CouldAffectChange(family, rule))
            return RuleAssessment.None;

        var ruleName = rule.Guid is null ? "A loaded rule" : $"Loaded rule {rule.Guid}";
        if (rule.Loaded is null)
            return RuleAssessment.Uncertain($"{ruleName} has an unknown load result and could produce this sound change");
        if (rule.InputPhonemeGuid is null || rule.OutputPhonemeGuid is null)
            return RuleAssessment.Uncertain($"{ruleName} has an unresolved sound change that could produce this one-segment change");
        if (!rule.IsExact)
            return RuleAssessment.Uncertain($"{ruleName} has additional or unresolved rule structure for this sound change");
        if (rule.ConditionKeys is null || family.ConditionKeys is null)
            return RuleAssessment.Uncertain($"{ruleName} has unresolved conditioning for this sound change");
        if (!rule.ConditionKeys.SequenceEqual(family.ConditionKeys, StringComparer.Ordinal))
            return RuleAssessment.Uncertain($"{ruleName} has conditioning that differs from the family's resolved conditioning");
        return RuleAssessment.Exact;
    }

    private static bool CouldAffectChange(AlternationFamilyFacts family, ExistingRewriteRule rule)
    {
        if (rule.InputPhonemeGuid is not null && rule.OutputPhonemeGuid is not null)
            return MatchesChange(family, rule);

        if (family.Directed)
            return (rule.InputPhonemeGuid is null ||
                    family.InputPhonemeGuids.SequenceEqual([rule.InputPhonemeGuid], StringComparer.Ordinal)) &&
                (rule.OutputPhonemeGuid is null ||
                    family.OutputPhonemeGuids.SequenceEqual([rule.OutputPhonemeGuid], StringComparer.Ordinal));

        var familyPhonemes = family.InputPhonemeGuids.Concat(family.OutputPhonemeGuids)
            .ToHashSet(StringComparer.Ordinal);
        return (rule.InputPhonemeGuid is null || familyPhonemes.Contains(rule.InputPhonemeGuid)) &&
            (rule.OutputPhonemeGuid is null || familyPhonemes.Contains(rule.OutputPhonemeGuid));
    }

    private static bool MatchesChange(AlternationFamilyFacts family, ExistingRewriteRule rule)
    {
        var forward = family.InputPhonemeGuids.SequenceEqual([rule.InputPhonemeGuid!], StringComparer.Ordinal) &&
            family.OutputPhonemeGuids.SequenceEqual([rule.OutputPhonemeGuid!], StringComparer.Ordinal);
        if (forward || family.Directed) return forward;
        return family.InputPhonemeGuids.SequenceEqual([rule.OutputPhonemeGuid!], StringComparer.Ordinal) &&
            family.OutputPhonemeGuids.SequenceEqual([rule.InputPhonemeGuid!], StringComparer.Ordinal);
    }

    private static string SideDescription(string morphType) => morphType switch
    {
        "prefix" => "prefixes",
        "suffix" => "suffixes",
        "infix" => "infixes",
        "stem" or "root" => "underlying/surface stems",
        "boundStem" or "boundRoot" => "bound stems",
        "proclitic" => "proclitics",
        "enclitic" => "enclitics",
        _ => "morphemes",
    };

    private sealed class FamilyBuilder(string key, ResolvedChange change, PairContext context, string gateKey,
        string gateDescription, string sideDescription)
    {
        public Dictionary<string, MemberBuilder> Members { get; } = new(StringComparer.Ordinal);

        public void AddMember(AlternationEntryFacts entry, AlternationAllomorphFacts left,
            AlternationAllomorphFacts right, string writingSystem, string leftForm, string rightForm,
            IReadOnlyList<string> localContext, PhonemeAlignmentResult alignment, ResolvedChange pairChange)
        {
            if (!Members.TryGetValue(entry.Guid, out var member))
                Members[entry.Guid] = member = new MemberBuilder(entry.Guid, entry.Description);
            member.Add(left.Guid, right.Guid, writingSystem, leftForm, rightForm, localContext,
                JsonSerializer.Serialize(new
                {
                    lane = pairChange.Lane,
                    costNumerator = pairChange.Lane == "segment-identity" ? (long?)null : alignment.CostNumerator,
                    costDenominator = pairChange.Lane == "segment-identity" ? (int?)null : alignment.CostDenominator,
                    pairChange.ChangedFeatures,
                    pairChange.SharedFeatures,
                }));
        }

        public AlternationFamilyFacts Build()
        {
            var members = Members.Values.OrderBy(item => item.EntryGuid, StringComparer.Ordinal)
                .Select(item => item.Build()).ToArray();
            var familyDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
            return new AlternationFamilyFacts("alternation-family/" + familyDigest, key,
                change.Description, change.Lane, change.Directed, context.Key, context.ConditionKeys, context.Description,
                gateKey, gateDescription, sideDescription, change.InputPhonemeGuids, change.OutputPhonemeGuids,
                change.ChangedFeatures, change.SharedFeatures, Array.AsReadOnly(members));
        }
    }

    private sealed class MemberBuilder(string entryGuid, string entryDescription)
    {
        private readonly SortedSet<string> _allomorphs = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _writingSystems = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _forms = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _contexts = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _alignments = new(StringComparer.Ordinal);

        public string EntryGuid { get; } = entryGuid;

        public void Add(string leftGuid, string rightGuid, string writingSystem, string leftForm, string rightForm,
            IReadOnlyList<string> localContext, string alignment)
        {
            _allomorphs.Add(leftGuid);
            _allomorphs.Add(rightGuid);
            _writingSystems.Add(writingSystem);
            _forms.Add(leftForm);
            _forms.Add(rightForm);
            _contexts.Add(JsonSerializer.Serialize(localContext));
            _alignments.Add(alignment);
        }

        public AlternationMemberFacts Build() => new(EntryGuid, entryDescription,
            Array.AsReadOnly(_allomorphs.ToArray()), Array.AsReadOnly(_writingSystems.ToArray()),
            Array.AsReadOnly(_forms.ToArray()), Array.AsReadOnly(_contexts.ToArray()),
            string.Join(";", _alignments));
    }

    private sealed record PairContext(string Key, string Description, bool DirectionSupported,
        string? ConditionedAllomorphGuid, IReadOnlyList<string>? ConditionKeys, bool Unknown);

    private sealed record RuleAssessment(bool Expressed, string? Reason)
    {
        public static RuleAssessment None { get; } = new(false, null);
        public static RuleAssessment Exact { get; } = new(true, null);
        public static RuleAssessment Uncertain(string reason) => new(false, reason);
    }

    private sealed record ResolvedChange(string Key, string Description, string Lane, bool Directed,
        IReadOnlyList<string> InputPhonemeGuids, IReadOnlyList<string> OutputPhonemeGuids,
        IReadOnlyList<AlternationFeatureChange> ChangedFeatures,
        IReadOnlyList<AlternationFeatureChange> SharedFeatures);
}
