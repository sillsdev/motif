using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.HandAnalysis;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Review;

/// <summary>
/// Compares the retained Trial evidence of several Proposals form by form. It reads each Proposal's newest completed
/// Trial export (newest by creation time, then by the greater job id), or the Trial a job pin names, measures nothing
/// and changes nothing. Gains and losses are each Trial's own before against its own
/// after. The approved analyses come from the hand-analysis index pinned at the Baseline the Trials were measured on;
/// each is kept or lost by <see cref="ApprovedReadingSet.KeptBy"/> against the Trial's readings, joined to the form by
/// <see cref="ApprovedAnalysisFact.FormIn"/>.
/// </summary>
public static class ProposalCompareReads
{
    /// <summary>The approved analyses of the pinned snapshot, and the snapshot's identity.</summary>
    internal sealed record ApprovedBasis(string StampId, ApprovedReadingSet Set);

    /// <summary>Pins the approved analyses at the Baseline whose semantic digest the Trials share.</summary>
    internal delegate CommandOutcome<ApprovedBasis> ApprovedLoader(MotifDatabase database,
        SIL.Motif.Contract.Projects.ProjectLocator project, string baselineSemanticDigest);

    private static readonly BaselinePaths Paths = new();
    private static readonly FwHandAnalysisIndex Index = new(Paths);

    private const int RetainedJoins = 4;
    private static readonly object JoinGate = new();
    private static readonly List<((string Store, string Snapshot) Key, Join Join)> Joins = [];
    private static readonly ConcurrentDictionary<string, int> JoinCounts = new(StringComparer.Ordinal);

    /// <summary>How many times the form join was computed for one store; a page read of a known snapshot adds none.</summary>
    internal static int JoinsComputedFor(string storePath) => JoinCounts.GetValueOrDefault(storePath);

    /// <summary>How many times the default loader has copied and read a Baseline in this process.</summary>
    internal static int BaselineLoads => Index.Loads;

    private static readonly Cell Unchanged = new(true, true, [], []);
    private static readonly Cell Absent = new(false, false, [], []);

    /// <summary>Reads the summary, and one page of form rows when the request names a page.</summary>
    public static CommandOutcome<ProposalComparison> Read(CompareProposalsRequest request) => Read(request, null);

