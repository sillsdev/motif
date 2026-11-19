using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Commands.Review;

/// <summary>
/// Compares the retained Trial evidence of several Proposals form by form. It reads each Proposal's newest completed
/// Trial export, measures nothing and changes nothing. The comparison is each Trial's own before against its own after;
/// it is not measured against approved analyses.
/// </summary>
public static class ProposalCompareReads
{
    private static readonly Cell Unchanged = new(true, true, [], []);
    private static readonly Cell Absent = new(false, false, [], []);

    /// <summary>Reads the summary, and one page of form rows when the request names a page.</summary>
    public static CommandOutcome<ProposalComparison> Read(CompareProposalsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Page is < 1)
            return Refuse(RefusalCodes.EvidenceInvalidPage, FailureReason.InvalidArgument,
                "The comparison page must be a positive integer.");
        var ids = new List<CanonicalId>();
        foreach (var text in request.ProposalIds ?? [])
        {
            if (!CanonicalId.TryParse(text, out var id) || ids.Contains(id))
                return Refuse(RefusalCodes.ProposalCompareProposalsInvalid, FailureReason.InvalidArgument,
                    "Name each Proposal once, by its Proposal ID.");
            ids.Add(id);
        }
        if (ids.Count is < 2 or > ProposalComparison.MaxProposals)
            return Refuse(RefusalCodes.ProposalCompareProposalsInvalid, FailureReason.InvalidArgument,
                $"Compare two to {ProposalComparison.MaxProposals} Proposals in one call.");

