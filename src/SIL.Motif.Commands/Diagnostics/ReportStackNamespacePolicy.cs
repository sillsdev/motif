namespace SIL.Motif.Commands.Diagnostics;

/// <summary>Identifies namespaces whose stack frames can appear in a Motif problem report.</summary>
public static class ReportStackNamespacePolicy
{
    /// <summary>Returns whether a namespace belongs to Motif, a runtime, or a supported platform library.</summary>
    public static bool IsKnown(string? ns) => ns is not null &&
        (ns.StartsWith("SIL.Motif.", StringComparison.Ordinal) || ns.StartsWith("System", StringComparison.Ordinal) ||
         ns.StartsWith("Microsoft.", StringComparison.Ordinal) || ns.StartsWith("Avalonia.", StringComparison.Ordinal) ||
         ns.StartsWith("SIL.LCModel.", StringComparison.Ordinal) || ns.StartsWith("SIL.Core.", StringComparison.Ordinal) ||
         ns.StartsWith("SIL.WritingSystems.", StringComparison.Ordinal));
}
