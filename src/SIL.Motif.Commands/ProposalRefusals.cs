using System.Collections.Generic;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Runner.Execution;

namespace SIL.Motif.Commands;

/// <summary>The one place a typed Runner failure becomes a <see cref="Refusal"/>.</summary>
internal static class ProposalRefusals
{
    /// <summary>
    /// The refusal for a Runner failure: its code, reason and message, with the caller's extra facts and then
    /// the failure's own facts, which win a clash because they describe what actually failed.
    /// </summary>
    public static Refusal ToRefusal(ProposalRefusalException exception, params (string Key, string? Value)[] extraFacts)
    {
        var facts = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var (key, value) in extraFacts)
        {
            if (value is not null) facts[key] = value;
        }
        foreach (var (key, value) in exception.Facts) facts[key] = value;
        return new Refusal(exception.Code, exception.Reason, exception.Message, facts);
    }
}
