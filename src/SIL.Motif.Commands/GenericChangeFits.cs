using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Model;

namespace SIL.Motif.Commands;

/// <summary>
/// The fit evidence of a pending change that is not about a wordform: a domain that names which check reads it, and
/// the subject the change shows in the list. Staged in an operation's <c>changeFit.generic</c>.
/// </summary>
/// <param name="Domain">The id of the registered check, such as <c>scripture-source</c>.</param>
/// <param name="Subject">What the change is about, as the list names it, such as a Source id.</param>
public sealed record GenericChangeFit(string Domain, string Subject);

/// <summary>
/// The checks of non-wordform pending changes, one per domain. A check reads the saved project and answers the
/// reason the operation no longer fits, or null when it still fits; it never writes.
/// </summary>
internal static class GenericChangeFits
{
    /// <summary>Reads one operation of a Proposal against the saved project.</summary>
    internal delegate string? Check(LcmCache cache, Proposal proposal, OperationEnvelope operation, GenericChangeFit fit);

    private static readonly Dictionary<string, Check> Checks = new(StringComparer.Ordinal);

    /// <summary>Registers the check of a domain; a domain has one.</summary>
    internal static void Register(string domain, Check check)
    {
        lock (Checks)
        {
            if (!Checks.TryAdd(domain, check))
                throw new InvalidOperationException($"A change-fit check is already registered for '{domain}'.");
        }
    }

    /// <summary>The reason the operation no longer fits, or null when it fits.</summary>
    internal static string? Evaluate(LcmCache cache, Proposal proposal, OperationEnvelope operation, JsonElement generic)
    {
        GenericChangeFit? fit;
        try
        {
            fit = JsonSerializer.Deserialize<GenericChangeFit>(generic.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException) { return ChangeFitReasons.FingerprintMalformed; }
        if (fit is null || string.IsNullOrWhiteSpace(fit.Domain) || fit.Subject is null)
            return ChangeFitReasons.FingerprintIncomplete;
        Check? check;
        lock (Checks) Checks.TryGetValue(fit.Domain, out check);
        return check is null ? ChangeFitReasons.FingerprintMalformed : check(cache, proposal, operation, fit);
    }
}

/// <summary>
/// How a composer step appears as a row of the pending list when it is not about a wordform.
/// </summary>
/// <param name="Domain">The id of the registered fit check.</param>
/// <param name="Kind">The change kind the row shows, such as an operation kind.</param>
/// <param name="Subject">What the change is about, such as a Source id.</param>
/// <param name="Summary">One line saying what the change does.</param>
internal sealed record PendingRowSpec(string Domain, string Kind, string Subject, string Summary);
