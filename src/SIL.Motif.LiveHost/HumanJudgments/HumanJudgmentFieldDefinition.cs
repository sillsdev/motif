using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;

namespace SIL.Motif.LiveHost.HumanJudgments;

public enum HumanJudgmentFieldState
{
    /// <summary>No metadata field uses the reserved internal name.</summary>
    Missing,

    /// <summary>Exactly one persisted field has the required schema.</summary>
    Compatible,

    /// <summary>One reserved-name field has an unsupported declaring class or schema.</summary>
    Incompatible,

    /// <summary>More than one metadata field uses a reserved internal name.</summary>
    Ambiguous,

    /// <summary>The project still has the single-field format that held a base64 marker.</summary>
    OldFormat,
}

/// <summary>The schema details used to decide whether a reserved field is compatible.</summary>
public sealed record HumanJudgmentFieldSignature(
    string Name,
    string DeclaringClass,
    string Type,
    bool IsCustom,
    int WritingSystemSelector,
    int DestinationClass,
    Guid ListRoot)
{
    /// <summary>Describes the definition without relying on its cache-local field id.</summary>
    public string Description =>
        $"{DeclaringClass}.{Name} ({Type}, " +
        $"writing-system selector {WritingSystemSelector}, destination {DestinationClass}, list {ListRoot})";
}

/// <summary>The reserved field definitions found in one loaded project.</summary>
public sealed record HumanJudgmentFieldInspection(
    HumanJudgmentFieldState State,
    IReadOnlyList<HumanJudgmentFieldSignature> Definitions);

/// <summary>Defines and inspects the two custom fields that store a Motif human judgment.</summary>
public static class HumanJudgmentFieldDefinition
{
    /// <summary>The exact metadata name of the field holding the hash-sealed judgment data.</summary>
    public const string InternalName = "MotifJudgmentData";

    /// <summary>The exact metadata name of the field holding the visible judgment ID.</summary>
    public const string IdInternalName = "MotifJudgmentId";

    /// <summary>The metadata name of the retired single field that held a base64 marker.</summary>
    public const string OldFormatInternalName = "MotifHumanJudgment";

    /// <summary>The LibLCM class that declares the reserved fields.</summary>
    public const string DeclaringClass = "RnGenericRec";

    /// <summary>Both reserved names, as the initialization result reports them.</summary>
    public const string AllInternalNames = InternalName + ", " + IdInternalName;

    /// <summary>The label of the data field, which tells a person not to edit it.</summary>
    public const string UserLabel = "Motif data (do not edit)";

    /// <summary>The label of the visible ID field.</summary>
    public const string IdUserLabel = "Motif judgment ID";

    /// <summary>Finds every exact-name definition and checks its persisted schema.</summary>
    public static HumanJudgmentFieldInspection Inspect(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        HumanJudgmentFieldSignature[] Find(string name) => metadata.GetFieldIds()
            .Where(flid => string.Equals(metadata.GetFieldName(flid), name, StringComparison.Ordinal))
            .Select(flid => new HumanJudgmentFieldSignature(
                name,
                metadata.GetOwnClsName(flid),
                ((CellarPropertyType)metadata.GetFieldType(flid)).ToString(),
                metadata.IsCustom(flid),
                metadata.GetFieldWs(flid),
                metadata.GetDstClsId(flid),
                metadata.GetFieldListRoot(flid)))
            .ToArray();

        var data = Find(InternalName);
        var id = Find(IdInternalName);
        var old = Find(OldFormatInternalName);
        var definitions = data.Concat(id).Concat(old).ToArray();
        var state = old.Length > 0 ? HumanJudgmentFieldState.OldFormat
            : data.Length > 1 || id.Length > 1 ? HumanJudgmentFieldState.Ambiguous
            : data.Concat(id).Any(definition => !IsCompatible(definition)) ? HumanJudgmentFieldState.Incompatible
            : data.Length == 0 || id.Length == 0 ? HumanJudgmentFieldState.Missing
            : HumanJudgmentFieldState.Compatible;
        return new HumanJudgmentFieldInspection(state, definitions);
    }

    /// <summary>Adds each missing definition in its own closed, non-undoable unit of work.</summary>
    public static void CreateDefinition(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var writingSystems = cache.ServiceLocator.WritingSystems.AnalysisWritingSystems;
        if (writingSystems.Count == 0)
            throw new InvalidOperationException("The project has no analysis writing system.");

        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        foreach (var (name, label) in new[] { (InternalName, UserLabel), (IdInternalName, IdUserLabel) })
        {
            if (metadata.GetFieldIds().Any(flid =>
                    string.Equals(metadata.GetFieldName(flid), name, StringComparison.Ordinal)))
                continue;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                metadata.AddCustomField(
                    DeclaringClass,
                    name,
                    CellarPropertyType.String,
                    destinationClass: 0,
                    label,
                    WritingSystemServices.kwsAnal,
                    Guid.Empty));
        }
    }

    private static bool IsCompatible(HumanJudgmentFieldSignature definition) =>
        string.Equals(definition.DeclaringClass, DeclaringClass, StringComparison.Ordinal) &&
        string.Equals(definition.Type, CellarPropertyType.String.ToString(), StringComparison.Ordinal) &&
        definition.IsCustom &&
        definition.WritingSystemSelector == WritingSystemServices.kwsAnal &&
        definition.DestinationClass == 0 &&
        definition.ListRoot == Guid.Empty;
}
