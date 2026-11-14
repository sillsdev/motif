using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

/// <summary>
/// The fit check of a staged Source change: an add still fits while no Source holds the id, and an edit still fits
/// while its Source is in the list, or is created by an add in the same Proposal. It reads the saved project only.
/// </summary>
internal static class SourceChangeFit
{
    /// <summary>The domain the Source composers name in the fit evidence of their pending rows.</summary>
    internal const string Domain = "scripture-source";

#pragma warning disable CA2255 // Load-time registration makes the check available before any pending list is read.
    [ModuleInitializer]
    internal static void Register() => GenericChangeFits.Register(Domain, Check);
#pragma warning restore CA2255

    private static string? Check(LcmCache cache, Proposal proposal, OperationEnvelope operation, GenericChangeFit fit)
    {
        try
        {
            return operation.Kind == ScriptureSourceOperationKinds.Create
                ? CheckCreate(cache, operation)
                : CheckEdit(cache, proposal, operation, fit.Subject);
        }
        catch (InvalidOperationException exception)
        {
            return $"The Motif Sources list cannot be read: {exception.Message}";
        }
    }

    private static string? CheckCreate(LcmCache cache, OperationEnvelope operation)
    {
        var id = operation.After is { ValueKind: JsonValueKind.Object } after &&
                 after.TryGetProperty("id", out var value) ? value.GetString() : null;
        if (string.IsNullOrEmpty(id)) return ChangeFitReasons.FingerprintIncomplete;
        return ScriptureSourceLookup.FindItem(cache, id) is null ? null
            : $"A Source with the id '{id}' is now in the project, so this one can no longer be added.";
    }

    private static string? CheckEdit(LcmCache cache, Proposal proposal, OperationEnvelope operation, string subject)
    {
        if (operation.Target is not { } target) return ChangeFitReasons.FingerprintIncomplete;
        if (proposal.Operations.Any(other => other.Kind == ScriptureSourceOperationKinds.Create && other.EntityId == target))
            return null;
        var list = ScriptureSourceLookup.FindList(cache);
        var present = list?.PossibilitiesOS.Any(item => item.Guid == target.ToGuid()) == true;
        return present ? null : $"The Source '{subject}' is no longer in the project's Source list.";
    }
}
