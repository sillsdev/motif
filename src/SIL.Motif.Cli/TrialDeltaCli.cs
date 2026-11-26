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
        ["project", "json", "autonomous", "proposal", "job", "classes", "write-manifests", "alias-created", "keep-case", "limit", "offset", "page",
         "expect-retired", "expect-added", "max-doubled", "max-doubled-frac", "write-receipt"];

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
        var held = Expectations(flags, out var expectationRefusal);
        if (expectationRefusal is not null) return CommandTextRenderer.Render(CommandOutcome<ReadingDelta>.Refused(expectationRefusal), asJson);
        if (held is not null)
        {
            if (flags.ContainsKey("write-manifests")) return Fail(out usage, "usage");
            var receiptPath = flags.GetValueOrDefault("write-receipt");
            if (receiptPath is "true" or "") return Fail(out usage, "usage");
            var alias = flags.GetValueOrDefault("alias-created", "true");
            if (alias is not ("true" or "false")) return Fail(out usage, "usage");
            var checkedDelta = TrialReadingDelta.Check(request, held, new ManifestOptions(alias == "true", flags.GetValueOrDefault("keep-case") == "true"), receiptPath);
            var printed = CommandTextRenderer.Render(checkedDelta, asJson);
            // A gate that is not green fails the command, so a script reads the exit code; the state is in the result.
            return printed.ExitCode == 0 && checkedDelta.Value?.Expectations is { State: not ReadingExpectationStates.Green } gate
                ? printed with { ExitCode = FailureEnvelope.TrialLossExitCode }
                : printed;
        }
        if (flags.ContainsKey("write-receipt")) return Fail(out usage, "usage");
        if (flags.TryGetValue("write-manifests", out var directory))
        {
            if (directory is "true" or "") return Fail(out usage, "usage");
            var alias = flags.GetValueOrDefault("alias-created", "true");
            if (alias is not ("true" or "false")) return Fail(out usage, "usage");
            var options = new ManifestOptions(alias == "true", flags.GetValueOrDefault("keep-case") == "true");
            return CommandTextRenderer.Render(TrialReadingDelta.ReadAndWrite(request, directory, options), asJson);
        }
        if (flags.ContainsKey("alias-created") || flags.ContainsKey("keep-case")) return Fail(out usage, "usage");
        var outcome = TrialReadingDelta.Read(request);
        return CommandTextRenderer.Render(outcome, asJson);
    }

    // Null expectations with no refusal means none of the four flags was given.
    private static TrialExpectations? Expectations(IReadOnlyDictionary<string, string> flags, out Refusal? refusal)
    {
        refusal = null;
        if (!new[] { "expect-retired", "expect-added", "max-doubled", "max-doubled-frac" }.Any(flags.ContainsKey)) return null;
        var retired = Rows(flags, "expect-retired", ref refusal);
        var added = refusal is null ? Rows(flags, "expect-added", ref refusal) : null;
        long? maxDoubled = null;
        double? fraction = null;
        if (refusal is null && flags.TryGetValue("max-doubled", out var count))
        {
            if (long.TryParse(count, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) maxDoubled = parsed;
            else refusal = new Refusal(RefusalCodes.TrialDeltaRequestInvalid, FailureReason.InvalidArgument, "--max-doubled must be zero or more.");
        }
        if (refusal is null && flags.TryGetValue("max-doubled-frac", out var text))
        {
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) fraction = parsed;
            else refusal = new Refusal(RefusalCodes.TrialDeltaRequestInvalid, FailureReason.InvalidArgument, "--max-doubled-frac must be between 0 and 1.");
        }
        return refusal is null ? new TrialExpectations(retired, added, maxDoubled, fraction) : null;
    }

    private static IReadOnlyList<string>? Rows(IReadOnlyDictionary<string, string> flags, string flag, ref Refusal? refusal)
    {
        if (!flags.TryGetValue(flag, out var path)) return null;
        if (path is "true" or "" || !File.Exists(path))
        {
            refusal = new Refusal(RefusalCodes.TrialDeltaRequestInvalid, FailureReason.InvalidArgument,
                $"--{flag} names a manifest file that exists; '{path}' does not.");
            return null;
        }
        return File.ReadAllLines(path);
    }

    private static CommandResult? Fail(out string? usage, string value)
    {
        usage = value;
        return null;
    }
}
