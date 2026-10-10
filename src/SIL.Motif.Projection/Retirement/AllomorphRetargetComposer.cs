using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Retirement;

namespace SIL.Motif.Projection.Retirement;

/// <summary>Composes exact reference retargets while leaving retired forms in their entries.</summary>
public static class AllomorphRetargetComposer
{
    /// <summary>Builds bundle and ad hoc operations from an authored, census-checked intent.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, RetireAllomorphIntent intent, Func<CanonicalId> operationIdFactory)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(operationIdFactory);

        if (intent.Scope != AllomorphRetirementScope.Affix)
            throw new InvalidOperationException("The retained-form composer requires the Affix scope; stem retarget proofs are unavailable.");

        var entryId = ParseId(intent.Entry, "entry");
        if (!cache.ServiceLocator.ObjectRepository.TryGetObject(entryId.ToGuid(), out var entryObject) ||
            entryObject is not ILexEntry entry)
            throw new InvalidOperationException("The authored retirement entry is absent from the loaded project.");

        var retired = intent.RetiredForms.ToDictionary(item => ParseId(item.Id, "retired form"), item => item);
        if (retired.Count == 0 || retired.Values.Any(item => ParseId(item.Entry, "retired form entry") != entryId))
            throw new InvalidOperationException("Retired forms must name one or more alternates of the authored entry.");

        var roleReplacements = new Dictionary<RoleKey, AllomorphIdentity>();
        foreach (var mapping in intent.RoleReplacements)
        {
            var retiredId = ParseId(mapping.RetiredForm, "role source");
            var msaId = ParseId(mapping.Msa, "role MSA");
            if (mapping.ExpansionRole != "whole" || !retired.ContainsKey(retiredId))
                throw new InvalidOperationException("Retargeting supports only declared whole-form roles of retired alternates.");
            var replacementId = ParseId(mapping.Replacement.Id, "role replacement");
            if (retired.ContainsKey(replacementId))
                throw new InvalidOperationException("A retired form cannot be a retarget destination.");
            if (!roleReplacements.TryAdd(new RoleKey(retiredId, msaId, ParseNullableId(mapping.InflType)), mapping.Replacement))
                throw new InvalidOperationException("A retired form role has more than one replacement.");
            RequireCompatible(cache, entry, retired[retiredId], mapping.Replacement);
        }

        var adhocReplacements = new Dictionary<AdhocKey, AllomorphIdentity>();
        foreach (var mapping in intent.AdhocReplacements)
        {
            var source = ParseId(mapping.RetiredForm, "ad hoc source");
            var replacement = ParseId(mapping.Replacement.Id, "ad hoc replacement");
            if (!retired.ContainsKey(source) || retired.ContainsKey(replacement))
                throw new InvalidOperationException("Ad hoc destinations must replace a retired form with a surviving form.");
            if (mapping.Field is not ("FirstAllomorph" or "RestOfAllos" or "Allomorphs") ||
                (mapping.Field == "FirstAllomorph") != (mapping.Ordinal is null) ||
                mapping.Ordinal is < 0)
                throw new InvalidOperationException("An ad hoc replacement must name one exact supported field occurrence.");
            if (!adhocReplacements.TryAdd(new AdhocKey(ParseId(mapping.Rule, "ad hoc rule"), mapping.Field,
                    mapping.Ordinal, source), mapping.Replacement))
                throw new InvalidOperationException("An ad hoc occurrence has more than one replacement.");
            RequireCompatible(cache, entry, retired[source], mapping.Replacement);
        }

        var forms = retired.Keys.Select(item => item.ToGuid()).ToArray();
        var census = AllomorphReferenceFootprintReader.Read(cache, forms);
        var diagnostics = AllomorphReferenceFootprintReader.Diagnose(census, intent);
        if (!diagnostics.IsComplete)
            throw new InvalidOperationException(diagnostics.Message);
        ValidateExactCensusBindings(census.References, intent, roleReplacements, adhocReplacements);

        var censusDigest = AllomorphRetargetCensusReader.ComputeDigest(cache, forms);
        var operations = new List<OperationEnvelope>();
        var operationIds = new HashSet<CanonicalId>();
        var retiredFormIds = retired.Keys.OrderBy(item => item.Value, StringComparer.Ordinal).Select(item => item.Value).ToArray();

