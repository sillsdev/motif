using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

/// <summary>Renders a Trial receipt's phases as the table <c>trial --verbose</c> prints.</summary>
public static class TrialPhaseTable
{
    /// <summary>One line per phase, in the receipt's order, then the total of the phases listed.</summary>
    public static string Render(IReadOnlyList<TrialPhase> phases, params TrialPhase[] extra)
    {
        ArgumentNullException.ThrowIfNull(phases);
        var all = phases.Concat(extra).ToArray();
        var width = all.Max(phase => phase.Name.Length);
        var table = new StringBuilder("Trial phases (seconds):\n");
        foreach (var phase in all)
            table.Append("  ").Append(phase.Name.PadRight(width)).Append(' ')
                .Append(phase.Seconds.ToString("0.000", CultureInfo.InvariantCulture).PadLeft(9))
                .Append(phase.Note is null ? string.Empty : "  (" + phase.Note + ")")
                .Append(phase.Detail is null ? string.Empty : "  [" + phase.Detail + "]").Append('\n');
        table.Append("  ").Append("total".PadRight(width)).Append(' ')
            .Append(all.Sum(phase => phase.Seconds).ToString("0.000", CultureInfo.InvariantCulture).PadLeft(9)).Append('\n');
        return table.ToString();
    }
}
