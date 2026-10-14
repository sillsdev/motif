using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Model;

namespace SIL.Motif.Runner.Operations;

/// <summary>Checks native inflectional affix requirements after the complete Proposal graph exists.</summary>
internal static class InflFeatsSemanticValidator
{
    private static readonly HashSet<string> RequirementKinds = new(StringComparer.Ordinal)
    {
        MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats,
        MoInflAffMsaPartOfSpeechOperationKinds.SetPartOfSpeech,
        MoInflAffMsaPartOfSpeechOperationKinds.ClearPartOfSpeech,
        FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs,
        FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs,
        FsFeatureSpecificationFeatureOperationKinds.SetFeature,
        FsFeatureSpecificationFeatureOperationKinds.ClearFeature,
        FsClosedValueValueOperationKinds.SetValue,
        FsClosedValueValueOperationKinds.ClearValue,
        FsFeatStrucTypeFeaturesOperationKinds.AddRefFeatures,
        FsFeatStrucTypeFeaturesOperationKinds.RemoveRefFeatures,
        FsClosedFeatureValuesOperationKinds.Create,
    };

    internal static void RequireSelection(LcmCache cache, IFsFeatStruc structure, IFsClosedFeature feature)
    {
        if (structure.Owner is not IMoInflAffMsa msa) return;
        RequireOwner(cache, msa, structure);
        var system = cache.LangProject.MsFeatureSystemOA ??
            throw new InvalidOperationException("InflFeats requires the current morphosyntactic feature system.");
        if (feature.Guid == Guid.Empty || feature.Owner?.Guid != system.Guid || !system.FeaturesOC.Contains(feature))
            throw new InvalidOperationException("InflFeats requires a closed feature in the current morphosyntactic feature system.");
        if (structure.TypeRA is { } type &&
            (type.Owner?.Guid != system.Guid || !system.TypesOC.Contains(type) || !type.FeaturesRS.Contains(feature)))
            throw new InvalidOperationException("InflFeats requires a morphosyntactic type containing the selected feature.");
    }

    internal static void ValidateProposal(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        var addressed = operations.Where(operation => RequirementKinds.Contains(operation.Kind))
            .SelectMany(operation => new[] { operation.Target, operation.EntityId })
            .OfType<SIL.Motif.Contract.Ids.CanonicalId>().Select(id => id.ToGuid()).ToHashSet();
        if (addressed.Count == 0) return;
        foreach (var msa in cache.ServiceLocator.GetInstance<IMoInflAffMsaRepository>().AllInstances())
        {
            if (msa.InflFeatsOA is not { } structure) continue;
            var graph = new List<ICmObject> { msa, structure };
            if (msa.Owner is { } owner) graph.Add(owner);
            if (msa.PartOfSpeechRA is { } category) graph.Add(category);
            if (structure.TypeRA is { } type) graph.Add(type);
            foreach (var spec in structure.FeatureSpecsOC)
            {
                graph.Add(spec);
                if (spec.FeatureRA is { } feature) graph.Add(feature);
                if (spec is IFsClosedValue closed && closed.ValueRA is { } value) graph.Add(value);
            }
            if (!graph.Any(item => addressed.Contains(item.Guid))) continue;
            RequireOwner(cache, msa, structure);
            if (structure.TypeRA is { } declaredType &&
                !cache.LangProject.MsFeatureSystemOA.TypesOC.Contains(declaredType))
                throw new InvalidOperationException("InflFeats type must belong to the current morphosyntactic feature system.");
            var features = new HashSet<Guid>();
            foreach (var spec in structure.FeatureSpecsOC)
            {
                if (spec.Guid == Guid.Empty)
                    throw new InvalidOperationException("InflFeats specifications must have nonzero identities.");
                // Native complex branches are incumbent grammar, outside the closed-selection authoring contract.
                if (spec is IFsComplexValue) continue;
                if (spec is not IFsClosedValue closed)
                    throw new InvalidOperationException("InflFeats specifications must be closed values with nonzero identities.");
                if (closed.FeatureRA is null)
                {
                    if (closed.ValueRA is not null)
                        throw new InvalidOperationException("An InflFeats value requires its feature reference.");
                    continue;
                }
                if (closed.FeatureRA is not IFsClosedFeature feature)
                    throw new InvalidOperationException("InflFeats requires a closed feature.");
                RequireSelection(cache, structure, feature);
                if (!features.Add(feature.Guid))
                    throw new InvalidOperationException("A feature may occur only once in InflFeats.");
                if (closed.ValueRA is { } value && (value.Guid == Guid.Empty ||
                    value.Owner?.Guid != feature.Guid || !feature.ValuesOC.Contains(value)))
                    throw new InvalidOperationException("InflFeats value must belong to its exact closed feature.");
            }
        }
    }

    private static void RequireOwner(LcmCache cache, IMoInflAffMsa msa, IFsFeatStruc structure)
    {
        if (msa.Guid == Guid.Empty || structure.Guid == Guid.Empty || msa.InflFeatsOA?.Guid != structure.Guid ||
            msa.Owner is not ILexEntry entry || !entry.MorphoSyntaxAnalysesOC.Contains(msa))
            throw new InvalidOperationException("InflFeats must occupy its inflectional MSA's owning slot in a lexical entry.");
        if (msa.PartOfSpeechRA is { } category &&
            !cache.LangProject.PartsOfSpeechOA.ReallyReallyAllPossibilities.Contains(category))
            throw new InvalidOperationException("The inflectional MSA category must belong to the project's parts-of-speech list.");
    }
}
