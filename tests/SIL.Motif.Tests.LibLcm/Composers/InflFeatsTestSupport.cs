using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

internal static class InflFeatsTestSupport
{
    internal static (CanonicalId Feature, CanonicalId[] Values) AddFeature(LcmCache cache, string name, params string[] values)
    {
        var operations = AuthorInflectionFeatureComposer.Build(cache,
            new(name, Anal(cache), values.Select(value => new InflectionFeatureOption(value)).ToArray()));
        ApplyOps(cache, operations);
        return (operations.Single(operation => operation.Kind == FsFeatureSystemFeaturesOperationKinds.Create).EntityId!.Value,
            operations.Where(operation => operation.Kind == FsClosedFeatureValuesOperationKinds.Create)
                .Select(operation => operation.EntityId!.Value).ToArray());
    }

    internal static OperationEnvelope Structure(CanonicalId msa) => new(CanonicalId.Mint(),
        MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats, target: msa, entityId: CanonicalId.Mint(),
        after: JsonSerializer.SerializeToElement(new { }));

    internal static OperationEnvelope[] Selection(CanonicalId structure, CanonicalId? feature, CanonicalId? value = null,
        CanonicalId? structureCreator = null)
    {
        var create = new OperationEnvelope(CanonicalId.Mint(), FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs,
            target: structure, entityId: CanonicalId.Mint(), after: JsonSerializer.SerializeToElement(new { }),
            dependsOn: structureCreator is { } creator ? [new(creator)] : []);
        var result = new List<OperationEnvelope> { create };
        if (feature is { } selectedFeature)
            result.Add(new(CanonicalId.Mint(), FsFeatureSpecificationFeatureOperationKinds.SetFeature,
                target: create.EntityId, after: JsonSerializer.SerializeToElement(new { @ref = selectedFeature.Value }),
                dependsOn: [new(create.OperationId)]));
        if (value is { } selectedValue)
            result.Add(new(CanonicalId.Mint(), FsClosedValueValueOperationKinds.SetValue,
                target: create.EntityId, after: JsonSerializer.SerializeToElement(new { @ref = selectedValue.Value }),
                dependsOn: [new(create.OperationId)]));
        return result.ToArray();
    }
}
