using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Cli;

/// <summary>Reads the flags of <c>proposal trial delta</c>, runs the read and writes any manifests it was asked for.</summary>
internal static class TrialDeltaCli
{
    private static readonly string[] Allowed =
        ["project", "json", "autonomous", "proposal", "job", "classes", "write-manifests", "limit", "offset", "page"];

    /// <summary>The result, or null with <paramref name="usage"/> set when the flags do not form a request.</summary>
    internal static CommandResult? Run(IReadOnlyDictionary<string, string> flags, bool asJson, out string? usage)
    {
        usage = null;
        if (!flags.TryGetValue("project", out var project) || flags.Keys.Any(flag => !Allowed.Contains(flag)))
            return Fail(out usage, "usage");
        var (window, refusal) = PagingFlags.Read(flags, 20, allowAll: false);
        if (refusal is not null) return CommandTextRenderer.Render(CommandOutcome<ReadingDelta>.Refused(refusal), asJson);
        var classes = flags.TryGetValue("classes", out var text)
            ? text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null;
        var request = new ReadTrialDeltaRequest(project, flags.GetValueOrDefault("job"), flags.GetValueOrDefault("proposal"),
            classes, window.Offset, window.Limit);
        var outcome = flags.TryGetValue("write-manifests", out var directory) && directory != "true"
            ? TrialReadingDelta.ReadAndWrite(request, directory) : TrialReadingDelta.Read(request);
        return CommandTextRenderer.Render(outcome, asJson);
    }

    private static CommandResult? Fail(out string? usage, string value)
    {
        usage = value;
        return null;
    }
}
