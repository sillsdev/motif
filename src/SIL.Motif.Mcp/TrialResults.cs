using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Mcp;

internal static class TrialResults
{
    internal static readonly string[] Categories = ["negatives-now-parsing", "positives-lost", "unfinished", "other-changes"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static CommandOutcome<AssessCommandResponse> Assess(ServerContext context, IReadOnlyList<string> words,
        CancellationToken cancellation) => AssessCommand.Assess(new AssessRequest(context.ProjectPath,
            words.Count == 0 ? null : new SelectionRequest(false, [], words, false, null)), RunnerOptions.ResolveRoot(),
            context.ParserPath ?? SIL.Motif.Host.Parser.PanGlossExecutable.TryLocate(), null, cancellation);

    internal static ToolOutcome Complete(ServerContext context, string jobId, string proposalId) => ToolOutcome.From(
        ProjectStoreCommand.Run(context.ProjectPath, context.ProductVersion, (database, _) =>
        {
            var job = new JobRepository(database).Get(jobId) ?? throw new InvalidDataException("Trial job not found.");
            if (job.Kind != JobCommands.TrialKind) throw new InvalidDataException("The job is not a Trial.");
            var input = TrialJobInput.Parse(job.InputJson);
            var proposal = SIL.Motif.Contract.Parsing.ProposalJsonParser.Parse(input.ProposalJson);
            if (proposal.ProposalId != CanonicalId.Parse(proposalId))
                throw new InvalidDataException("The Trial belongs to another Proposal.");
            if (job.Status != JobStatus.Completed)
                return CommandOutcome<JsonObject>.Refused(new Refusal("trial.measurement-incomplete", FailureReason.Refused,
                    "The Trial did not complete its Assessments. Inspect the job result and revise or retry the Trial.",
                    new Dictionary<string, string> { ["jobId"] = jobId, ["status"] = job.Status.ToString() }));
            var assessments = new AssessmentRepository(database);
            using var completion = JsonDocument.Parse(job.ResultJson ?? throw new InvalidDataException("The Trial has no result."));
            var ids = completion.RootElement.GetProperty("assessmentIds").EnumerateArray().Select(id => id.GetString()!).ToArray();
            var before = (input.BaselineAssessmentIds ?? []).Select(assessments.Get).ToArray();
            var after = ids.Select(assessments.Get).ToArray();
            var pair = after.OrderBy(record => record.Kind == "Correctness" ? 0 : 1)
                .Select(record => (After: record, Before: before.FirstOrDefault(other => other.Kind == record.Kind &&
                    other.Assessor == record.Assessor && other.BaselineToken == record.BaselineToken)))
                .FirstOrDefault(pair => pair.Before is not null && pair.After.Words?.Count > 0);
            if (pair.Before is null)
                return CommandOutcome<JsonObject>.Refused(new Refusal("trial.difference-unavailable", FailureReason.Refused,
                    "The Trial lacks matching Baseline Assessments. Trial the Draft again to produce its Difference."));
            var existing = assessments.ListByProposal(proposal.ProposalId).Where(record => record.Kind == "Difference" &&
                    record.Pipeline == "trial-difference/v1").FirstOrDefault(record =>
                ScopeCodec.ReadDifference(record.ScopeJson, "difference").ToAssessmentId == pair.After.AssessmentId);
            string differenceId;
            if (existing is not null) differenceId = existing.AssessmentId;
            else
            {
                differenceId = CanonicalId.Mint("assessment/").Value;
                var comparison = AssessmentComparer.Compare(pair.Before.ToComparable(), pair.After.ToComparable());
                var scope = ScopeCodec.Write(new StoredScope.Difference(pair.Before.AssessmentId, pair.After.AssessmentId,
                    comparison.FromWordCount, comparison.ToWordCount, comparison.SharedWords.Count,
                    pair.Before.GrammarSourceSha256, pair.After.GrammarSourceSha256,
                    comparison.TokeniserMismatch, comparison.TokeniserWarning));
                var rows = Classify(pair.Before.Words!, pair.After.Words!, input.NegativeWords ?? []);
                assessments.Record(new NewAssessmentRecord(differenceId, proposal.ProposalId, pair.After.ProposalIntentDigest,
                    pair.After.Assessor, "Difference", scope, AssessmentMaterial.Digest(scope), pair.After.TokeniserName,
                    pair.After.TokeniserVersion, pair.After.BaselineToken, Selection.Create("trial-difference", comparison.SharedWords),
                    AssessmentMaterial.Digest(scope), AssessmentMaterial.Digest(string.Join('\n', rows.Select(row => row.Word + row.Outcome))),
                    pair.After.GrammarSourceSha256, pair.After.ModelFingerprint, "trial-difference/v1", 0, rows));
            }
            var difference = assessments.Get(differenceId);
            var storedRows = difference.Words ?? [];
            var summary = new JsonObject();
            foreach (var category in Categories)
            {
                var words = storedRows.Where(row => row.Outcome.StartsWith(category + ":", StringComparison.Ordinal)).ToArray();
                summary[category] = new JsonObject { ["count"] = words.Length,
                    ["words"] = new JsonArray(words.Take(5).Select(row => (JsonNode)JsonValue.Create(row.Word)!).ToArray()) };
            }
            summary["counts"] = new JsonObject { ["before"] = pair.Before.Words!.Count, ["after"] = pair.After.Words!.Count,
                ["changed"] = storedRows.Count };
            var measuredBaseline = JsonSerializer.Deserialize<SIL.Motif.Contract.Baselines.BaselineToken>(pair.After.BaselineToken,
                MotifJson.CreateOptions());
            var currentProposal = new ProposalRepository(database).Get(proposal.ProposalId);
            var currentDigest = SIL.Motif.Contract.Canonicalization.IntentDigest.Compute(
                SIL.Motif.Contract.Parsing.ProposalJsonParser.Parse(JobCommands.CanonicalProposalJson(currentProposal)));
            var result = new JsonObject { ["summary"] = summary, ["difference"] = differenceId,
                ["contentDigest"] = pair.After.ProposalIntentDigest, ["baseline"] = JsonNode.Parse(pair.After.BaselineToken),
                ["evidenceCurrent"] = currentDigest == pair.After.ProposalIntentDigest &&
                    measuredBaseline is not null && new BaselineRepository(database).IsCurrentEvidence(job.ProjectKey, measuredBaseline),
                ["job"] = jobId };
            return CommandOutcome<JsonObject>.Success(result);
        }), _ => "Read motif_difference by category. Revise and Trial again, then use motif_finalize_proposal.");

    internal static IReadOnlyList<AssessedWord> Classify(IReadOnlyList<AssessedWord> before,
        IReadOnlyList<AssessedWord> after, IReadOnlyList<string> negatives)
    {
        var old = before.ToDictionary(word => word.Word, StringComparer.Ordinal);
        var negativeSet = negatives.ToHashSet(StringComparer.Ordinal);
        var rows = new List<AssessedWord>();
        foreach (var word in after.OrderBy(word => word.Word, StringComparer.Ordinal))
        {
            if (!old.TryGetValue(word.Word, out var prior)) continue;
            var newDisapproved = Disapproved(word).Except(Disapproved(prior), StringComparer.Ordinal).Any();
            var newlyMissingApproved = (word.AnalysisComparison?.UnbuiltApproved ?? []).Select(item => item.AnalysisId)
                .Except((prior.AnalysisComparison?.UnbuiltApproved ?? []).Select(item => item.AnalysisId), StringComparer.Ordinal).Any();
            string? category = Unfinished(word) || Unfinished(prior) ? "unfinished"
                : (negativeSet.Contains(word.Word) && Count(prior) == 0 && Count(word) > 0) ||
                    newDisapproved ? "negatives-now-parsing"
                : newlyMissingApproved || Count(prior) > 0 && Count(word) == 0 ||
                    word.Correctness is { } correctness && prior.Correctness is { } earlier &&
                    correctness.Matched < earlier.Matched ? "positives-lost"
                : Changed(prior, word) ? "other-changes" : null;
            if (category is not null) rows.Add(new AssessedWord(word.Word, category + ":" + prior.Outcome + "->" + word.Outcome, []));
        }
        return rows;
    }

    private static IEnumerable<string> Disapproved(AssessedWord word) =>
        (word.AnalysisComparison?.Readings ?? []).SelectMany(reading => reading.Matches)
            .Where(match => match.Opinion.Equals("disapproved", StringComparison.OrdinalIgnoreCase))
            .Select(match => match.AnalysisId);

    private static int Count(AssessedWord word) => word.Morphology?.Analyses.Count ?? word.Analyses.Count;
    private static bool Unfinished(AssessedWord word) => word.IsIncomplete || word.Morphology is
        { Capped: true } or { TimedOut: true } or { InvalidShape: true } ||
        word.Morphology?.Refusal is not null || word.Morphology?.Unavailable.Count > 0 ||
        word.Outcome is "timeout" or "capped" or "incomplete" or "skipped" or "invalid";
    private static bool Changed(AssessedWord before, AssessedWord after) => before.Outcome != after.Outcome ||
        !before.Analyses.Select(item => item.IdentityDigest).ToHashSet().SetEquals(after.Analyses.Select(item => item.IdentityDigest)) ||
        !new HashSet<string>((before.Morphology?.Analyses ?? []).Select(item => JsonSerializer.Serialize(item, Json)))
            .SetEquals((after.Morphology?.Analyses ?? []).Select(item => JsonSerializer.Serialize(item, Json)));

    internal static ToolOutcome Read(ServerContext context, ToolArgs args)
    {
        var category = args.Required("category");
        if (!Categories.Contains(category)) throw new ToolArgumentException("tool.invalid-category", "Choose a declared Difference category.");
        var offset = args.Int("offset", 0);
        var limit = args.Int("limit", 25);
        if (offset < 0 || limit is < 1 or > 200) throw new ToolArgumentException("tool.invalid-page", "Use a nonnegative offset and a limit from 1 to 200.");
        return ToolOutcome.From(ProjectStoreCommand.Run(context.ProjectPath, context.ProductVersion, (database, _) =>
        {
            var assessments = new AssessmentRepository(database);
            var difference = assessments.Get(args.Required("difference"));
            if (difference.Kind != "Difference" || difference.Pipeline != "trial-difference/v1")
                throw new InvalidDataException("This is not a stored Trial Difference.");
            var scope = ScopeCodec.ReadDifference(difference.ScopeJson, "difference");
            var before = assessments.Get(scope.FromAssessmentId).Words!.ToDictionary(row => row.Word);
            var after = assessments.Get(scope.ToAssessmentId).Words!.ToDictionary(row => row.Word);
            var rows = difference.Words!.Where(row => row.Outcome.StartsWith(category + ":", StringComparison.Ordinal)).ToArray();
            var items = rows.Skip(offset).Take(limit).Select(row => (JsonNode)new JsonObject
            {
                ["word"] = row.Word, ["before"] = JsonSerializer.SerializeToNode(before[row.Word], Json),
                ["after"] = JsonSerializer.SerializeToNode(after[row.Word], Json),
            }).ToArray();
            return CommandOutcome<JsonObject>.Success(new JsonObject { ["difference"] = difference.AssessmentId,
                ["category"] = category, ["total"] = rows.Length, ["offset"] = offset, ["returned"] = items.Length,
                ["nextOffset"] = offset + items.Length < rows.Length ? offset + items.Length : null,
                ["items"] = new JsonArray(items) });
        }));
    }
}
