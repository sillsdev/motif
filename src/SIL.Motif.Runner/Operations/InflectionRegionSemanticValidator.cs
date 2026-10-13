using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Model;

namespace SIL.Motif.Runner.Operations;

internal static class InflectionRegionSemanticValidator
{
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        PartOfSpeechStemNamesOperationKinds.Create,
        MoStemNameRegionsOperationKinds.Create,
        FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs,
        FsFeatureSpecificationFeatureOperationKinds.SetFeature,
        FsFeatureSpecificationFeatureOperationKinds.ClearFeature,
        FsClosedValueValueOperationKinds.SetValue,
        FsClosedValueValueOperationKinds.ClearValue,
    };

    internal static void ValidateProposal(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        var relevant = operations.Where(operation => Kinds.Contains(operation.Kind)).ToArray();
        if (relevant.Length == 0) return;
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var names = new Dictionary<Guid, IMoStemName>();
        foreach (var operation in relevant)
        {
            if (operation.Target is { } target && repository.IsValidObjectId(target.ToGuid())) Include(repository.GetObject(target.ToGuid()));
            if (operation.EntityId is { } entity && repository.IsValidObjectId(entity.ToGuid())) Include(repository.GetObject(entity.ToGuid()));
        }
        foreach (var name in names.Values)
        {
            if (name.Owner is not IPartOfSpeech category ||
                !cache.LangProject.PartsOfSpeechOA.ReallyReallyAllPossibilities.Contains(category))
                throw new InvalidOperationException("A stem name must belong to a category in the project's parts-of-speech list.");
            foreach (var region in name.RegionsOC)
            {
                var features = new HashSet<Guid>();
                foreach (var spec in region.FeatureSpecsOC)
                {
                    if (spec is not IFsClosedValue closed || closed.FeatureRA is not IFsClosedFeature feature ||
                        closed.ValueRA is not { } value || !cache.LangProject.MsFeatureSystemOA.FeaturesOC.Contains(feature) ||
                        !feature.ValuesOC.Contains(value))
                        throw new InvalidOperationException("A stem-name region must use a value of its closed morphosyntactic feature. Run a fresh Dry Run after correcting the choices.");
                    if (!features.Add(feature.Guid))
                        throw new InvalidOperationException("A feature may occur only once in a stem-name region.");
                }
            }
        }

        void Include(ICmObject value)
        {
            ICmObject? current = value;
            while (current is not null && current is not IMoStemName) current = current.Owner;
            if (current is IMoStemName name) names[name.Guid] = name;
        }
    }
}
