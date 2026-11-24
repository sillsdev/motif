using System.Globalization;
using System.Text.Json;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Explains one parse by asking the PanGloss that parses to say where each candidate path stopped, then naming the
/// grammar objects in Motif's canonical ids. It reads the project and changes nothing; it never runs a tracing
/// parser of its own.
/// </summary>
public static class RejectionTraceQuery
{
    private const string ErrorLinePrefix = "PANGLOSS_DIAGNOSTIC_ERROR ";
    private const int MaxWordScalars = 256;

    /// <summary>Traces one word against the Baseline or a Trial copy with the located parser.</summary>
    public static CommandOutcome<RejectionTraceResponse> Parse(
        ParseTraceRequest request, CancellationToken cancellationToken = default) =>
        Parse(request, PanGlossExecutable.TryLocate(), cancellationToken);

    /// <summary>Traces one word with an explicitly selected parser.</summary>
    public static CommandOutcome<RejectionTraceResponse> Parse(
        ParseTraceRequest request, string? parserPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Check(request.Word, request.At, request.MaxPaths) is { } invalid) return invalid;
        using var invoker = new PanGlossInvoker(parserPath);
        return Parse(request, invoker, cancellationToken);
    }

    internal static CommandOutcome<RejectionTraceResponse> Parse(
        ParseTraceRequest request, IPanGlossInvoker invoker, CancellationToken cancellationToken)
    {
        if (Check(request.Word, request.At, request.MaxPaths) is { } invalid) return invalid;
        var maxPaths = request.MaxPaths ?? RejectionTraceLimits.DefaultMaxPaths;
        var proposalId = ProposalOf(request.At);
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            using var parseLease = ProjectParseLease.TryAcquire(database);
            if (parseLease is null)
                return CommandOutcome<RejectionTraceResponse>.Refused(ProjectParseLease.BusyRefusal());
            var resolved = TracedGrammar.Resolve(database, project, proposalId);
            if (!resolved.Succeeded) return CommandOutcome<RejectionTraceResponse>.Refused(resolved.Refusal!);
            return Run(invoker, resolved.Value!.FwDataPath, request.ProjectPath, request.Word, maxPaths,
                resolved.Value.TracedAgainst, cancellationToken);
        });
    }

    /// <summary>Runs the capture on an exact grammar and projects it; the seam the tests drive without a project.</summary>
    internal static CommandOutcome<RejectionTraceResponse> Run(
        IPanGlossInvoker invoker, string grammarPath, string projectPath, string word, int maxPaths,
        string tracedAgainst, CancellationToken cancellationToken)
    {
        var outcome = invoker.RunAsync(new PanGlossRequest.RejectionTrace(grammarPath, word, maxPaths),
            "rejection-trace:" + word, cancellationToken).GetAwaiter().GetResult();
        return Shape(outcome, projectPath, word, maxPaths, tracedAgainst);
    }

    internal static CommandOutcome<RejectionTraceResponse> Shape(
        PanGlossOutcome outcome, string projectPath, string word, int maxPaths, string tracedAgainst)
    {
        switch (outcome)
        {
            case PanGlossOutcome.Completed completed:
                if (!RejectionTraceDocument.TryRead(completed.Output, out var document, out var error))
                    return Refuse(RefusalCodes.RejectionTraceMalformed, FailureReason.Refused,
                        "PanGloss's rejection trace could not be read: " + error);
                if (document!.Footer.TryGetProperty("status", out var status) && status.GetString() != "complete")
                    return Refuse(RefusalCodes.RejectionTraceCaptureLimit, FailureReason.Refused,
                        "PanGloss did not finish the rejection trace" + FooterError(document.Footer) + ".");
                return CommandOutcome<RejectionTraceResponse>.Success(
                    RejectionTraceProjection.Project(document, word, tracedAgainst, maxPaths));
            case PanGlossOutcome.Cancelled:
                return Refuse(RefusalCodes.WordTraceCancelled, FailureReason.Cancelled, "The trace was cancelled.");
            case PanGlossOutcome.MemoryLimit memory:
                return CommandOutcome<RejectionTraceResponse>.Refused(ParserMemoryRefusal.From(projectPath, memory.Kill));
            case PanGlossOutcome.Refused refused:
                return TypedError(refused.StandardError) is { } typed
                    ? Refuse(RefusalCodes.RejectionTraceCaptureRefused, FailureReason.Refused,
                        $"PanGloss refused the rejection trace ({typed.Code}): {typed.Message}",
                        new Dictionary<string, string> { ["parserCode"] = typed.Code })
                    : CommandOutcome<RejectionTraceResponse>.Refused(
                        ParserExecutionRefusal.From(RefusalCodes.WordTraceParserRefused, projectPath, refused));
            case PanGlossOutcome.Unavailable unavailable:
                return CommandOutcome<RejectionTraceResponse>.Refused(new Refusal(RefusalCodes.WordTraceParserUnavailable,
                    FailureReason.Refused, unavailable.Detail, ParserNotFoundFact.Mark(null, unavailable.ExecutableMissing)));
            default:
                return Refuse(RefusalCodes.RejectionTraceCaptureLimit, FailureReason.Refused, outcome.Message);
        }
    }

    private static (string Code, string Message)? TypedError(string standardError)
    {
        foreach (var line in standardError.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(ErrorLinePrefix, StringComparison.Ordinal)) continue;
            try
            {
                using var json = JsonDocument.Parse(trimmed[ErrorLinePrefix.Length..]);
                var code = json.RootElement.GetProperty("code").GetString();
                var message = json.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
                if (code is not null) return (code, message ?? "no message");
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return null;
            }
        }
        return null;
    }

    private static string FooterError(JsonElement footer) =>
        footer.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object &&
        e.TryGetProperty("code", out var code) ? $" ({code.GetString()})" : string.Empty;

    private static string? ProposalOf(string? at) =>
        string.IsNullOrWhiteSpace(at) || at == "baseline" ? null : at;

    private static CommandOutcome<RejectionTraceResponse>? Check(string subject, string? at, int? maxPaths)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.EnumerateRunes().Count() > MaxWordScalars)
            return Refuse(RefusalCodes.RejectionTraceRequestInvalid, FailureReason.InvalidArgument,
                $"Give one word of 1 to {MaxWordScalars} characters.");
        if (maxPaths is < 1 or > RejectionTraceLimits.MaximumPaths)
            return Refuse(RefusalCodes.RejectionTraceRequestInvalid, FailureReason.InvalidArgument,
                $"The cap on paths must be 1 to {RejectionTraceLimits.MaximumPaths}; it was {maxPaths!.Value.ToString(CultureInfo.InvariantCulture)}.");
        if (ProposalOf(at) is { } proposal && !CanonicalId.TryParse(proposal, out _))
            return Refuse(RefusalCodes.RejectionTraceUnsupported, FailureReason.Refused,
                $"'{at}' is neither 'baseline' nor a Proposal id. Revision digests cannot be resolved yet.");
        return null;
    }

    /// <summary>The refusal for a path cap that is not a whole number in range.</summary>
    public static CommandOutcome<RejectionTraceResponse> InvalidCap(string text) =>
        Refuse(RefusalCodes.RejectionTraceRequestInvalid, FailureReason.InvalidArgument,
            $"The cap on paths must be 1 to {RejectionTraceLimits.MaximumPaths}; it was '{text}'.");

    private static CommandOutcome<RejectionTraceResponse> Refuse(
        string code, FailureReason reason, string message, IReadOnlyDictionary<string, string>? facts = null) =>
        CommandOutcome<RejectionTraceResponse>.Refused(new Refusal(code, reason, message, facts));
}
