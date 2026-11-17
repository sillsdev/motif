using System.Runtime.CompilerServices;
using SIL.LCModel;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

/// <summary>
/// The fit check of a staged approval or clear: its Source must still be in the list. The analysis operations staged
/// beside an approval carry the same evidence, and they fit while the approval does.
/// </summary>
internal static class SourceApprovalFit
{
    /// <summary>The domain the approval composer names in the fit evidence of its pending rows.</summary>
    internal const string Domain = "scripture-approval";

#pragma warning disable CA2255 // Load-time registration makes the check available before any pending list is read.
    [ModuleInitializer]
    internal static void Register() => GenericChangeFits.Register(Domain, Check);
#pragma warning restore CA2255

    private static string? Check(LcmCache cache, Proposal proposal, OperationEnvelope operation, GenericChangeFit fit)
    {
        if (operation.Kind is not (ScriptureApprovalOperationKinds.Approve or ScriptureApprovalOperationKinds.Clear)) return null;
        if (operation.Target is not { } target) return ChangeFitReasons.FingerprintIncomplete;
        try
        {
            return ScriptureSourceLookup.FindList(cache)?.PossibilitiesOS.Any(item => item.Guid == target.ToGuid()) == true
                ? null : $"The Source '{fit.Subject}' is no longer in the project's Source list.";
        }
        catch (InvalidOperationException exception)
        {
            return $"The Motif Sources list cannot be read: {exception.Message}";
        }
    }
}
