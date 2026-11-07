using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Review;

/// <summary>Produces explicit diagnostics from a report's pinned inputs and reads retained traces.</summary>
public sealed class ReviewTraceCommands(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Produces or reads one trace only for the exact report, case and side.</summary>
    public CommandOutcome<TracePage> Produce(ReviewTraceRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Validate() is { } invalid) return Refuse(invalid, FailureReason.InvalidArgument);
        try
        {
            var repository = new ReviewReportRepository(_database);
            var stored = repository.ReadManifest(request.ReportId);
            if (stored is null || stored.Summary.Pins.ProjectKey != request.ProjectKey || !stored.DetailAvailable)
                return Refuse("The report or its retained details are unavailable.");
            var pins = stored.Summary.Pins;
            var word = repository.ReadWord(request.ProjectKey, request.ReportId, request.CaseKey, 1).Row;
            var saved = ReviewTraceArtifacts.Find(_database, request.ProjectKey, request.ReportId.Value, request.CaseKey, request.Side);
            if (saved is not null) return Page(saved, request.Cursor, request.Limit, TraceOrigin.Stored);
            var reference = stored.InputReferences.FirstOrDefault(value => value.StartsWith("sha256:", StringComparison.Ordinal));
            if (reference is null) return Refuse("The report has no retained diagnostic inputs.");
            var input = ReviewInputArtifacts.Read(_database, new ReviewInputReference(reference[7..]));
            if (input.Inputs.ProjectKey != request.ProjectKey || input.Inputs.ProposalId != pins.ProposalId ||
                input.Inputs.EvidenceManifestDigest != pins.EvidenceManifestDigest || input.Baseline != pins.BaselineToken)
                return Refuse("The retained diagnostic inputs do not match this report.");
            var checkpoint = pins.Checkpoints.FirstOrDefault(item => item.Side == request.Side);
            var parserDigest = checkpoint?.ParserDigest;
            var grammarDigest = request.Side == Side.Before ? input.BaselineFileDigest : pins.Candidate.GrammarDigest;
            if (parserDigest is null || grammarDigest is null || input.Limits is null ||
                input.Inputs.ParserDigest is not { } capturedParser || capturedParser != parserDigest)
                return Refuse("The report does not pin the diagnostic inputs.");
            var traceId = Hash(JsonSerializer.Serialize(new { request.ProjectKey, Report = request.ReportId.Value,
                request.CaseKey, request.Side, parserDigest, grammarDigest, input.Inputs.ScopeDigest, reference }, ReviewJson.Options))[7..];
            var existing = ReviewTraceArtifacts.Read(_database, request.ProjectKey, request.ReportId.Value, traceId);
            if (existing is not null) return Page(existing, request.Cursor, request.Limit, TraceOrigin.Stored);
            if (request.Cursor is not null) return Refuse("The trace cursor no longer has retained data.");
            var grammar = request.Side == Side.Before ? input.BaselineFile :
                TrialScratchPath.ProjectFile(TrialScratchPath.ForJob(pins.TrialJobId));
            var parser = PanGlossExecutable.TryLocate();
            if (grammar is null || !File.Exists(grammar) || HashFile(grammar) != grammarDigest || parser is null ||
                !File.Exists(parser) || !string.Equals(HashFile(parser), parserDigest, StringComparison.Ordinal))
                return Refuse("The exact grammar or parser for this report is unavailable.");
            var outcome = WordTraceQuery.QueryExact(grammar, word.Word, parser, cancellationToken,
                input.Limits.PerWordParserWorkLimit, input.Limits.PerWordLimit);
            if (!outcome.Succeeded) return CommandOutcome<TracePage>.Refused(outcome.Refusal!);
            var diagnostic = outcome.Value!;
            var (nodes, complete) = ReviewTraceProjection.Capture(diagnostic.Reading.Root);
            var observed = request.Side == Side.Before ? word.Before : word.After;
            var readings = new List<ReadingRow>();
            string? cursor = null;
            var captured = true;
            if (diagnostic.SearchCompletion == TraceSearchCompletion.Complete && observed.Completion == Completion.Complete)
            {
                do
                {
                    var page = repository.ReadChildren(new ReviewChildPageRequest(request.ProjectKey, request.ReportId,
                        "readings", request.CaseKey, cursor, 200) { Side = request.Side });
                    if (page.ListAvailability != ListAvailability.Complete) { captured = false; break; }
                    readings.AddRange(page.Items.OfType<ReadingRow>());
                    cursor = page.NextCursor;
                } while (cursor is not null);
            }
            else captured = false;
            var agreement = captured ? CompareReadings(diagnostic.Reading.Analyses, readings) : ReadingAgreement.Unknown;
            var trace = new RetainedReviewTrace(traceId, request.ProjectKey, request.ReportId.Value, request.CaseKey,
                request.Side, parserDigest, grammarDigest, input.Inputs.ScopeDigest,
                diagnostic.Complete ? "complete" : "stopped", complete, nodes, agreement);
            ReviewTraceArtifacts.Store(_database, trace);
            return Page(trace, null, request.Limit, TraceOrigin.DiagnosticRerun);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or ArgumentException or ReviewDetailUnavailableException)
        { return Refuse(exception.Message); }
    }

    /// <summary>Reads a retained trace without starting a diagnostic or parser.</summary>
    public CommandOutcome<TracePage> Read(string project, ReviewReportId report, string traceId, string? cursor = null, int limit = 50,
        string? parent = null)
    {
        if (limit is < 1 or > 200) return Refuse("Invalid trace page limit.", FailureReason.InvalidArgument);
        try
        {
            var trace = ReviewTraceArtifacts.Read(_database, project, report.Value, traceId);
            return trace is null ? Refuse("The retained trace is unavailable.") : Page(trace, cursor, limit, TraceOrigin.Stored, parent);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or ArgumentException or ReviewDetailUnavailableException)
        { return Refuse(exception.Message); }
    }

    private static CommandOutcome<TracePage> Page(RetainedReviewTrace trace, string? cursor, int limit, TraceOrigin origin,
        string? parent = null)
    {
        if (parent is not null && !trace.Nodes.Any(node => node.Id == parent))
            return Refuse("The trace parent was not found.", FailureReason.InvalidArgument);
        var selected = parent is null ? trace.Nodes : trace.Nodes.Where(node => node.ParentId == parent).ToArray();
        var binding = Convert.ToBase64String(Encoding.UTF8.GetBytes(parent ?? ""));
        var offset = 0;
        if (cursor is not null)
        {
            try
            {
                var value = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':');
                if (value.Length != 3 || value[0] != trace.TraceId || value[1] != binding ||
                    !int.TryParse(value[2], out offset) || offset < 0 || offset >= selected.Count)
                    return InvalidCursor();
            }
            catch (FormatException) { return InvalidCursor(); }
        }
        var parentIds = trace.Nodes.Select(node => node.ParentId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var baseUri = $"motif://projects/{Uri.EscapeDataString(trace.ProjectKey)}/trial-reports/{Uri.EscapeDataString(trace.ReportId)}" +
            "/traces/" + Uri.EscapeDataString(trace.TraceId);
        var items = selected.Skip(offset).Take(limit).Select(node => parentIds.Contains(node.Id) ? node with
        {
            Children = new ReadTarget(TargetKind.Resource, "trace children", baseUri + "?parent=" + Uri.EscapeDataString(node.Id), null, null)
        } : node).ToArray();
        var next = offset + items.Length < selected.Count
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes($"{trace.TraceId}:{binding}:{offset + items.Length}")) : null;
        return CommandOutcome<TracePage>.Success(new TracePage(trace.TraceId, trace.ReportId, trace.CaseKey, trace.Side,
            origin, trace.ParserDigest, trace.GrammarDigest, trace.ScopeDigest, "motif-trace-v1", trace.Completion,
            trace.CaptureComplete, trace.CaptureComplete ? null : "capture-limit", trace.ReadingAgreement, items, next, []));
    }

    private static string Hash(string text) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream)); }
    internal static ReadingAgreement CompareReadings(IReadOnlyList<TraceAnalysis> diagnostic, IReadOnlyList<ReadingRow> saved)
    {
        static string? Key(IEnumerable<(string? Form, string? Msa, string? Infl, string? Guess)> morphs)
        {
            var parts = new List<object>();
            foreach (var (form, msa, infl, guess) in morphs)
            {
                if (!Guid.TryParse(form, out var formId) || formId == Guid.Empty ||
                    !Guid.TryParse(msa, out var msaId) || msaId == Guid.Empty ||
                    infl is not null && (!Guid.TryParse(infl, out var inflId) || inflId == Guid.Empty)) return null;
                string? normalized;
                try { normalized = string.IsNullOrEmpty(guess) ? null : guess.Normalize(NormalizationForm.FormD); }
                catch (ArgumentException) { return null; }
                parts.Add(new { Form = formId.ToString("D"), Msa = msaId.ToString("D"),
                    Infl = infl is null ? null : Guid.Parse(infl).ToString("D"), Guess = normalized });
            }
            return parts.Count == 0 ? null : JsonSerializer.Serialize(parts);
        }
        var before = new HashSet<string>(StringComparer.Ordinal);
        var after = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in saved)
        {
            if (row.IdentityState != IdentityState.Exact || row.Morphs.Any(morph => morph.InflTypeIds.Count > 1))
                return ReadingAgreement.Unknown;
            var key = Key(row.Morphs.Select(morph => (morph.FormId, morph.MsaId, morph.InflTypeIds.FirstOrDefault(), (string?)morph.Form)));
            if (key is null) return ReadingAgreement.Unknown;
            before.Add(key);
        }
        foreach (var row in diagnostic)
        {
            var key = Key(row.Morphs.Select(morph => (morph.FormId, morph.MsaId, morph.InflTypeId, morph.GuessedString)));
            if (key is null) return ReadingAgreement.Unknown;
            after.Add(key);
        }
        return before.SetEquals(after) ? ReadingAgreement.Same : ReadingAgreement.Different;
    }

    private static CommandOutcome<TracePage> InvalidCursor() => CommandOutcome<TracePage>.Refused(
        new Refusal(RefusalCodes.ReviewCursorInvalid, FailureReason.InvalidArgument, "The continuation does not match this trace."));

    private static CommandOutcome<TracePage> Refuse(string message, FailureReason reason = FailureReason.Refused)
    {
        if (reason == FailureReason.InvalidArgument)
            return CommandOutcome<TracePage>.Refused(new Refusal(RefusalCodes.DifferenceInvalidPage,
                FailureReason.InvalidArgument, message));
        return CommandOutcome<TracePage>.Refused(new Refusal(RefusalCodes.TrialTraceUnavailable,
            FailureReason.Refused, message + " Run a new Trial to capture the needed inputs."));
    }
}