        var absent = CommandOutcome<ProposalComparison>.Refused(new Refusal(RefusalCodes.ProposalCompareTrialMissing,
            FailureReason.Refused, "The project has no Motif store, so no Trial evidence to compare."));
        return ProjectStoreCommand.ReadExisting(request.ProjectPath, absent, (database, project) =>
        {
            try { return Compare(database, project, ids, request.At, request.Page); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                UnauthorizedAccessException or JsonException)
            {
                return Refuse(RefusalCodes.ProposalCompareTrialMissing, FailureReason.Refused,
                    "A retained Trial evidence export could not be read.");
            }
        });
    }

    private static CommandOutcome<ProposalComparison> Compare(MotifDatabase database,
        SIL.Motif.Contract.Projects.ProjectLocator project, List<CanonicalId> ids, string? at, int? page)
    {
        var columns = new List<(CanonicalId Proposal, TrialExportReceipt Receipt)>();
        foreach (var id in ids)
        {
            var job = Trials.JobsFor(database, project, id).LastOrDefault(item => item.Status == JobStatus.Completed);
            var receipt = job is null ? null : TrialExportArtifact.Read(database, job.JobId);
            if (receipt is null)
                return Refuse(RefusalCodes.ProposalCompareTrialMissing, FailureReason.Refused,
                    "A Proposal has no completed Trial with a retained evidence export to compare.",
                    new() { ["proposalId"] = id.Value });
            columns.Add((id, receipt));
        }
        if (at is not null)
            foreach (var (id, receipt) in columns)
                if (TrialExportArtifact.Read(database, receipt.JobId, at) is null)
                    return Refuse(RefusalCodes.ProposalCompareAtMismatch, FailureReason.Refused,
                        "A Trial was not measured on the requested Baseline or Proposal revision.",
                        new() { ["proposalId"] = id.Value });
        if (columns.Select(column => column.Receipt.BaselineBundle).Distinct(StringComparer.Ordinal).Count() > 1)
            return Refuse(RefusalCodes.ProposalCompareBaselineMismatch, FailureReason.Refused,
                "These Trials were measured on different Baselines, so their gains and losses are not comparable. " +
                "Run a Trial for each Proposal on the current Baseline.",
                columns.ToDictionary(column => column.Proposal.Value, column => Short(column.Receipt.BaselineBundle)));

        var forms = new Dictionary<(string Ws, string Key), FormAccumulator>();
        for (var index = 0; index < columns.Count; index++)
            Collect(database, columns[index].Receipt, index, columns.Count, forms);

        var counts = new int[columns.Count, 5];
        var rows = new List<CompareRow>();
        var diverging = 0;
        foreach (var form in forms.Values)
        {
            var cells = form.Cells.Select(cell => cell ?? Absent).ToArray();
            var trivial = true;
            for (var index = 0; index < cells.Length; index++)
            {
                var cell = cells[index];
                if (!cell.Measured) { counts[index, 3]++; trivial = false; continue; }
                if (cell.Gained.Length > 0) counts[index, 0]++;
                if (cell.Lost.Length > 0) counts[index, 1]++;
                if (cell.Gained.Length == 0 && cell.Lost.Length == 0) counts[index, 2]++; else trivial = false;
                if (!cell.Complete) { counts[index, 4]++; trivial = false; }
            }
            if (trivial) continue;
            var diverges = cells.Skip(1).Any(cell => !cell.SameObservation(cells[0]));
            if (diverges) diverging++;
            rows.Add(new CompareRow(form.WritingSystemId, form.Form, form.FormKey, diverges,
                cells.Select(cell => new CompareCell(cell.Measured, cell.Complete, cell.Gained, cell.Lost)).ToArray()));
        }
        rows.Sort((left, right) =>
        {
            var order = right.Diverges.CompareTo(left.Diverges);
            if (order == 0) order = string.CompareOrdinal(left.WritingSystemId ?? "", right.WritingSystemId ?? "");
            return order != 0 ? order : string.CompareOrdinal(left.FormKey, right.FormKey);
        });

        var summaryColumns = columns.Select((column, index) => new CompareColumn(column.Proposal.Value,
            counts[index, 0], counts[index, 1],
            counts[index, 2], counts[index, 3], counts[index, 4])).ToArray();
        var limitations = new List<CompareLimitation>();
        var notMeasured = summaryColumns.Sum(column => column.NotMeasured);
        if (notMeasured > 0) limitations.Add(new(CompareLimitation.NotMeasured, notMeasured));
        var incomplete = columns.Where((column, index) =>
            !column.Receipt.ComparisonComplete || summaryColumns[index].Incomplete > 0).Count();
        if (incomplete > 0) limitations.Add(new(CompareLimitation.IncompleteTrial, incomplete));
        var pages = (rows.Count + CompareRowPage.Size - 1) / CompareRowPage.Size;
        var summary = new ProposalComparison(1, ProposalComparison.TrialBeforeAfter, summaryColumns, forms.Count,
            diverging, rows.Count, pages, limitations.Count == 0, limitations);
        if (page is { } number)
            summary = summary with
            {
                Page = new CompareRowPage(number, number < pages ? number + 1 : null,
                    rows.Skip((number - 1) * CompareRowPage.Size).Take(CompareRowPage.Size).ToArray()),
            };
        return CommandOutcome<ProposalComparison>.Success(summary);
    }

    private static void Collect(MotifDatabase database, TrialExportReceipt receipt, int column, int width,
        Dictionary<(string Ws, string Key), FormAccumulator> forms)
    {
        for (var number = 1; number <= receipt.Pages; number++)
        {
            var words = TrialExportArtifact.Read(database, receipt.JobId, page: number)?.Page?.Words
                ?? throw new InvalidDataException("A retained Trial export page is missing.");
            foreach (var word in words)
            {
                var ws = word.TryGetProperty("writingSystemId", out var wsValue) && wsValue.ValueKind == JsonValueKind.String
                    ? wsValue.GetString()! : "";
                foreach (var form in word.GetProperty("expandedForms").EnumerateArray())
                {
                    var key = form.GetProperty("formKey").GetString()!;
                    if (!forms.TryGetValue((ws, key), out var accumulator))
                        forms[(ws, key)] = accumulator = new FormAccumulator(ws.Length == 0 ? null : ws,
                            form.GetProperty("form").GetString()!, key, width);
                    var cell = ReadCell(form);
                    accumulator.Cells[column] = accumulator.Cells[column] is { } existing ? existing.Merge(cell) : cell;
                }
            }
        }
    }

    private static Cell ReadCell(JsonElement form)
    {
        var before = form.GetProperty("before");
        var after = form.GetProperty("after");
        if (!before.GetProperty("dispatched").GetBoolean() || !after.GetProperty("dispatched").GetBoolean())
            return Absent;
        var beforeIds = Ids(before.GetProperty("readings"));
        var afterIds = Ids(after.GetProperty("readings"));
        var gained = afterIds.Except(beforeIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var lost = beforeIds.Except(afterIds, StringComparer.Ordinal)
            .Concat(form.GetProperty("lostReadingIds").EnumerateArray().Select(item => item.GetString()!))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var complete = before.GetProperty("complete").GetBoolean() && after.GetProperty("complete").GetBoolean();
        return gained.Length == 0 && lost.Length == 0 && complete ? Unchanged : new Cell(true, complete, gained, lost);
    }

    private static HashSet<string> Ids(JsonElement readings) =>
        readings.EnumerateArray().Select(reading => reading.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);

    private static string Short(string digest)
    {
        var text = digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest["sha256:".Length..] : digest;
        return text.Length > 8 ? text[..8] : text;
    }

    private static CommandOutcome<ProposalComparison> Refuse(string code, FailureReason reason, string message,
        Dictionary<string, string>? facts = null) =>
        CommandOutcome<ProposalComparison>.Refused(new Refusal(code, reason, message, facts));

    private sealed record Cell(bool Measured, bool Complete, string[] Gained, string[] Lost)
    {
        internal bool SameObservation(Cell other) => Measured == other.Measured &&
            Gained.AsSpan().SequenceEqual(other.Gained) && Lost.AsSpan().SequenceEqual(other.Lost);

        internal Cell Merge(Cell other) => !other.Measured ? this : !Measured ? other : new(Measured || other.Measured, Complete && other.Complete,
            Gained.Union(other.Gained, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Lost.Union(other.Lost, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    private sealed class FormAccumulator(string? writingSystemId, string form, string formKey, int width)
    {
        internal string? WritingSystemId { get; } = writingSystemId;
        internal string Form { get; } = form;
        internal string FormKey { get; } = formKey;
        internal Cell?[] Cells { get; } = new Cell?[width];
    }
}
