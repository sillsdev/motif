using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Snapshotting;

/// <summary>Projects inflection ownership and named grammar pieces by exact canonical identity.</summary>
public static class InflectionSnapshots
{
    /// <summary>Projects the feature system's unordered owning membership.</summary>
    public static ObjectSnapshot Snapshot(LcmCache cache, IFsFeatureSystem system) =>
        Members(system, (SnapshotFields.FsFeatureSystemFeatures, system.FeaturesOC.Cast<ICmObject>()));

    /// <summary>Projects the closed feature's unordered symbolic values.</summary>
    public static ObjectSnapshot Snapshot(LcmCache cache, IFsClosedFeature feature) =>
        Members(feature, (SnapshotFields.FsClosedFeatureValues, feature.ValuesOC.Cast<ICmObject>()));

    /// <summary>Projects a category's explicitly owned classes and stem names.</summary>
    public static ObjectSnapshot Snapshot(LcmCache cache, IPartOfSpeech category) =>
        Members(category, (SnapshotFields.PartOfSpeechInflectionClasses, category.InflectionClassesOC.Cast<ICmObject>()),
            (SnapshotFields.PartOfSpeechStemNames, category.StemNamesOC.Cast<ICmObject>()));

    /// <summary>Projects the inflection class's unordered subclasses.</summary>
    public static ObjectSnapshot Snapshot(LcmCache cache, IMoInflClass inflectionClass) =>
        Members(inflectionClass, (SnapshotFields.MoInflClassSubclasses, inflectionClass.SubclassesOC.Cast<ICmObject>()));

    /// <summary>Projects the stem name's unordered feature regions.</summary>
    public static ObjectSnapshot Snapshot(LcmCache cache, IMoStemName stemName) =>
        Members(stemName, (SnapshotFields.MoStemNameRegions, stemName.RegionsOC.Cast<ICmObject>()));

    /// <summary>Projects the structure's unordered feature specifications.</summary>
    public static ObjectSnapshot Snapshot(LcmCache cache, IFsFeatStruc structure) =>
        Members(structure, (SnapshotFields.FsFeatStrucFeatureSpecs, structure.FeatureSpecsOC.Cast<ICmObject>()));

    /// <summary>Reads both feature systems and each category's class and stem-name trees for mechanical diff.</summary>
    public static IReadOnlyDictionary<CanonicalId, ObjectSnapshot> Read(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var result = new Dictionary<CanonicalId, ObjectSnapshot>();
        foreach (var system in new[] { cache.LangProject.MsFeatureSystemOA, cache.LangProject.PhFeatureSystemOA })
        {
            if (system is null) continue;
            Add(system, Snapshot(cache, system));
            foreach (var feature in system.FeaturesOC)
            {
                var fields = new List<ObjectSnapshot>
                    { FsFeatDefnSnapshotter.Snapshot(cache, feature), FsFeatDefnAuthoringSnapshotter.Snapshot(cache, feature) };
                if (feature is IFsClosedFeature closed)
                {
                    fields.Add(Snapshot(cache, closed));
                    foreach (var value in closed.ValuesOC)
                        Add(value, FsSymFeatValSnapshotter.Snapshot(cache, value), FsSymFeatValAuthoringSnapshotter.Snapshot(cache, value));
                }
                Add(feature, fields.ToArray());
            }
        }
        foreach (var category in cache.LangProject.PartsOfSpeechOA.ReallyReallyAllPossibilities.OfType<IPartOfSpeech>())
        {
            Add(category, Snapshot(cache, category), PartOfSpeechRelationsSnapshotter.Snapshot(cache, category));
            foreach (var inflectionClass in category.InflectionClassesOC) Class(inflectionClass);
            foreach (var stemName in category.StemNamesOC)
            {
                Add(stemName, Snapshot(cache, stemName), MoStemNameSnapshotter.Snapshot(cache, stemName),
                    MoStemNameAuthoringSnapshotter.Snapshot(cache, stemName));
                foreach (var region in stemName.RegionsOC)
                {
                    Add(region, Snapshot(cache, region));
                    foreach (var spec in region.FeatureSpecsOC)
                    {
                        if (spec is IFsClosedValue closed)
                            Add(spec, FsFeatureSpecificationRelationsSnapshotter.Snapshot(cache, spec),
                                FsClosedValueRelationsSnapshotter.Snapshot(cache, closed));
                        else Add(spec, FsFeatureSpecificationRelationsSnapshotter.Snapshot(cache, spec));
                    }
                }
            }
        }
        return result;

        void Class(IMoInflClass item)
        {
            Add(item, Snapshot(cache, item), MoInflClassSnapshotter.Snapshot(cache, item),
                MoInflClassAuthoringSnapshotter.Snapshot(cache, item));
            foreach (var child in item.SubclassesOC) Class(child);
        }

        void Add(ICmObject value, params ObjectSnapshot[] parts)
        {
            var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
                { ["class"] = new Dictionary<string, string> { ["value"] = value.ClassName } };
            if (value.Owner is { } owner)
                fields["owner"] = new Dictionary<string, string> { ["ref"] = CanonicalId.FromGuid(owner.Guid).Value };
            foreach (var part in parts)
                foreach (var (field, alternatives) in part.AlternativesFields) fields.Add(field, alternatives);
            var id = CanonicalId.FromGuid(value.Guid);
            result.Add(id, new(id, fields));
        }
    }

    private static ObjectSnapshot Members(ICmObject owner, params (string Field, IEnumerable<ICmObject> Members)[] collections)
    {
        var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (field, members) in collections)
        {
            var alternatives = members.Select(member => CanonicalId.FromGuid(member.Guid).Value).Order(StringComparer.Ordinal)
                .Select((id, index) => (id, index)).ToDictionary(pair => pair.index.ToString(CultureInfo.InvariantCulture), pair => pair.id);
            if (alternatives.Count != 0) fields.Add(field, alternatives);
        }
        return new(CanonicalId.FromGuid(owner.Guid), fields);
    }
}
