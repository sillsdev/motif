using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Contract.Retirement;

internal static class AllomorphRetirementValidation
{
    internal static string Key(string value) => CanonicalId.FromGuid(CanonicalId.Parse(value).ToGuid()).Value;
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new FormatException(message);
    }
    internal static void Id(string? value) => Require(CanonicalId.TryParse(value, out _), "Invalid canonical entity id.");
    internal static void Digest(string? value) => Require(Sha256Value.IsCanonical(value), "Invalid semantic digest.");
    internal static void Text(string? value) => Require(!string.IsNullOrWhiteSpace(value) && value.Length <= 4096,
        "Missing or oversized text.");
    private static void List<T>(IReadOnlyList<T>? value, int minimum, int maximum = 65536) =>
        Require(value is not null && value.Count >= minimum && value.Count <= maximum && value.All(x => x is not null),
            "Missing, null or out-of-bounds list.");
    private static void Ids(IReadOnlyList<string> values, int minimum, bool distinct)
    {
        List(values, minimum);
        foreach (var id in values) Id(id);
        if (distinct) Require(values.Select(Key).Distinct().Count() == values.Count, "Duplicate identity.");
    }
    private static string Role(string form, string msa, string? inflType, string role)
    {
        Id(form); Id(msa);
        if (inflType is not null) Id(inflType);
        Require(role == "whole", "Unsupported expansion role; ordinary retirement requires whole.");
        return Key(form) + ":" + Key(msa) + ":" + (inflType is null ? "null" : Key(inflType)) + ":" + role;
    }
    private static void Form(AllomorphIdentity value, string entry, bool source, AllomorphRetirementScope scope)
    {
        Require(value is not null, "Missing allomorph identity.");
        Id(value!.Id); Id(value.Entry); Digest(value.SemanticDigest);
        Require(Key(value.Entry) == Key(entry), "Replacement and retired form must remain in the same entry.");
        Require(scope is AllomorphRetirementScope.Affix or AllomorphRetirementScope.Stem, "Unsupported retirement scope.");
        Require(value.Class == (scope == AllomorphRetirementScope.Affix ? "MoAffixAllomorph" : "MoStemAllomorph"),
            "Allomorph class must match the declared retirement scope.");
        Require(scope == AllomorphRetirementScope.Affix ? value.Position is "prefix" or "suffix"
                : value.Position is "root" or "stem",
            "Only ordinary prefix/suffix affixes or unbound root/stem forms are supported.");
        if (value.StemName is not null) Id(value.StemName);
        Require(scope == AllomorphRetirementScope.Stem || value.StemName is null, "Affixes cannot assert a stem name.");
        Require(source ? value.Location == "alternate" : value.Location is "alternate" or "lexeme",
            "Only an alternate form may be retired; the replacement must survive in its entry.");
    }

    internal static void Validate(ReplaceListedAllomorphsWithRuleIntent value)
    {
        Require(value.Format == "motif-allomorph-retirement" && value.Version == 2, "Unsupported retirement format/version; regenerate the authoring artifact.");
        Require(value.NaturalClass is not null && value.Rule is not null, "Complete class and rule authoring is required.");
        var cls = value.NaturalClass!;
        Id(cls.Id); Text(cls.Name); Text(cls.Abbreviation); Ids(cls.Members, 1, true);
        Require(cls.Mode is "create" or "reuse", "Unsupported natural-class mode.");
        if (cls.Mode == "reuse") Digest(cls.SemanticDigest);
        else Require(cls.SemanticDigest is null, "A new natural class has no Baseline semantic digest.");
        var rule = value.Rule!;
        Id(rule.Id); Text(rule.Name);
        Require(Key(rule.Id) != Key(cls.Id), "Rule and natural class cannot share an identity.");
        Ids(rule.Input, 1, false); Ids(rule.Output, 1, false);
        foreach (var atoms in new[] { rule.Left, rule.Right })
        {
            List(atoms, 0, 256);
            foreach (var atom in atoms)
            {
                Id(atom.Id);
                Require(atom.Kind is "segment" or "natural-class" or "boundary", "Unsupported rule context kind.");
                if (atom.Kind == "natural-class")
                    Require(Key(atom.Id) == Key(cls.Id), "Rule context must name the declared natural class.");
            }
        }
        Require(rule.Left.Concat(rule.Right).Any(a => a.Kind == "natural-class"), "Declared class must participate in the rule.");
        Require(rule.Enabled && rule.Placement is not null, "The candidate requires a complete enabled and placed rule.");
        var placement = rule.Placement!;
        Require(placement.Kind is "first" or "last" or "before" or "after", "Unsupported rule placement.");
        if (placement.Kind is "before" or "after")
        {
            Id(placement.Anchor);
            Require(Key(placement.Anchor!) != Key(rule.Id), "A rule cannot anchor itself.");
        }
        else Require(placement.Anchor is null, "First/last placement has no anchor.");
        List(value.Retirements, 1, 256);
        var sources = new HashSet<string>();
        var entries = new HashSet<string>();
        var bundles = new HashSet<string>();
        var expectedSlots = new HashSet<string>();
        var objectClasses = new Dictionary<string, string>();
        var replacementAssertions = new Dictionary<string, AllomorphIdentity>();
        void Object(string id, string type)
        {
            Id(id);
            var key = Key(id);
            Require(!objectClasses.TryGetValue(key, out var prior) || prior == type, "Object identity has conflicting classes.");
            objectClasses[key] = type;
        }
        Object(cls.Id, "PhNCSegments"); Object(rule.Id, "PhRegularRule");
        foreach (var id in cls.Members.Concat(rule.Input).Concat(rule.Output)) Object(id, "PhPhoneme");
        foreach (var atom in rule.Left.Concat(rule.Right))
            Object(atom.Id, atom.Kind switch { "segment" => "PhPhoneme", "boundary" => "PhBdryMarker", _ => "PhNCSegments" });
        foreach (var retirement in value.Retirements)
        {
            Object(retirement.Entry, "LexEntry");
            Require(entries.Add(Key(retirement.Entry)), "Compose all retirements in one component per entry.");
            List(retirement.RetiredForms, 1, 4096);
            foreach (var source in retirement.RetiredForms)
            {
                Form(source, retirement.Entry, true, retirement.Scope); Object(source.Id, source.Class);
                if (source.StemName is not null) Object(source.StemName, "MoStemName");
                Require(sources.Add(Key(source.Id)), "Duplicate retired identity.");
                expectedSlots.Add(Slot("alternate-delete", retirement.Entry, source.Id));
            }
        }
        void Destination(AllomorphIdentity destination, AllomorphIdentity source, RetireAllomorphIntent retirement)
        {
            Form(destination, retirement.Entry, false, retirement.Scope); Object(destination.Id, destination.Class);
            if (destination.StemName is not null) Object(destination.StemName, "MoStemName");
            Require(!sources.Contains(Key(destination.Id)), "Mapping chains, cycles and retired replacements are refused.");
            Require(destination.Position == source.Position, "Source and surviving form must have compatible morph types.");
            Require(source.StemName is null ? destination.StemName is null
                    : destination.StemName is not null && Key(source.StemName) == Key(destination.StemName),
                "Retirement cannot change a stem-name gate.");
            var normalized = destination with { Id = Key(destination.Id), Entry = Key(destination.Entry),
                StemName = destination.StemName is null ? null : Key(destination.StemName) };
            Require(!replacementAssertions.TryGetValue(normalized.Id, out var prior) || prior == normalized,
                "Conflicting semantic assertions for a surviving form.");
            replacementAssertions[normalized.Id] = normalized;
        }
        var analysisOwners = new Dictionary<string, string>();
        var occurrences = new HashSet<string>();
        var targets = new HashSet<string>();
        foreach (var retirement in value.Retirements)
        {
            var local = retirement.RetiredForms.ToDictionary(f => Key(f.Id));
            List(retirement.RoleReplacements, 1); List(retirement.Bundles, 0); List(retirement.AdhocReplacements, 0);
            var roles = new HashSet<string>();
            foreach (var mapping in retirement.RoleReplacements)
            {
                var key = Role(mapping.RetiredForm, mapping.Msa, mapping.InflType, mapping.ExpansionRole);
                Require(local.TryGetValue(Key(mapping.RetiredForm), out var source), "Role names an undeclared retired form.");
                Require(roles.Add(key), "Duplicate or ambiguous role mapping.");
                Object(mapping.Msa, "MoMorphSynAnalysis");
                if (mapping.InflType is not null) Object(mapping.InflType, "LexEntryInflType");
                Require(retirement.Scope != AllomorphRetirementScope.Stem || mapping.InflType is null,
                    "Stem variant expansion with InflType is unsupported.");
                Destination(mapping.Replacement, source!, retirement);
            }
            Require(local.Keys.All(id => retirement.RoleReplacements.Any(m => Key(m.RetiredForm) == id)),
                "Every retired form needs an explicit role mapping.");
            foreach (var bundle in retirement.Bundles)
            {
                Object(bundle.Bundle, "WfiMorphBundle"); Object(bundle.Analysis, "WfiAnalysis"); Object(bundle.Wordform, "WfiWordform");
                Require(bundles.Add(Key(bundle.Bundle)), "Duplicate bundle destination.");
                Require(!analysisOwners.TryGetValue(Key(bundle.Analysis), out var owner) || owner == Key(bundle.Wordform),
                    "Analysis belongs to conflicting wordforms.");
                analysisOwners[Key(bundle.Analysis)] = Key(bundle.Wordform);
                Require(roles.Contains(Role(bundle.RetiredForm, bundle.Msa, bundle.InflType, bundle.ExpansionRole)),
                    "Bundle role has no exact authored replacement.");
                expectedSlots.Add(Slot("bundle-morph", bundle.Bundle, null));
            }
            foreach (var reference in retirement.AdhocReplacements)
            {
                Object(reference.Rule, "MoAlloAdhocProhib"); Id(reference.RetiredForm);
                Require(reference.Field is "FirstAllomorph" or "RestOfAllos" or "Allomorphs", "Unsupported ad hoc reference field.");
                Require(reference.Field == "FirstAllomorph" ? reference.Ordinal is null : reference.Ordinal is >= 0,
                    "Atomic references require null ordinal; sequence/collection occurrences require a nonnegative ordinal.");
                Require(local.TryGetValue(Key(reference.RetiredForm), out var source), "Reference names an undeclared retired form.");
                Destination(reference.Replacement, source!, retirement);
                Require(occurrences.Add(Key(reference.Rule) + ":" + reference.Field + ":" + reference.Ordinal),
                    "Duplicate reference occurrence.");
                Require(targets.Add(Key(reference.Rule) + ":" + Key(reference.Replacement.Id)),
                    "Mapped ad hoc targets collapse a conjunctive member or form a self-target.");
                var slot = reference.Field switch { "FirstAllomorph" => "adhoc-first", "RestOfAllos" => "adhoc-rest", _ => "adhoc-legacy" };
                expectedSlots.Add(Slot(slot, reference.Rule, null));
            }
        }
        if (cls.Mode == "create")
        {
            expectedSlots.Add(Slot("class-create", cls.Id, null));
            expectedSlots.Add(Slot("class-members", cls.Id, null));
        }
        foreach (var slot in RuleSlots) expectedSlots.Add(Slot(slot, rule.Id, null));
        Graph(value.Operations, expectedSlots, cls.Mode == "create");
        if (value.Display is { } display) { Text(display.Title); Text(display.Description); }
    }

    internal static void Validate(RetireAllomorphIntentDocument value)
    {
        Require(value.Format == "motif-retire-allomorph" && value.Version == 1,
            "Unsupported standalone retirement format/version; regenerate the authoring artifact.");
        var retirement = value.Retirement;
        Require(retirement is not null && retirement.Scope == AllomorphRetirementScope.Affix,
            "Standalone retirement supports only affix alternate forms.");
        Id(retirement!.Entry);
        var entry = Key(retirement.Entry);
        List(retirement.RetiredForms, 1, 4096);
        List(value.DuplicateSurvivors, 1, 4096);
        List(retirement.RoleReplacements, 0);
        List(retirement.Bundles, 0);
        List(retirement.AdhocReplacements, 0);

        var sources = new Dictionary<string, AllomorphIdentity>(StringComparer.Ordinal);
        foreach (var source in retirement.RetiredForms)
        {
            Form(source, entry, true, AllomorphRetirementScope.Affix);
            Require(sources.TryAdd(Key(source.Id), source), "Duplicate retired identity.");
        }

        var survivors = new Dictionary<string, AllomorphIdentity>(StringComparer.Ordinal);
        foreach (var survivor in value.DuplicateSurvivors)
        {
            Form(survivor, entry, false, AllomorphRetirementScope.Affix);
            Require(!sources.ContainsKey(Key(survivor.Id)), "A retired form cannot survive as its own destination.");
            Require(survivors.TryAdd(Key(survivor.Id), survivor), "Duplicate surviving identity.");
        }
        Require(sources.Values.All(source => survivors.Values.Any(survivor =>
                survivor.Position == source.Position && survivor.SemanticDigest == source.SemanticDigest)),
            "Every retired form requires a measured exact duplicate survivor.");

        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in retirement.RoleReplacements)
        {
            var sourceId = Key(mapping.RetiredForm);
            Require(sources.TryGetValue(sourceId, out var source), "Role names an undeclared retired form.");
            var role = Role(mapping.RetiredForm, mapping.Msa, mapping.InflType, mapping.ExpansionRole);
            Require(roles.Add(role), "Duplicate or ambiguous role mapping.");
            _ = Destination(mapping.Replacement, source!, entry, sources);
            Require(survivors.TryGetValue(Key(mapping.Replacement.Id), out var survivor) && SameIdentity(survivor, mapping.Replacement),
                "Every role replacement must name a declared duplicate survivor.");
        }

        var bundles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bundle in retirement.Bundles)
        {
            Id(bundle.Bundle); Id(bundle.Analysis); Id(bundle.Wordform); Id(bundle.Msa);
            if (bundle.InflType is not null) Id(bundle.InflType);
            var sourceId = Key(bundle.RetiredForm);
            Require(sources.ContainsKey(sourceId), "Bundle names an undeclared retired form.");
            Require(bundle.ExpansionRole == "whole" &&
                roles.Contains(Role(bundle.RetiredForm, bundle.Msa, bundle.InflType, bundle.ExpansionRole)),
                "Bundle has no exact whole-form role mapping.");
            Require(bundles.Add(Key(bundle.Bundle)), "Duplicate bundle destination.");
        }

        var occurrences = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in retirement.AdhocReplacements)
        {
            Id(mapping.Rule);
            Require(mapping.Field is "FirstAllomorph" or "RestOfAllos" or "Allomorphs",
                "Unsupported ad hoc reference field.");
            Require(mapping.Field == "FirstAllomorph" ? mapping.Ordinal is null : mapping.Ordinal is >= 0,
                "Ad hoc atomic and ordered occurrences require their exact ordinal shape.");
            var sourceId = Key(mapping.RetiredForm);
            Require(sources.TryGetValue(sourceId, out var source), "Ad hoc row names an undeclared retired form.");
            var destination = Destination(mapping.Replacement, source!, entry, sources);
            Require(survivors.TryGetValue(Key(destination.Id), out var survivor) && SameIdentity(survivor, destination),
                "Every ad hoc replacement must name a declared duplicate survivor.");
            Require(occurrences.Add(Key(mapping.Rule) + ":" + mapping.Field + ":" + mapping.Ordinal),
                "Duplicate ad hoc occurrence.");
            Require(targets.Add(Key(mapping.Rule) + ":" + Key(destination.Id)),
                "Ad hoc destinations cannot collapse or self-target a prohibition.");
        }

    }

    private static AllomorphIdentity Destination(AllomorphIdentity destination, AllomorphIdentity source,
        string entry, IReadOnlyDictionary<string, AllomorphIdentity> sources)
    {
        Form(destination, entry, false, AllomorphRetirementScope.Affix);
        Require(!sources.ContainsKey(Key(destination.Id)), "A retired form cannot be a replacement destination.");
        Require(destination.Position == source.Position, "Source and survivor must have matching morph types.");
        return destination with { Id = Key(destination.Id), Entry = Key(destination.Entry) };
    }

    private static bool SameIdentity(AllomorphIdentity left, AllomorphIdentity right) =>
        Key(left.Id) == Key(right.Id) && Key(left.Entry) == Key(right.Entry) && left.Class == right.Class &&
        left.Location == right.Location && left.Position == right.Position &&
        left.SemanticDigest == right.SemanticDigest && (left.StemName is null
            ? right.StemName is null
            : right.StemName is not null && Key(left.StemName) == Key(right.StemName));

    private static readonly string[] RuleSlots =
    ["rule-create", "rule-input", "rule-output", "rule-left", "rule-right", "rule-placement", "rule-enabled"];
    private static string Slot(string slot, string target, string? member) =>
        slot + ":" + Key(target) + ":" + (member is null ? "" : Key(member));

    private static void Graph(IReadOnlyList<RetirementOperationBinding> operations, HashSet<string> expected, bool createClass)
    {
        List(operations, 1);
        var byId = new Dictionary<string, RetirementOperationBinding>();
        var slots = new HashSet<string>();
        foreach (var operation in operations)
        {
            Id(operation.OperationId); Id(operation.Target);
            Require(operation.Slot == "alternate-delete" ? operation.Member is not null : operation.Member is null,
                "Only alternate deletion carries a member identity.");
            if (operation.Member is not null) Id(operation.Member);
            Ids(operation.DependsOn, 0, true);
            Require(byId.TryAdd(Key(operation.OperationId), operation), "Duplicate operation identity.");
            Require(slots.Add(Slot(operation.Slot, operation.Target, operation.Member)), "Multiple writers for one retirement slot.");
        }
        Require(slots.SetEquals(expected), "Operation bindings must cover exactly the complete authored class/rule, retargets and deletions.");
        foreach (var operation in operations)
            Require(operation.DependsOn.All(id => byId.ContainsKey(Key(id))), "Dependency names an absent operation.");
        var ancestors = new Dictionary<string, HashSet<string>>();
        var visiting = new HashSet<string>();
        HashSet<string> Visit(string id)
        {
            if (ancestors.TryGetValue(id, out var result)) return result;
            Require(visiting.Count < 256, "Dependency graph exceeds supported depth.");
            Require(visiting.Add(id), "Dependency cycle.");
            result = new HashSet<string>();
            foreach (var dependency in byId[id].DependsOn.Select(Key))
            {
                result.Add(dependency);
                result.UnionWith(Visit(dependency));
            }
            visiting.Remove(id);
            ancestors[id] = result;
            return result;
        }
        foreach (var id in byId.Keys) Visit(id);
        var classOps = operations.Where(o => o.Slot.StartsWith("class-", StringComparison.Ordinal)).ToArray();
        var ruleOps = operations.Where(o => RuleSlots.Contains(o.Slot)).ToArray();
        var retargets = operations.Where(o => o.Slot == "bundle-morph" || o.Slot.StartsWith("adhoc-", StringComparison.Ordinal)).ToArray();
        void After(RetirementOperationBinding operation, IEnumerable<RetirementOperationBinding> prerequisites) =>
            Require(prerequisites.All(p => ancestors[Key(operation.OperationId)].Contains(Key(p.OperationId))),
                "Missing required declared dependency; array position is not mutation order.");
        if (createClass) After(classOps.Single(o => o.Slot == "class-members"), classOps.Where(o => o.Slot == "class-create"));
        foreach (var operation in ruleOps)
        {
            After(operation, classOps);
            if (operation.Slot != "rule-create") After(operation, ruleOps.Where(o => o.Slot == "rule-create"));
        }
        foreach (var operation in retargets) After(operation, ruleOps);
        foreach (var operation in operations.Where(o => o.Slot == "alternate-delete")) After(operation, ruleOps.Concat(retargets));
    }
}
