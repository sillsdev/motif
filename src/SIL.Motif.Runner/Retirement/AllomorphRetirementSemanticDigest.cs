using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Retirement;

public static class AllomorphRetirementSemanticDigest
{
    public static string Compute(LcmCache cache, IMoAffixAllomorph form)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(form);
        var state = new
        {
            className = form.ClassName,
            morphType = Id(form.MorphTypeRA),
            form.IsAbstract,
            alternatives = form.Form.AvailableWritingSystemIds
                .Select(ws => (Tag: cache.WritingSystemFactory.GetStrFromWs(ws),
                    Text: form.Form.get_String(ws)?.Text?.Normalize(NormalizationForm.FormD) ?? string.Empty))
                .OrderBy(item => item.Tag, StringComparer.Ordinal)
                .Select(item => new { writingSystem = item.Tag, text = item.Text }).ToArray(),
            requiredFeatures = FeatureStructure(form.MsEnvFeaturesOA),
            phoneEnvironments = form.PhoneEnvRC.Select(environment => new
                {
                    id = Id(environment),
                    representation = environment.StringRepresentation?.get_NormalizedForm(FwNormalizationMode.knmNFSC)?.Text
                        .Normalize(NormalizationForm.FormD),
                    names = environment.Name.AvailableWritingSystemIds
                        .Select(ws => (Tag: cache.WritingSystemFactory.GetStrFromWs(ws),
                            Text: environment.Name.get_String(ws)?.Text?.Normalize(NormalizationForm.FormD) ?? string.Empty))
                        .OrderBy(item => item.Tag, StringComparer.Ordinal)
                        .Select(item => new { writingSystem = item.Tag, text = item.Text }).ToArray()
                })
                .OrderBy(item => item.id, StringComparer.Ordinal).ToArray(),
            positions = form.PositionRS.Select(Id).ToArray(),
            inflectionClasses = form.InflectionClassesRC.Select(Id).Order(StringComparer.Ordinal).ToArray()
        };
        var canonical = CanonicalJson.Canonicalize(JsonSerializer.Serialize(state));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static object? FeatureStructure(IFsFeatStruc? structure, HashSet<Guid>? ancestry = null)
    {
        if (structure is null) return null;
        ancestry ??= [];
        if (!ancestry.Add(structure.Guid))
            throw new InvalidOperationException("A cyclic allomorph feature structure cannot be retired safely.");
        try
        {
            return structure.FeatureSpecsOC.Select(spec => new
                {
                    className = spec.ClassName,
                    feature = Id(spec.FeatureRA),
                    value = spec is IFsClosedValue closed ? Id(closed.ValueRA) : null,
                    nested = spec is IFsComplexValue complex
                        ? FeatureStructure(complex.ValueOA as IFsFeatStruc, ancestry)
                        : null
                })
                .OrderBy(item => item.feature, StringComparer.Ordinal).ToArray();
        }
        finally
        {
            ancestry.Remove(structure.Guid);
        }
    }

    private static string? Id(ICmObject? value) => value is null ? null : CanonicalId.FromGuid(value.Guid).Value;
}
