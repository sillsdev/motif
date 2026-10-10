using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;

namespace SIL.Motif.Projection.HumanJudgments;

/// <summary>The capability found for the project's reserved human-judgment field.</summary>
public enum HumanJudgmentFieldCapability
{
    /// <summary>The reserved String field is available with the required schema.</summary>
    Available,

    /// <summary>Project initialization has not created the reserved field.</summary>
    Missing,

    /// <summary>The reserved name exists, but its owner or schema is incompatible.</summary>
    Incompatible,

    /// <summary>More than one field uses the reserved internal name.</summary>
    Ambiguous,

    /// <summary>The field is valid, but the project has no Research Notebook.</summary>
    NotebookUnavailable,
}

/// <summary>The metadata signature used to validate the reserved field without a field GUID.</summary>
public sealed record HumanJudgmentFieldSignature(
    string DeclaringClass,
    string Type,
    bool IsCustom,
    int WritingSystemSelector,
    int DestinationClass,
    Guid ListRoot);

/// <summary>The result of resolving the project's reserved custom field by its internal name.</summary>
public sealed record HumanJudgmentFieldResolution(
    HumanJudgmentFieldCapability Capability,
    string Message,
    HumanJudgmentFieldSignature? Signature)
{
    /// <summary>The current cache-local field id, never a portable identity.</summary>
    internal int CacheFieldId { get; init; }
}

/// <summary>Finds the initialized Notebook field by exact internal name and verifies its complete schema.</summary>
public static class HumanJudgmentFieldResolver
{
    /// <summary>The reserved field's exact LibLCM metadata name.</summary>
    public const string InternalName = "MotifHumanJudgment";

    /// <summary>The model class that declares the reserved field.</summary>
    public const string DeclaringClass = "RnGenericRec";

    /// <summary>Resolves the installed field without relying on its local field id or user label.</summary>
    public static HumanJudgmentFieldResolution Resolve(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var fields = metadata.GetFieldIds()
            .Where(flid => string.Equals(metadata.GetFieldName(flid), InternalName, StringComparison.Ordinal))
            .ToArray();
        if (fields.Length == 0)
            return new(HumanJudgmentFieldCapability.Missing,
                $"Project initialization is required because {DeclaringClass}.{InternalName} is missing.", null);
        if (fields.Length > 1)
            return new(HumanJudgmentFieldCapability.Ambiguous,
                $"The project contains {fields.Length} definitions named {InternalName}; only one is supported.", null);

        var fieldId = fields[0];
        var signature = new HumanJudgmentFieldSignature(
            metadata.GetOwnClsName(fieldId),
            ((CellarPropertyType)metadata.GetFieldType(fieldId)).ToString(),
            metadata.IsCustom(fieldId),
            metadata.GetFieldWs(fieldId),
            metadata.GetDstClsId(fieldId),
            metadata.GetFieldListRoot(fieldId));
        if (!Compatible(signature))
            return new(HumanJudgmentFieldCapability.Incompatible,
                $"The {InternalName} field has an unsupported schema: " + Describe(signature) + ".", signature);

        return new HumanJudgmentFieldResolution(HumanJudgmentFieldCapability.Available,
            $"{DeclaringClass}.{InternalName} is available.", signature) { CacheFieldId = fieldId };
    }

    /// <summary>Checks the full reserved signature without using a label or local field id.</summary>
    internal static bool Compatible(HumanJudgmentFieldSignature signature) =>
        string.Equals(signature.DeclaringClass, DeclaringClass, StringComparison.Ordinal) &&
        string.Equals(signature.Type, CellarPropertyType.String.ToString(), StringComparison.Ordinal) &&
        signature.IsCustom &&
        signature.WritingSystemSelector == WritingSystemServices.kwsAnal &&
        signature.DestinationClass == 0 &&
        signature.ListRoot == Guid.Empty;

    private static string Describe(HumanJudgmentFieldSignature signature) =>
        $"{signature.DeclaringClass}.{InternalName} ({signature.Type}, custom={signature.IsCustom}, " +
        $"writing-system selector={signature.WritingSystemSelector}, destination={signature.DestinationClass}, " +
        $"list={signature.ListRoot})";
}
