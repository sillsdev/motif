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

    /// <summary>More than one field uses a reserved internal name.</summary>
    Ambiguous,

    /// <summary>The project still has the single-field format that held a base64 marker.</summary>
    OldFormat,

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
    /// <summary>The current cache-local id of the data field, never a portable identity.</summary>
    internal int CacheFieldId { get; init; }

    /// <summary>The current cache-local id of the visible ID field, never a portable identity.</summary>
    internal int IdFieldId { get; init; }
}

/// <summary>Finds the initialized Notebook field by exact internal name and verifies its complete schema.</summary>
public static class HumanJudgmentFieldResolver
{
    /// <summary>The data field's exact LibLCM metadata name.</summary>
    public const string InternalName = "MotifJudgmentData";

    /// <summary>The visible ID field's exact LibLCM metadata name.</summary>
    public const string IdInternalName = "MotifJudgmentId";

    /// <summary>The retired single field's metadata name; its presence is refused, never read.</summary>
    public const string OldFormatInternalName = "MotifHumanJudgment";

    /// <summary>The model class that declares the reserved fields.</summary>
    public const string DeclaringClass = "RnGenericRec";

    /// <summary>The words for a project that still has the single-field format.</summary>
    public const string OldFormatMessage =
        "This project holds Motif judgments in the old format. Delete Motif's test/demo judgment records and the " +
        "MotifHumanJudgment field in FieldWorks, then run project initialize again.";

    /// <summary>Resolves the installed fields without relying on local field ids or user labels.</summary>
    public static HumanJudgmentFieldResolution Resolve(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        int[] Find(string name) => metadata.GetFieldIds()
            .Where(flid => string.Equals(metadata.GetFieldName(flid), name, StringComparison.Ordinal))
            .ToArray();

        if (Find(OldFormatInternalName).Length > 0)
            return new(HumanJudgmentFieldCapability.OldFormat, OldFormatMessage, null);
        var data = Find(InternalName);
        var ids = Find(IdInternalName);
        if (data.Length > 1 || ids.Length > 1)
        {
            var (name, count) = data.Length > 1 ? (InternalName, data.Length) : (IdInternalName, ids.Length);
            return new(HumanJudgmentFieldCapability.Ambiguous,
                $"The project contains {count} definitions named {name}; only one is supported.", null);
        }

        HumanJudgmentFieldSignature SignatureOf(int fieldId) => new(
            metadata.GetOwnClsName(fieldId),
            ((CellarPropertyType)metadata.GetFieldType(fieldId)).ToString(),
            metadata.IsCustom(fieldId),
            metadata.GetFieldWs(fieldId),
            metadata.GetDstClsId(fieldId),
            metadata.GetFieldListRoot(fieldId));
        var signature = data.Length == 1 ? SignatureOf(data[0]) : null;
        var idSignature = ids.Length == 1 ? SignatureOf(ids[0]) : null;
        if (signature is not null && !Compatible(signature))
            return new(HumanJudgmentFieldCapability.Incompatible,
                $"The {InternalName} field has an unsupported schema: " + Describe(InternalName, signature) + ".", signature);
        if (idSignature is not null && !Compatible(idSignature))
            return new(HumanJudgmentFieldCapability.Incompatible,
                $"The {IdInternalName} field has an unsupported schema: " + Describe(IdInternalName, idSignature) + ".", idSignature);
        if (signature is null || idSignature is null)
            return new(HumanJudgmentFieldCapability.Missing,
                $"Project initialization is required because {DeclaringClass}.{(signature is null ? InternalName : IdInternalName)} is missing.",
                null);

        return new HumanJudgmentFieldResolution(HumanJudgmentFieldCapability.Available,
            $"{DeclaringClass}.{InternalName} and {DeclaringClass}.{IdInternalName} are available.", signature)
        { CacheFieldId = data[0], IdFieldId = ids[0] };
    }

    /// <summary>Checks the full reserved signature without using a label or local field id.</summary>
    internal static bool Compatible(HumanJudgmentFieldSignature signature) =>
        string.Equals(signature.DeclaringClass, DeclaringClass, StringComparison.Ordinal) &&
        string.Equals(signature.Type, CellarPropertyType.String.ToString(), StringComparison.Ordinal) &&
        signature.IsCustom &&
        signature.WritingSystemSelector == WritingSystemServices.kwsAnal &&
        signature.DestinationClass == 0 &&
        signature.ListRoot == Guid.Empty;

    private static string Describe(string name, HumanJudgmentFieldSignature signature) =>
        $"{signature.DeclaringClass}.{name} ({signature.Type}, custom={signature.IsCustom}, " +
        $"writing-system selector={signature.WritingSystemSelector}, destination={signature.DestinationClass}, " +
        $"list={signature.ListRoot})";
}