        foreach (var reference in census.References.Where(item => item.Kind == "bundle-morph")
                     .OrderBy(item => item.SourceObject).ThenBy(item => item.TargetForm))
        {
            var retiredId = CanonicalId.FromGuid(reference.TargetForm);
            if (!roleReplacements.TryGetValue(new RoleKey(retiredId,
                    CanonicalId.FromGuid(reference.Msa ?? throw new InvalidOperationException("A bundle has no MSA.")),
                    reference.InflType is { } inflType ? CanonicalId.FromGuid(inflType) : null), out var destination))
                throw new InvalidOperationException("A bundle's exact MSA and InflType role has no replacement.");

            var bundleId = CanonicalId.FromGuid(reference.Bundle ?? throw new InvalidOperationException("A bundle reference has no bundle identity."));
            AddOperation(AllomorphRetargetOperationKinds.SetBundleMorph, bundleId, new
            {
                retiredForms = retiredFormIds,
                censusDigest,
                retiredForm = retiredId.Value,
                replacement = destination.Id
            });
        }

        foreach (var ruleGroup in census.References.Where(item => item.Kind.StartsWith("adhoc-", StringComparison.Ordinal))
                     .GroupBy(item => item.SourceObject).OrderBy(group => group.Key))
        {
            if (!cache.ServiceLocator.ObjectRepository.TryGetObject(ruleGroup.Key, out var ruleObject) ||
                ruleObject is not IMoAlloAdhocProhib rule)
                throw new InvalidOperationException("An ad hoc reference owner is absent from the loaded project.");

            var fields = new Dictionary<string, List<AdhocOccurrenceReplacement>>();
            foreach (var reference in ruleGroup.OrderBy(item => item.Field, StringComparer.Ordinal).ThenBy(item => item.Ordinal))
            {
                var field = reference.Field ?? throw new InvalidOperationException("An ad hoc reference has no field name.");
                var oldId = CanonicalId.FromGuid(reference.TargetForm);
                var key = new AdhocKey(CanonicalId.FromGuid(rule.Guid), field, reference.Ordinal, oldId);
                if (!adhocReplacements.TryGetValue(key, out var destination))
                    throw new InvalidOperationException("An ad hoc reference occurrence has no exact authored replacement.");
                if (!fields.TryGetValue(field, out var values)) fields[field] = values = [];
                values.Add(new AdhocOccurrenceReplacement(oldId, ParseId(destination.Id, "ad hoc destination"),
                    reference.Ordinal));
            }

            var fieldPayloads = fields.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
                BuildField(cache, rule, pair.Key, pair.Value)).ToArray();
            AddOperation(AllomorphRetargetOperationKinds.RetargetAdhocReferences, CanonicalId.FromGuid(rule.Guid), new
            {
                retiredForms = retiredFormIds,
                censusDigest,
                fields = fieldPayloads
            });
        }

        return operations;

        void AddOperation(string kind, CanonicalId target, object payload)
        {
            var operationId = operationIdFactory();
            if (!operationIds.Add(operationId))
                throw new InvalidOperationException("The operation id factory returned a duplicate id.");
            operations.Add(new OperationEnvelope(operationId, kind, target: target,
                after: JsonSerializer.SerializeToElement(payload)));
        }
    }

    private static object BuildField(LcmCache cache, IMoAlloAdhocProhib rule, string field,
        IReadOnlyList<AdhocOccurrenceReplacement> replacements)
    {
        var before = field switch
        {
            "FirstAllomorph" => rule.FirstAllomorphRA is { } first ? new[] { first.Guid } : [],
            "RestOfAllos" => rule.RestOfAllosRS.Select(item => item.Guid).ToArray(),
            "Allomorphs" => rule.AllomorphsRS.Select(item => item.Guid).ToArray(),
            _ => throw new InvalidOperationException($"Unsupported ad hoc field '{field}'.")
        };
        var after = before.ToArray();
        foreach (var replacement in replacements)
        {
            var index = field == "FirstAllomorph" ? 0 : replacement.Ordinal ??
                throw new InvalidOperationException("An ad hoc sequence replacement needs an ordinal.");
            if (index >= before.Length || before[index] != replacement.Source.ToGuid())
                throw new InvalidOperationException("An ad hoc occurrence changed after its replacement was authored.");
            after[index] = replacement.Destination.ToGuid();
        }
        return new
        {
            field,
            before = before.Select(item => CanonicalId.FromGuid(item).Value).ToArray(),
            after = after.Select(item => CanonicalId.FromGuid(item).Value).ToArray()
        };
    }

    private static void RequireCompatible(LcmCache cache, ILexEntry entry, AllomorphIdentity sourceIdentity,
        AllomorphIdentity destinationIdentity)
    {
        var source = ResolveAffix(cache, ParseId(sourceIdentity.Id, "retired form"));
        var destination = ResolveAffix(cache, ParseId(destinationIdentity.Id, "replacement"));
        if (source.Guid == destination.Guid || source.Owner?.Guid != entry.Guid || destination.Owner?.Guid != entry.Guid ||
            !entry.AlternateFormsOS.Contains(source) ||
            (!entry.AlternateFormsOS.Contains(destination) && entry.LexemeFormOA?.Guid != destination.Guid))
            throw new InvalidOperationException("Retargeting requires a retired alternate and a surviving form in the same entry.");
        var type = source.MorphTypeRA?.Guid;
        if ((type != MoMorphTypeTags.kguidMorphPrefix && type != MoMorphTypeTags.kguidMorphSuffix) ||
            destination.MorphTypeRA?.Guid != type || source.IsAbstract || destination.IsAbstract)
            throw new InvalidOperationException("Retargeting supports matching ordinary prefix and suffix forms only.");
        var position = type == MoMorphTypeTags.kguidMorphPrefix ? "prefix" : "suffix";
        if (ParseId(sourceIdentity.Entry, "retired form entry") != CanonicalId.FromGuid(entry.Guid) ||
            ParseId(destinationIdentity.Entry, "replacement entry") != CanonicalId.FromGuid(entry.Guid) ||
            sourceIdentity.Class != source.ClassName || destinationIdentity.Class != destination.ClassName ||
            sourceIdentity.Position != position || destinationIdentity.Position != position ||
            sourceIdentity.Location != "alternate" ||
            (destinationIdentity.Location != "alternate" && destinationIdentity.Location != "lexeme"))
            throw new InvalidOperationException("An authored allomorph identity no longer matches its LibLCM object.");
    }

    private static IMoAffixAllomorph ResolveAffix(LcmCache cache, CanonicalId id) =>
        cache.ServiceLocator.GetInstance<IMoAffixAllomorphRepository>().GetObject(id.ToGuid());

    private static void ValidateExactCensusBindings(
        IReadOnlyList<AllomorphReference> references,
        RetireAllomorphIntent intent,
        IReadOnlyDictionary<RoleKey, AllomorphIdentity> roleReplacements,
        IReadOnlyDictionary<AdhocKey, AllomorphIdentity> adhocReplacements)
    {
        var bundleReferences = references.Where(item => item.Kind == "bundle-morph").ToArray();
        var expectedRoles = bundleReferences.Select(item => new RoleKey(CanonicalId.FromGuid(item.TargetForm),
            CanonicalId.FromGuid(item.Msa ?? throw new InvalidOperationException("A bundle has no MSA.")),
            item.InflType is { } type ? CanonicalId.FromGuid(type) : null)).ToHashSet();
        if (!expectedRoles.SetEquals(roleReplacements.Keys))
            throw new InvalidOperationException("Authored role replacements do not exactly match the live bundle census.");

        var declaredBundles = intent.Bundles;
        if (declaredBundles.Count != bundleReferences.Length || bundleReferences.Any(reference =>
                !declaredBundles.Any(item => SameId(item.Bundle, reference.Bundle) &&
                    SameId(item.Analysis, reference.Analysis) && SameId(item.Wordform, reference.Wordform) &&
                    SameId(item.RetiredForm, reference.TargetForm) && SameId(item.Msa, reference.Msa) &&
                    NullableSameId(item.InflType, reference.InflType) && item.ExpansionRole == "whole")))
            throw new InvalidOperationException("Authored bundle rows do not exactly match the live reference census.");

        var adhocReferences = references.Where(item => item.Kind.StartsWith("adhoc-", StringComparison.Ordinal)).ToArray();
        if (adhocReferences.Length != adhocReplacements.Count || adhocReferences.Any(reference =>
                !adhocReplacements.ContainsKey(new AdhocKey(CanonicalId.FromGuid(reference.Rule ??
                    throw new InvalidOperationException("An ad hoc reference has no rule identity.")),
                    reference.Field ?? throw new InvalidOperationException("An ad hoc reference has no field."),
                    reference.Ordinal, CanonicalId.FromGuid(reference.TargetForm)))))
            throw new InvalidOperationException("Authored ad hoc rows do not exactly match the live reference census.");
    }

    private static bool SameId(string value, Guid? guid) => guid is { } actual &&
        CanonicalId.TryParse(value, out var id) && id.ToGuid() == actual;

    private static bool NullableSameId(string? value, Guid? guid) => value is null
        ? guid is null
        : SameId(value, guid);

    private static CanonicalId ParseId(string value, string role) =>
        CanonicalId.TryParse(value, out var id) ? id : throw new InvalidOperationException($"The {role} is not a canonical id.");

    private static CanonicalId? ParseNullableId(string? value) => value is null ? null : ParseId(value, "InflType");

    private readonly record struct RoleKey(CanonicalId Retired, CanonicalId Msa, CanonicalId? InflType);
    private readonly record struct AdhocKey(CanonicalId Rule, string Field, int? Ordinal, CanonicalId Retired);
    private sealed record AdhocOccurrenceReplacement(CanonicalId Source, CanonicalId Destination, int? Ordinal);
}
