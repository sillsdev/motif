using System.Globalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

/// <summary>The one refusal for a parser run that Motif stopped because it passed the memory ceiling.</summary>
public static class ParserMemoryRefusal
{
    /// <summary>
    /// Names the limit applied, the peak measured when the platform knows it, and the setting that changes it,
    /// as facts a caller can read without parsing the message.
    /// </summary>
    public static Refusal From(string projectPath, PanGlossMemoryKill kill)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["projectPath"] = projectPath,
            ["limitBytes"] = kill.LimitBytes.ToString(CultureInfo.InvariantCulture),
            ["setting"] = PanGlossMemoryLimit.Variable,
        };
        if (kill.PeakBytes is { } peak) facts["peakBytes"] = peak.ToString(CultureInfo.InvariantCulture);
        return new Refusal(RefusalCodes.AssessParserMemoryLimit, FailureReason.Refused, kill.Describe(), facts);
    }
}
