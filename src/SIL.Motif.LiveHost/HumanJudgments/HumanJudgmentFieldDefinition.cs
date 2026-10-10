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

    /// <summary>More than one metadata field uses the reserved internal name.</summary>
    Ambiguous,
}

/// <summary>The schema details used to decide whether a reserved field is compatible.</summary>
public sealed record HumanJudgmentFieldSignature(
    string DeclaringClass,
    string Type,
    bool IsCustom,
    int WritingSystemSelector,
    int DestinationClass,
    Guid ListRoot)
{
    /// <summary>Describes the definition without relying on its cache-local field id.</summary>
    public string Description =>
        $"{DeclaringClass}.{HumanJudgmentFieldDefinition.InternalName} ({Type}, " +
        $"writing-system selector {WritingSystemSelector}, destination {DestinationClass}, list {ListRoot})";
}

/// <summary>The reserved field definitions found in one loaded project.</summary>
public sealed record HumanJudgmentFieldInspection(
    HumanJudgmentFieldState State,
    IReadOnlyList<HumanJudgmentFieldSignature> Definitions);

/// <summary>Defines and inspects the one custom field that stores Motif human judgments.</summary>
public static class HumanJudgmentFieldDefinition
{
    /// <summary>The exact metadata name used to find the reserved field.</summary>
    public const string InternalName = "MotifHumanJudgment";

    /// <summary>The LibLCM class that declares the reserved field.</summary>
    public const string DeclaringClass = "RnGenericRec";

    /// <summary>The initial user-facing label assigned when the field is created.</summary>
    public const string UserLabel = "Motif human judgment";

    /// <summary>Finds every exact-name definition and checks its persisted schema.</summary>
    public static HumanJudgmentFieldInspection Inspect(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var definitions = metadata.GetFieldIds()
            .Where(flid => string.Equals(metadata.GetFieldName(flid), InternalName, StringComparison.Ordinal))
            .Select(flid => new HumanJudgmentFieldSignature(
                metadata.GetOwnClsName(flid),
                ((CellarPropertyType)metadata.GetFieldType(flid)).ToString(),
                metadata.IsCustom(flid),
                metadata.GetFieldWs(flid),
                metadata.GetDstClsId(flid),
                metadata.GetFieldListRoot(flid)))
            .ToArray();

        var state = definitions.Length switch
        {
            0 => HumanJudgmentFieldState.Missing,
            > 1 => HumanJudgmentFieldState.Ambiguous,
            _ when IsCompatible(cache, definitions[0]) => HumanJudgmentFieldState.Compatible,
            _ => HumanJudgmentFieldState.Incompatible,
        };
        return new HumanJudgmentFieldInspection(state, definitions);
    }

    /// <summary>Adds the definition in its own closed, non-undoable unit of work.</summary>
    public static void CreateDefinition(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var writingSystems = cache.ServiceLocator.WritingSystems.AnalysisWritingSystems;
        if (writingSystems.Count == 0)
            throw new InvalidOperationException("The project has no analysis writing system.");

        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            metadata.AddCustomField(
                DeclaringClass,
                InternalName,
                CellarPropertyType.String,
                destinationClass: 0,
                UserLabel,
                WritingSystemServices.kwsAnal,
                Guid.Empty));
    }

    private static bool IsCompatible(LcmCache cache, HumanJudgmentFieldSignature definition) =>
        string.Equals(definition.DeclaringClass, DeclaringClass, StringComparison.Ordinal) &&
        string.Equals(definition.Type, CellarPropertyType.String.ToString(), StringComparison.Ordinal) &&
        definition.IsCustom &&
        definition.WritingSystemSelector == WritingSystemServices.kwsAnal &&
        definition.DestinationClass == 0 &&
        definition.ListRoot == Guid.Empty;
}
