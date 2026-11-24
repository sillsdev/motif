using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

internal static class WhyLostRenderer
{
    public static string Render(WhyLostResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Why lost, {response.Word}: Baseline against proposal {response.ProposalId}" +
            (response.TrialJobId is { } job ? $" (Trial job {job})." : "."));
        text.AppendLine($"Baseline {Readings(response.Baseline.Outputs)}, Trial copy {Readings(response.Trial.Outputs)}: " +
            $"lost {N(response.Lost.Count)}, gained {N(response.Gained.Count)}, kept {N(response.Kept.Count)}.");
        for (var index = 0; index < response.Lost.Count; index++)
        {
            var lost = response.Lost[index];
            text.AppendLine();
            text.AppendLine($"Lost Reading {N(index + 1)}: {Describe(lost)}");
            foreach (var cause in lost.Causes)
            {
                text.AppendLine("  " + cause.Sentence);
            }
        }
        foreach (var gained in response.Gained) text.AppendLine($"Gained: {Describe(gained)}");
        foreach (var note in response.Notes) text.AppendLine("Note: " + note);
        return text.ToString();
    }

    private static string Readings(int count) => N(count) + (count == 1 ? " Reading" : " Readings");

    private static string Describe(WhyLostReading reading) => string.Join(" + ", reading.Morphs.Select(Morph));

    private static string Morph(RejectionTraceMorph morph)
    {
        var parts = new List<string>();
        if (Id(morph.Entry) is { } entry) parts.Add("entry " + entry);
        if (Id(morph.Msa) is { } msa) parts.Add("msa " + msa);
        foreach (var form in morph.Forms)
            if (Id(form) is { } formId) parts.Add("form " + formId);
        return parts.Count == 0 ? "(no authored identity)" : "[" + string.Join(" ", parts) + "]";
    }

    private static string? Id(RejectionTraceObject? value) =>
        value?.Ids.FirstOrDefault(id => id.IsSubject)?.Id;

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}
