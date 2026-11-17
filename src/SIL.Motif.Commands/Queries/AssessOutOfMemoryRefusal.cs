using System.Globalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>The one refusal for an Assess that Motif itself could not hold in memory.</summary>
public static class AssessOutOfMemoryRefusal
{
    /// <summary>Config key that removes the per-object timing cost, named so a caller can act without reading docs.</summary>
    public const string Setting = "collect";

    /// <summary>
    /// Tells the caller nothing is damaged and what cheapens the run: collecting without ObjectTiming, or fewer words.
    /// </summary>
    /// <param name="projectPath">The project that was being assessed.</param>
    /// <param name="wordCount">The number of words requested, when the caller named them.</param>
    public static Refusal From(string projectPath, int? wordCount)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["projectPath"] = projectPath,
            ["setting"] = Setting,
        };
        if (wordCount is { } words) facts["words"] = words.ToString(CultureInfo.InvariantCulture);
        return new Refusal(RefusalCodes.AssessOutOfMemory, FailureReason.Refused,
            "Assess ran out of memory, and no Assessment was saved. Nothing in the project or the store is damaged. " +
            "Per-object timing is the usual cause: set `collect = [\"ParseTime\", \"Correctness\"]` in the project's " +
            ".motif.toml to collect without ObjectTiming, or assess fewer words.", facts);
    }
}