    internal static CommandOutcome<ProposalComparison> Read(CompareProposalsRequest request, ApprovedLoader? loader)
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
            try { return Compare(database, project, ids, request, loader ?? LoadApproved); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                UnauthorizedAccessException or JsonException)
            {
                return Refuse(RefusalCodes.ProposalCompareTrialMissing, FailureReason.Refused,
                    "A retained Trial evidence export could not be read.");
            }
        });
    }

    private static CommandOutcome<ProposalComparison> Compare(MotifDatabase database,
        SIL.Motif.Contract.Projects.ProjectLocator project, List<CanonicalId> ids, CompareProposalsRequest request,
        ApprovedLoader loadApproved)
    {
        var at = request.At;
        var page = request.Page;
        var requestedSnapshot = request.Snapshot;
        var pins = PinJobs(database, project, ids, request.Jobs, out var pinRefusal);
        if (pinRefusal is not null) return CommandOutcome<ProposalComparison>.Refused(pinRefusal);
        var columns = new List<Column>();
        foreach (var id in ids)
        {
            var all = Trials.NewestFirst(Trials.JobsFor(database, project, id));
            var job = pins.GetValueOrDefault(id) ?? all.FirstOrDefault(item => item.Status == JobStatus.Completed);
            var receipt = job is null ? null : TrialExportArtifact.Read(database, job.JobId);
            if (job is null || receipt is null)
                return Refuse(RefusalCodes.ProposalCompareTrialMissing, FailureReason.Refused,
                    "A Proposal has no completed Trial with a retained evidence export to compare.",
                    new() { ["proposalId"] = id.Value });
            columns.Add(new Column(id, receipt, job, all));
        }
        var samples = columns.Select(column =>
        {
            var input = TrialJobInput.Parse(column.Job.InputJson);
            return (Working: TrialSampleKind.IsWorkingSet(input.Sample), input.WordListDigest);
        }).ToArray();
        if (samples.Any(sample => sample.Working))
        {
            if (!samples.All(sample => sample.Working))
                return Refuse(RefusalCodes.TrialWorkingSetNotAGate, FailureReason.Refused,
                    "A working-set Trial measured only a chosen word list, so it cannot be compared with a Trial that did not. " +
                    "Compare working-set Trials with each other, or run each Proposal without --no-base-sample.");
            if (samples.Select(sample => sample.WordListDigest).Distinct(StringComparer.Ordinal).Count() > 1)
                return Refuse(RefusalCodes.TrialWorkingSetListsDiffer, FailureReason.Refused,
                    "These working-set Trials measured different word lists. Run each Proposal on the same list to compare them.");
        }
        if (at is not null)
            foreach (var column in columns)
                if (TrialExportArtifact.Read(database, column.Receipt.JobId, at) is null)
                    return Refuse(RefusalCodes.ProposalCompareAtMismatch, FailureReason.Refused,
                        "A Trial was not measured on the requested Baseline or Proposal revision.",
                        new() { ["proposalId"] = column.Proposal.Value });
        if (columns.Select(column => Semantic(database, column.Receipt)).Distinct(StringComparer.Ordinal).Count() > 1)
            return Refuse(RefusalCodes.ProposalCompareBaselineMismatch, FailureReason.Refused,
                "These Trials were measured on different Baselines, so their gains and losses are not comparable. " +
                "Run a Trial for each Proposal on the current Baseline.",
                columns.ToDictionary(column => column.Proposal.Value, column => Short(Semantic(database, column.Receipt))));

        var semantic = Semantic(database, columns[0].Receipt);
        var loaded = loadApproved(database, project, semantic);
        if (!loaded.Succeeded)
            return CommandOutcome<ProposalComparison>.Refused(loaded.Refusal!);
        var approved = loaded.Value!;
        var snapshot = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', columns.Select(column => column.Receipt.JobId).Append(approved.StampId)))))[..12];
        if (requestedSnapshot is not null && requestedSnapshot != snapshot)
            return Refuse(RefusalCodes.ProposalCompareSnapshotChanged, FailureReason.Refused,
                "A newer Trial or a changed set of approved analyses has replaced what this comparison was read from. Read the summary again.");

        var join = JoinFor(database, (database.FullPath, snapshot), columns, approved.Set);
        var limitations = new List<CompareLimitation>(join.Limitations);
        var freshness = Freshness(database, columns);
        if (freshness.Stale > 0) limitations.Add(new(CompareLimitation.TrialStale, freshness.Stale));
        if (freshness.Superseded > 0) limitations.Add(new(CompareLimitation.TrialSuperseded, freshness.Superseded));
        var rows = join.Rows;
        var pages = (rows.Count + CompareRowPage.Size - 1) / CompareRowPage.Size;
        var summary = new ProposalComparison(1, ProposalComparison.TrialBeforeAfter, snapshot, join.Columns, join.FormsCompared,
            join.Diverging, rows.Count, pages, limitations.Count == 0, limitations, approved.Set.SkippedOtherHumanAgentApprovals,
            approved.Set.Analyses.Length, join.Uncounted);
        if (page > Math.Max(pages, 1))
            return Refuse(RefusalCodes.EvidenceInvalidPage, FailureReason.InvalidArgument,
                $"The comparison has {Math.Max(pages, 1)} page(s); page {page} does not exist.");
        if (page is { } number)
            summary = summary with
            {
                Page = new CompareRowPage(number, number < pages ? number + 1 : null,
                    rows.Skip((number - 1) * CompareRowPage.Size).Take(CompareRowPage.Size).ToArray()),
            };
        return CommandOutcome<ProposalComparison>.Success(summary);
    }

    // A pin names a completed Trial of a named Proposal, at most one per Proposal.
    private static Dictionary<CanonicalId, JobRecord> PinJobs(MotifDatabase database,
        SIL.Motif.Contract.Projects.ProjectLocator project, List<CanonicalId> ids, IReadOnlyList<string>? jobs, out Refusal? refusal)
    {
        refusal = null;
        var pins = new Dictionary<CanonicalId, JobRecord>();
        if (jobs is null || jobs.Count == 0) return pins;
        var repository = new JobRepository(database);
        var key = ProjectWorkspaceKey.Compute(project);
        foreach (var text in jobs)
        {
            var job = string.IsNullOrWhiteSpace(text) ? null : repository.Get(text);
            if (job is null || job.ProjectKey != key || job.Kind != TrialJobHandler.TrialKind || job.Status != JobStatus.Completed)
            {
                refusal = new Refusal(RefusalCodes.ProposalCompareTrialMissing, FailureReason.Refused,
                    "A pinned job is not a completed Trial of this project.", new Dictionary<string, string> { ["jobId"] = text ?? "" });
                return pins;
            }
            CanonicalId owner;
            try { owner = CanonicalId.Parse(Trials.ProposalOf(job)); }
            catch (Exception exception) when (exception is InvalidDataException or JsonException or FormatException or ArgumentException)
            {
                refusal = new Refusal(RefusalCodes.ProposalCompareTrialMissing, FailureReason.Refused,
                    "A pinned Trial does not name a readable Proposal.", new Dictionary<string, string> { ["jobId"] = job.JobId });
                return pins;
            }
            if (!ids.Contains(owner) || !pins.TryAdd(owner, job))
            {
                refusal = new Refusal(RefusalCodes.ProposalCompareProposalsInvalid, FailureReason.InvalidArgument,
                    "Pin at most one Trial per Proposal, and only a Trial of a Proposal this comparison names.",
                    new Dictionary<string, string> { ["jobId"] = job.JobId });
                return pins;
            }
        }
        return pins;
    }

    // Stale means a different revision; superseded means a newer attempt or a superseded Proposal.
    private static (int Stale, int Superseded) Freshness(MotifDatabase database, List<Column> columns)
    {
        var repository = new ProposalRepository(database);
        int stale = 0, superseded = 0;
        foreach (var column in columns)
        {
            ProposalRecord? record;
            try { record = repository.Get(column.Proposal); }
            catch (KeyNotFoundException) { record = null; }
            string? current;
            try { current = Trials.ReadCurrentIntentDigest(record); }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or ContractParseException)
            { current = null; }
            var used = IntentDigest.Compute(ProposalJsonParser.Parse(TrialJobInput.Parse(column.Job.InputJson).ProposalJson));
            if (current is not null && !string.Equals(current, used, StringComparison.Ordinal)) stale++;
            var newerAttempt = column.All.TakeWhile(job => job.JobId != column.Job.JobId).Any();
            if (newerAttempt || record?.SupersededBy is not null) superseded++;
        }
        return (stale, superseded);
    }

    private static Join JoinFor(MotifDatabase database, (string Store, string Snapshot) key, List<Column> columns,
        ApprovedReadingSet approved)
    {
        lock (JoinGate)
        {
            var hit = Joins.FindIndex(entry => entry.Key == key);
            if (hit >= 0)
            {
                var found = Joins[hit];
                Joins.RemoveAt(hit);
                Joins.Insert(0, found);
                return found.Join;
            }
        }
        var join = BuildJoin(database, columns, approved);
        lock (JoinGate)
        {
            Joins.RemoveAll(entry => entry.Key == key);
            Joins.Insert(0, (key, join));
            if (Joins.Count > RetainedJoins) Joins.RemoveRange(RetainedJoins, Joins.Count - RetainedJoins);
        }
        return join;
    }

    private static Join BuildJoin(MotifDatabase database, List<Column> columns, ApprovedReadingSet approved)
    {
        JoinCounts.AddOrUpdate(database.FullPath, 1, (_, count) => count + 1);
        var forms = new Dictionary<(string Ws, string Key), FormAccumulator>();
        var measured = new HashSet<(string Ws, string Form)>();
        for (var index = 0; index < columns.Count; index++)
            Collect(database, columns[index].Receipt, index, columns.Count, forms, approved, measured);

        var counts = new int[columns.Count, 5];
        var keptAnalyses = columns.Select(_ => new HashSet<Guid>()).ToArray();
        var lostAnalyses = columns.Select(_ => new HashSet<Guid>()).ToArray();
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
                keptAnalyses[index].UnionWith(cell.Kept.Select(hit => hit.Analysis));
                lostAnalyses[index].UnionWith(cell.LostApproved.Select(hit => hit.Analysis));
            }
            var diverges = cells.Skip(1).Any(cell => !cell.SameObservation(cells[0]));
            if (trivial && !diverges && cells.All(cell => cell.LostApproved.Length == 0)) continue;
            if (diverges) diverging++;
            rows.Add(new CompareRow(form.WritingSystemId, form.Form, form.FormKey, diverges,
                cells.Select(cell => new CompareCell(cell.Measured, cell.Complete, cell.Gained, cell.Lost,
                    cell.KeptIds(), cell.LostApprovedIds())).ToArray()));
        }
        rows.Sort((left, right) =>
        {
            var order = right.Diverges.CompareTo(left.Diverges);
            if (order == 0) order = string.CompareOrdinal(left.WritingSystemId ?? "", right.WritingSystemId ?? "");
            return order != 0 ? order : string.CompareOrdinal(left.FormKey, right.FormKey);
        });

        var summaryColumns = columns.Select((column, index) => new CompareColumn(column.Proposal.Value,
            counts[index, 0], counts[index, 1],
            counts[index, 2], counts[index, 3], counts[index, 4], keptAnalyses[index].Count, lostAnalyses[index].Except(keptAnalyses[index]).Count())).ToArray();
        var uncounted = approved.Analyses.Count(fact => !Counted(fact, measured));
        var limitations = new List<CompareLimitation>();
        var notMeasured = summaryColumns.Count(column => column.NotMeasured > 0);
        if (notMeasured > 0) limitations.Add(new(CompareLimitation.NotMeasured, notMeasured));
        var incomplete = columns.Where((column, index) =>
            !column.Receipt.ComparisonComplete || summaryColumns[index].Incomplete > 0).Count();
        if (incomplete > 0) limitations.Add(new(CompareLimitation.IncompleteTrial, incomplete));
        if (approved.SkippedOtherHumanAgentApprovals > 0)
            limitations.Add(new(CompareLimitation.ApprovedOtherAgents, columns.Count));
        if (uncounted > 0) limitations.Add(new(CompareLimitation.ApprovedUncounted, columns.Count));
        if (ParserVersions(database, columns) is { } parser) limitations.Add(parser);
        return new Join(summaryColumns, forms.Count, diverging, rows, limitations, uncounted);
    }

    // An approval counts when signed and a Trial measured its word.
    private static bool Counted(ApprovedAnalysisFact fact, HashSet<(string Ws, string Form)> measured) =>
        fact.Signature is not null && fact.Forms is { } forms &&
        forms.Keys.Any(ws => fact.FormIn(ws) is { } form && measured.Contains((ws, form)));

    // Count is the columns whose sides differ, else all columns.
    private static CompareLimitation? ParserVersions(MotifDatabase database, List<Column> columns)
    {
        var versions = columns.Select(column => TrialExportArtifact.ReadParserVersions(database, column.Receipt.JobId)).ToArray();
        var distinct = versions.Where(version => version is not null)
            .SelectMany(version => new[] { version!.Value.Before, version.Value.After }).Where(text => text is not null)
            .Select(text => text!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var sidesDiffer = versions.Count(version => version is { } pair && pair.Before != pair.After);
        if (distinct.Length < 2 && sidesDiffer == 0) return null;
        var detail = string.Join(" | ", distinct);
        if (detail.Length > 80) detail = detail[..77] + "...";
        return new CompareLimitation(CompareLimitation.ParserVersionDiffers, sidesDiffer > 0 ? sidesDiffer : columns.Count)
        {
            Detail = detail.Length == 0 ? null : detail,
        };
    }

    private static void Collect(MotifDatabase database, TrialExportReceipt receipt, int column, int width,
        Dictionary<(string Ws, string Key), FormAccumulator> forms, ApprovedReadingSet approved,
        HashSet<(string Ws, string Form)> measured)
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
                    var text = form.GetProperty("form").GetString()!.Normalize(NormalizationForm.FormD);
                    var cell = ReadCell(form, approved, ws, text);
                    if (cell.Measured && ws.Length > 0) measured.Add((ws, text));
                    accumulator.Cells[column] = accumulator.Cells[column] is { } existing ? existing.Merge(cell) : cell;
                }
            }
        }
    }

    private static Cell ReadCell(JsonElement form, ApprovedReadingSet approved, string ws, string formText)
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
        var (kept, lostApproved) = ws.Length == 0 ? ([], []) : Approvals(approved, ws, formText, before, after, complete);
        return gained.Length == 0 && lost.Length == 0 && complete && kept.Length == 0 && lostApproved.Length == 0
            ? Unchanged : new Cell(true, complete, gained, lost) { Kept = kept, LostApproved = lostApproved };
    }

    // Lost needs a before match, no after match and both sides finished.
    private static (ApprovedHit[] Kept, ApprovedHit[] Lost) Approvals(ApprovedReadingSet approved, string ws, string formText,
        JsonElement before, JsonElement after, bool complete)
    {
        List<ApprovedAnalysisFact> OnForm(JsonElement side) =>
            [.. Signatures(side.GetProperty("readings")).SelectMany(approved.KeptBy)
                .Where(fact => string.Equals(fact.FormIn(ws), formText, StringComparison.Ordinal))
                .DistinctBy(fact => fact.AnalysisId)];
        var kept = OnForm(after).Select(Hit).ToList();
        var keptIds = kept.Select(hit => hit.Analysis).ToHashSet();
        var lost = complete
            ? OnForm(before).Where(fact => !keptIds.Contains(fact.AnalysisId)).Select(Hit).ToList() : [];
        return ([.. kept.OrderBy(hit => hit.Id, StringComparer.Ordinal).ThenBy(hit => hit.Analysis)],
            [.. lost.OrderBy(hit => hit.Id, StringComparer.Ordinal).ThenBy(hit => hit.Analysis)]);
    }

    private static ApprovedHit Hit(ApprovedAnalysisFact fact) => new(fact.AnalysisId, fact.Signature!.Id);

    private static List<ReadingSignature> Signatures(JsonElement readings) =>
        [.. readings.EnumerateArray().Select(reading =>
            JsonSerializer.Deserialize<ReadingSignature>(reading.GetProperty("signature").GetRawText())
                ?? throw new InvalidDataException("A retained Trial reading has no signature."))];

    private static HashSet<string> Ids(JsonElement readings) =>
        readings.EnumerateArray().Select(reading => reading.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);

    // Two Trials share a Baseline when its semantic identity matches; the bundle digest also varies with capture.
    private static string Semantic(MotifDatabase database, TrialExportReceipt receipt) =>
        TrialExportArtifact.ReadBaselineSemantic(database, receipt.JobId) ?? receipt.BaselineBundle;

    private static CommandOutcome<ApprovedBasis> LoadApproved(MotifDatabase database,
        SIL.Motif.Contract.Projects.ProjectLocator project, string baselineSemanticDigest)
    {
        var key = ProjectWorkspaceKey.Compute(project);
        var baseline = new BaselineRepository(database).GetCurrentPublished(key);
        if (baseline is null || baseline.Token.SemanticSnapshotDigest != baselineSemanticDigest)
            return CommandOutcome<ApprovedBasis>.Refused(new Refusal(RefusalCodes.HandAnalysisSnapshotExpired,
                RefusalCatalog.Specs[RefusalCodes.HandAnalysisSnapshotExpired].DefaultReason,
                "The Baseline these Trials were measured on is no longer retained, so its approved analyses cannot be read. " +
                "Run a Trial for each Proposal on the current Baseline."));
        var identity = new BaselineSemanticIdentity(baseline.Token.ProjectIdentity, baseline.Token.SemanticSnapshotDigest,
            baseline.Token.ProjectionVersion);
        var projectId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key))[..16]);
        Paths.Set(projectId, identity, baseline.FwDataPath);
        var pin = Index.PinAtBaselineAsync(projectId, identity, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        if (!pin.Succeeded) return CommandOutcome<ApprovedBasis>.Refused(pin.Refusal!);
        var snapshot = pin.Value!;
        try
        {
            return CommandOutcome<ApprovedBasis>.Success(new(snapshot.Stamp.SnapshotId,
                ((IApprovedReadingSource)snapshot).Approved));
        }
        finally { snapshot.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private sealed class BaselinePaths : IHandAnalysisProjectSources
    {
        private readonly ConcurrentDictionary<(Guid Project, BaselineSemanticIdentity Identity), string> _paths = new();

        internal void Set(Guid projectId, BaselineSemanticIdentity identity, string path) => _paths[(projectId, identity)] = path;

        public string? LiveFwData(Guid projectId) => null;

        public string? BaselineFwData(Guid projectId, BaselineSemanticIdentity identity) =>
            _paths.GetValueOrDefault((projectId, identity));
    }

    private static string Short(string digest)
    {
        var text = digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest["sha256:".Length..] : digest;
        return text.Length > 8 ? text[..8] : text;
    }

    private static CommandOutcome<ProposalComparison> Refuse(string code, FailureReason reason, string message,
        Dictionary<string, string>? facts = null) =>
        CommandOutcome<ProposalComparison>.Refused(new Refusal(code, reason, message, facts));

    private readonly record struct ApprovedHit(Guid Analysis, string Id);

    private sealed record Column(CanonicalId Proposal, TrialExportReceipt Receipt, JobRecord Job, IReadOnlyList<JobRecord> All);

    private sealed record Join(CompareColumn[] Columns, int FormsCompared, int Diverging, List<CompareRow> Rows,
        List<CompareLimitation> Limitations, int Uncounted);

    private sealed record Cell(bool Measured, bool Complete, string[] Gained, string[] Lost)
    {
        internal ApprovedHit[] Kept { get; init; } = [];
        internal ApprovedHit[] LostApproved { get; init; } = [];

        internal string[] KeptIds() => Ids(Kept);
        internal string[] LostApprovedIds() => Ids(LostApproved);

        internal bool SameObservation(Cell other) => Measured == other.Measured &&
            Gained.AsSpan().SequenceEqual(other.Gained) && Lost.AsSpan().SequenceEqual(other.Lost) &&
            KeptIds().AsSpan().SequenceEqual(other.KeptIds()) && LostApprovedIds().AsSpan().SequenceEqual(other.LostApprovedIds());

        internal Cell Merge(Cell other)
        {
            if (!other.Measured) return this;
            if (!Measured) return other;
            var kept = Kept.Union(other.Kept).OrderBy(hit => hit.Id, StringComparer.Ordinal).ThenBy(hit => hit.Analysis).ToArray();
            return new(Measured || other.Measured, Complete && other.Complete,
                Gained.Union(other.Gained, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                Lost.Union(other.Lost, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())
            {
                Kept = kept,
                LostApproved = LostApproved.Union(other.LostApproved).Except(kept)
                    .OrderBy(hit => hit.Id, StringComparer.Ordinal).ThenBy(hit => hit.Analysis).ToArray(),
            };
        }

        private static string[] Ids(ApprovedHit[] hits) => [.. hits.Select(hit => hit.Id).Distinct(StringComparer.Ordinal)];
    }

    private sealed class FormAccumulator(string? writingSystemId, string form, string formKey, int width)
    {
        internal string? WritingSystemId { get; } = writingSystemId;
        internal string Form { get; } = form;
        internal string FormKey { get; } = formKey;
        internal Cell?[] Cells { get; } = new Cell?[width];
    }
}
