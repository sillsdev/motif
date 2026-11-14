using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// The hypothesis ledger: one record per rule, with its prediction, test and decision, in Motif's store (ADR 0117).
/// These commands are the whole API. A later control-test runner (#172) calls <see cref="RecordTest"/> and a rule-attempt
/// gate (#171) calls <see cref="Neighbours"/>; neither needs to know the tables. Nothing here writes the FieldWorks project.
/// </summary>
public static class HypothesisCommands
{
    /// <summary>Predicted forms a PASS needs; fewer leaves the verdict UNDECIDED at best.</summary>
    public const int MinimumPredictions = 20;

    private static readonly Regex GdrIdPattern = new(@"^GDR-\d+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>States a hypothesis and returns it with the neighbours already on its shape.</summary>
    public static CommandOutcome<HypothesisResponse> Add(AddHypothesisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var draft = request.Hypothesis;
            var missing = new[]
            {
                (draft.Statement, "statement"), (draft.Mechanism, "mechanism"), (draft.Change, "change"),
                (draft.Neighbour, "neighbour"),
            }.FirstOrDefault(field => string.IsNullOrWhiteSpace(field.Item1));
            if (missing.Item2 is not null)
                return Refused<HypothesisResponse>(RefusalCodes.HypothesisInvalid, FailureReason.InvalidArgument,
                    $"A hypothesis needs a {missing.Item2}. The neighbour field records the ledger query and its result, " +
                    "or the rule widened, so the check is on record.");
            var questions = (draft.Questions ?? []).Where(q => !string.IsNullOrWhiteSpace(q.Question))
                .Select(q => new HypothesisQuestion(q.Question.Trim(), Clean(q.Predicted), null)).ToList();
            var ledger = new HypothesisRepository(database);
            var created = ledger.Add(new HypothesisRepository.NewHypothesis(
                Clean(draft.Pattern), draft.Statement.Trim(), Squash(draft.Mechanism), Squash(draft.Change),
                Squash(draft.Environment), Squash(draft.HostClass), Squash(draft.Slot), Clean(draft.Neighbour),
                (draft.Predictions ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList(),
                Clean(draft.Falsifier), Clean(draft.Exceptions), draft.ObjectsAdded, draft.AttestedTypesGained, questions));
            return Done(ledger, created.HypothesisId);
        });
    }

    /// <summary>Records a test. A PASS needs 20 predicted forms and no attested Trial loss.</summary>
    public static CommandOutcome<HypothesisResponse> RecordTest(RecordHypothesisTestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var ledger = new HypothesisRepository(database);
            if (ledger.Get(request.HypothesisId) is not { } hypothesis) return NotFound<HypothesisResponse>(request.HypothesisId);
            var result = request.Result;
            var verdict = result.Verdict?.Trim().ToUpperInvariant();
            var controls = result.Controls ?? new Dictionary<string, int>();
            if (verdict is null || !HypothesisVerdicts.All.Contains(verdict))
                return Invalid<HypothesisResponse>("The verdict must be PASS, FAIL or UNDECIDED.");
            if (result.Predicted < 0 || result.RuleHits < 0 || controls.Values.Any(hits => hits < 0) ||
                result.TrialAttestedLosses < 0 || result.TrialNewReadings < 0)
                return Invalid<HypothesisResponse>("Counts cannot be negative.");
            if (hypothesis.Status == HypothesisStatuses.Superseded)
                return Rule<HypothesisResponse>($"{hypothesis.HypothesisId} is superseded by {hypothesis.SupersededBy}; test that one.");
            if (verdict == HypothesisVerdicts.Pass && result.Predicted < MinimumPredictions)
                return Rule<HypothesisResponse>($"A PASS needs at least {MinimumPredictions} predicted forms; this test predicted " +
                    $"{result.Predicted}. Record it as UNDECIDED, or predict more forms.");
            if (verdict == HypothesisVerdicts.Pass && result.TrialAttestedLosses > 0)
                return Rule<HypothesisResponse>("Any attested loss is a FAIL until explained; this PASS reports " +
                    $"{result.TrialAttestedLosses}.");
            var margin = result.Margin ?? result.RuleHits - controls.Values.DefaultIfEmpty(0).Max();
            ledger.AddTest(hypothesis.HypothesisId, verdict, result.Predicted, result.RuleHits, controls, margin,
                result.TrialAttestedLosses, result.TrialNewReadings, result.TrialWorkSteps, Clean(result.Command));
            return Done(ledger, hypothesis.HypothesisId);
        });
    }

    /// <summary>
    /// Decides a hypothesis. <c>passed</c> needs a PASS as the latest test, a GDR link or a stated reason for none, and no
    /// passed neighbour with another mechanism that it does not supersede (the contradiction guard).
    /// </summary>
    public static CommandOutcome<HypothesisResponse> Decide(DecideHypothesisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var ledger = new HypothesisRepository(database);
            if (ledger.Get(request.HypothesisId) is not { } hypothesis) return NotFound<HypothesisResponse>(request.HypothesisId);
            var status = request.Status?.Trim().ToLowerInvariant();
            var decision = Clean(request.Decision);
            if (status is not (HypothesisStatuses.Passed or HypothesisStatuses.Failed or HypothesisStatuses.Superseded))
                return Invalid<HypothesisResponse>("The status to decide must be passed, failed or superseded.");
            if (decision is null) return Invalid<HypothesisResponse>("A decision needs its words: say what was decided and why.");
            if (hypothesis.Status is HypothesisStatuses.Superseded ||
                hypothesis.Status != HypothesisStatuses.Proposed && status != HypothesisStatuses.Superseded)
                return Rule<HypothesisResponse>($"{hypothesis.HypothesisId} is {hypothesis.Status}. A decided hypothesis " +
                    "changes only by being superseded; state a new hypothesis for a new attempt.");
            var tests = ledger.Tests(hypothesis.HypothesisId);
            var columns = new Dictionary<string, object?> { ["Status"] = status, ["Decision"] = decision };
            IReadOnlyList<string>? supersedes = null;
            if (status == HypothesisStatuses.Superseded)
            {
                var by = Clean(request.SupersededBy);
                if (by is null || ledger.Get(by) is not { } replacement)
                    return Rule<HypothesisResponse>("A superseded hypothesis names the hypothesis that replaces it " +
                        "(--superseded-by H-n), and that hypothesis must exist.");
                if (string.Equals(replacement.HypothesisId, hypothesis.HypothesisId, StringComparison.OrdinalIgnoreCase))
                    return Rule<HypothesisResponse>("A hypothesis cannot supersede itself.");
                columns["SupersededBy"] = replacement.HypothesisId;
            }
            else if (status == HypothesisStatuses.Failed)
            {
                if (tests.Count == 0 || tests[^1].Verdict == HypothesisVerdicts.Pass)
                    return Rule<HypothesisResponse>("A failed hypothesis needs a recorded test whose latest verdict is " +
                        "FAIL or UNDECIDED. Record the test first.");
            }
            else
            {
                if (tests.Count == 0 || tests[^1].Verdict != HypothesisVerdicts.Pass)
                    return Rule<HypothesisResponse>("A hypothesis passes only when its latest recorded test is PASS. " +
                        "Record the test first.");
                if (hypothesis.GdrId is null && hypothesis.GdrNone is null)
                    return Rule<HypothesisResponse>("A passed hypothesis names the GDR section its Proposal changes, or says " +
                        "why none changed. Link it first: hypothesis link --gdr GDR-n --gdr-section <text>, or --gdr-none <reason>.");
                var named = (request.Supersedes ?? []).Select(id => ledger.Get(id)?.HypothesisId).ToList();
                if (named.Any(id => id is null))
                    return NotFound<HypothesisResponse>(request.Supersedes!.First(id => ledger.Get(id) is null));
                var standing = ledger.Neighbours(hypothesis.ChangeText, hypothesis.Environment, hypothesis.HostClass, hypothesis.Slot)
                    .Where(other => other.Status == HypothesisStatuses.Passed &&
                        !string.Equals(other.HypothesisId, hypothesis.HypothesisId, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(other.Mechanism, hypothesis.Mechanism, StringComparison.OrdinalIgnoreCase) &&
                        !named.Contains(other.HypothesisId, StringComparer.OrdinalIgnoreCase)).ToList();
                if (standing.Count > 0)
                    return Rule<HypothesisResponse>($"{string.Join(", ", standing.Select(o => o.HypothesisId))} already passed on " +
                        $"this shape with another mechanism ({string.Join("; ", standing.Select(o => o.Mechanism))}) and binds until " +
                        "a ledger row supersedes it. Pass this one with --supersedes to replace it, or widen that rule.");
                supersedes = named!.Select(id => id!).ToList();
            }
            ledger.Set(hypothesis.HypothesisId, columns, supersedes,
                supersedes is null ? null : $"Superseded by {hypothesis.HypothesisId}: {decision}");
            return Done(ledger, hypothesis.HypothesisId);
        });
    }

    /// <summary>Links Proposals and a GDR section to a hypothesis. A Proposal is not looked up: Proposals can be archived.</summary>
    public static CommandOutcome<HypothesisResponse> Link(LinkHypothesisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var ledger = new HypothesisRepository(database);
            if (ledger.Get(request.HypothesisId) is not { } hypothesis) return NotFound<HypothesisResponse>(request.HypothesisId);
            var proposals = (request.ProposalIds ?? []).Select(id => id?.Trim() ?? "").ToList();
            var gdrId = Clean(request.GdrId);
            var gdrSection = Clean(request.GdrSection);
            var gdrNone = Clean(request.GdrNone);
            if (proposals.Any(id => id.Length == 0) ||
                proposals.Count == 0 && gdrId is null && gdrNone is null && gdrSection is null)
                return Invalid<HypothesisResponse>("Name at least one Proposal id, or a GDR (--gdr GDR-n --gdr-section <text>), " +
                    "or the reason none changed (--gdr-none <reason>).");
            if (gdrId is not null && !GdrIdPattern.IsMatch(gdrId))
                return Invalid<HypothesisResponse>($"'{gdrId}' is not a GDR id; use GDR-n, as gdr list shows.");
            if (gdrId is null != gdrSection is null && gdrNone is null)
                return Invalid<HypothesisResponse>("Give the GDR id and the section together.");
            if (gdrNone is not null && (gdrId is not null || gdrSection is not null))
                return Invalid<HypothesisResponse>("A GDR link and 'no GDR changed' exclude each other.");
            var columns = new Dictionary<string, object?>();
            if (gdrId is not null) { columns["GdrId"] = gdrId.ToUpperInvariant(); columns["GdrSection"] = gdrSection; columns["GdrNone"] = null; }
            if (gdrNone is not null) { columns["GdrNone"] = gdrNone; columns["GdrId"] = null; columns["GdrSection"] = null; }
            if (columns.Count > 0) ledger.Set(hypothesis.HypothesisId, columns);
            if (proposals.Count > 0) ledger.LinkProposals(hypothesis.HypothesisId, proposals);
            return Done(ledger, hypothesis.HypothesisId);
        });
    }

    /// <summary>Records the four numbers of issue 185 after the answer key is known.</summary>
    public static CommandOutcome<HypothesisResponse> Grade(GradeHypothesisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var ledger = new HypothesisRepository(database);
            if (ledger.Get(request.HypothesisId) is not { } hypothesis) return NotFound<HypothesisResponse>(request.HypothesisId);
            var verdict = request.SealedVerdict?.Trim().ToLowerInvariant();
            var counts = new[] { request.WordQuestionsAsked, request.ObjectsBefore, request.TypesBefore, request.ObjectsAfter, request.TypesAfter };
            var hours = new[] { request.AgentHours, request.AlternativeHours };
            if (verdict is not (null or "match" or "mismatch"))
                return Invalid<HypothesisResponse>("The sealed verdict is 'match' or 'mismatch' against the answer key.");
            if (counts.Any(c => c < 0) || hours.Any(h => h < 0))
                return Invalid<HypothesisResponse>("Counts and hours cannot be negative.");
            var columns = new Dictionary<string, object?>();
            void Put(string column, object? value) { if (value is not null) columns[column] = value; }
            Put("SealedVerdict", verdict);
            Put("VerdictSource", Clean(request.VerdictSource));
            Put("WordQuestionsAsked", request.WordQuestionsAsked);
            Put("ObjectsBefore", request.ObjectsBefore);
            Put("TypesBefore", request.TypesBefore);
            Put("ObjectsAfter", request.ObjectsAfter);
            Put("TypesAfter", request.TypesAfter);
            Put("AgentHours", request.AgentHours);
            Put("AlternativeHours", request.AlternativeHours);
            if (columns.Count == 0)
                return Invalid<HypothesisResponse>("Give at least one of the four numbers: --sealed-verdict, --word-questions, " +
                    "--objects-before/--types-before/--objects-after/--types-after, --agent-hours/--alternative-hours.");
            ledger.Set(hypothesis.HypothesisId, columns);
            return Done(ledger, hypothesis.HypothesisId);
        });
    }

    /// <summary>Records a speaker's answer to one of a hypothesis's questions.</summary>
    public static CommandOutcome<HypothesisResponse> AnswerQuestion(AnswerHypothesisQuestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var ledger = new HypothesisRepository(database);
            if (ledger.Get(request.HypothesisId) is not { } hypothesis) return NotFound<HypothesisResponse>(request.HypothesisId);
            var answer = Clean(request.Answer);
            if (answer is null) return Invalid<HypothesisResponse>("An answer cannot be empty.");
            if (request.Question < 1 || request.Question > hypothesis.Questions.Count)
                return Invalid<HypothesisResponse>($"{hypothesis.HypothesisId} has {hypothesis.Questions.Count} question(s); " +
                    "number them from 1.");
            var questions = hypothesis.Questions.ToList();
            questions[request.Question - 1] = questions[request.Question - 1] with { Answer = answer };
            ledger.SetQuestions(hypothesis.HypothesisId, questions);
            return Done(ledger, hypothesis.HypothesisId);
        });
    }

    /// <summary>The neighbour check: what the ledger already says about a change, environment, host class and slot.</summary>
    public static CommandOutcome<HypothesisNeighboursResponse> Neighbours(HypothesisNeighboursRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var change = Squash(request.Change);
            var environment = Squash(request.Environment);
            var hostClass = Squash(request.HostClass);
            var slot = Squash(request.Slot);
            if (change.Length == 0)
                return Invalid<HypothesisNeighboursResponse>("Give the change, such as --change d>r; the shape index is keyed on it.");
            var ledger = new HypothesisRepository(database);
            var found = ledger.Neighbours(change, environment, hostClass, slot);
            var items = found.Select(h => Brief(ledger, h)).ToList();
            var query = string.Join(", ", new[]
            {
                $"change={change}", environment.Length > 0 ? $"environment={environment}" : null,
                hostClass.Length > 0 ? $"host class={hostClass}" : null, slot.Length > 0 ? $"slot={slot}" : null,
            }.Where(part => part is not null));
            return CommandOutcome<HypothesisNeighboursResponse>.Success(new HypothesisNeighboursResponse(query, items,
                found.Any(h => h.Status == HypothesisStatuses.Passed),
                found.Any(h => h.Status == HypothesisStatuses.Failed ||
                    ledger.Tests(h.HypothesisId).Any(t => t.Verdict == HypothesisVerdicts.Fail))));
        });
    }

    /// <summary>One hypothesis by id, or a page of them in id order.</summary>
    public static CommandOutcome<HypothesisListResponse> List(ListHypothesesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var ledger = new HypothesisRepository(database);
            if (request.HypothesisId is not null)
                return ledger.Get(request.HypothesisId) is { } one
                    ? CommandOutcome<HypothesisListResponse>.Success(new HypothesisListResponse([Item(ledger, one)], 1, 0))
                    : NotFound<HypothesisListResponse>(request.HypothesisId);
            var status = request.Status?.Trim().ToLowerInvariant();
            if (status is not null && !HypothesisStatuses.All.Contains(status))
                return Invalid<HypothesisListResponse>("The status must be proposed, passed, failed or superseded; leave it out for all.");
            if (request.Offset < 0 || request.Limit is < 1 or > 200)
                return Invalid<HypothesisListResponse>("The offset cannot be negative and the limit must be from 1 to 200.");
            var (page, total) = ledger.List(status, request.Offset, request.Limit);
            return CommandOutcome<HypothesisListResponse>.Success(
                new HypothesisListResponse(page.Select(h => Item(ledger, h)).ToList(), total, request.Offset));
        });
    }

    /// <summary>
    /// Generates the decision record or the speaker-question log from the ledger, so neither is written by hand. The
    /// question log never shows the predicted answers.
    /// </summary>
    public static CommandOutcome<HypothesisReportResponse> Report(HypothesisReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var kind = request.Kind?.Trim().ToLowerInvariant();
            if (kind is not ("decisions" or "questions"))
                return Invalid<HypothesisReportResponse>("The report kind is 'decisions' or 'questions'.");
            var ledger = new HypothesisRepository(database);
            var all = ledger.List(null, 0, int.MaxValue).Page;
            return CommandOutcome<HypothesisReportResponse>.Success(kind == "decisions"
                ? DecisionRecord(ledger, all) : QuestionLog(all));
        });
    }

    private static HypothesisReportResponse DecisionRecord(HypothesisRepository ledger, IReadOnlyList<HypothesisRecord> all)
    {
        var text = new StringBuilder("# Hypothesis decisions\n");
        var decided = all.Where(h => h.Status != HypothesisStatuses.Proposed).ToList();
        foreach (var h in decided)
        {
            var tests = ledger.Tests(h.HypothesisId);
            text.Append($"\n## {h.HypothesisId} {h.Status}: {h.ChangeText}{Condition(h)}\n\n{h.Statement}\n\n");
            text.Append($"- Mechanism: {h.Mechanism}\n- Decision: {h.Decision}\n");
            if (tests.Count > 0)
            {
                var last = tests[^1];
                text.Append($"- Test {last.TestNumber}: {last.Verdict}, {last.RuleHits} rule hits of {last.PredictedForms} predicted, " +
                    $"margin {last.Margin}\n");
            }
            if (h.SupersededBy is not null) text.Append($"- Superseded by {h.SupersededBy}\n");
            var proposals = ledger.ProposalsOf(h.HypothesisId);
            if (proposals.Count > 0) text.Append($"- Proposals: {string.Join(", ", proposals)}\n");
            text.Append(h.GdrId is not null ? $"- GDR: {h.GdrId}, {h.GdrSection}\n"
                : h.GdrNone is not null ? $"- GDR: none, {h.GdrNone}\n" : "");
        }
        if (decided.Count == 0) text.Append("\nNo hypothesis has been decided.\n");
        return new HypothesisReportResponse("decisions", text.ToString(), decided.Count);
    }

    private static HypothesisReportResponse QuestionLog(IReadOnlyList<HypothesisRecord> all)
    {
        var text = new StringBuilder("# Speaker questions\n");
        var count = 0;
        foreach (var h in all.Where(h => h.Status != HypothesisStatuses.Superseded && h.Questions.Count > 0))
        {
            text.Append($"\n## {h.HypothesisId}: {h.ChangeText}{Condition(h)}\n\n");
            for (var i = 0; i < h.Questions.Count; i++)
            {
                count++;
                var q = h.Questions[i];
                text.Append($"{i + 1}. {q.Question} {(q.Answer is null ? "(open)" : "Answer: " + q.Answer)}\n");
            }
        }
        if (count == 0) text.Append("\nNo questions are queued.\n");
        return new HypothesisReportResponse("questions", text.ToString(), count);
    }

    private static string Condition(HypothesisRecord h)
    {
        var parts = new[] { h.Environment, h.HostClass, h.Slot }.Where(p => p.Length > 0).ToList();
        return parts.Count == 0 ? "" : " (" + string.Join("; ", parts) + ")";
    }

    private static CommandOutcome<HypothesisResponse> Done(HypothesisRepository ledger, string id)
    {
        var h = ledger.Get(id)!;
        var neighbours = ledger.Neighbours(h.ChangeText, h.Environment, h.HostClass, h.Slot)
            .Where(o => !string.Equals(o.HypothesisId, h.HypothesisId, StringComparison.OrdinalIgnoreCase))
            .Select(o => Brief(ledger, o)).ToList();
        return CommandOutcome<HypothesisResponse>.Success(new HypothesisResponse(Item(ledger, h), neighbours));
    }

    private static HypothesisNeighbour Brief(HypothesisRepository ledger, HypothesisRecord h) =>
        new(h.HypothesisId, h.Status, h.Mechanism, h.ChangeText, h.Environment, h.Statement, h.Decision,
            ledger.Tests(h.HypothesisId).LastOrDefault()?.Verdict);

    internal static HypothesisItem Item(HypothesisRepository ledger, HypothesisRecord h) => new(
        h.HypothesisId, h.Pattern, h.Statement, h.Mechanism, h.ChangeText, h.Environment, h.HostClass, h.Slot, h.Neighbour,
        h.Predictions, h.Falsifier, h.Exceptions, h.ObjectsAdded, h.AttestedTypesGained,
        h.Questions.Select(q => new HypothesisQuestionItem(q.Question, q.Predicted, q.Answer)).ToList(), h.Status,
        h.Decision, h.SupersededBy, h.GdrId, h.GdrSection, h.GdrNone, ledger.ProposalsOf(h.HypothesisId),
        ledger.Tests(h.HypothesisId).Select(t => new HypothesisTestItem(t.TestNumber, t.Verdict, t.PredictedForms, t.RuleHits,
            t.Controls, t.Margin, t.TrialAttestedLosses, t.TrialNewReadings, t.TrialWorkSteps, t.Command, t.RecordedUtc)).ToList(),
        new HypothesisGradeItem(h.SealedVerdict, h.VerdictSource, h.WordQuestionsAsked, h.ObjectsBefore, h.TypesBefore,
            h.ObjectsAfter, h.TypesAfter, PerType(h.ObjectsBefore, h.TypesBefore), PerType(h.ObjectsAfter, h.TypesAfter),
            h.AgentHours, h.AlternativeHours),
        h.CreatedUtc, h.UpdatedUtc);

    private static double? PerType(int? objects, int? types) =>
        objects is { } o && types is > 0 ? Math.Round((double)o / types.Value, 3) : null;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    // Runs of spaces must not make two spellings of one shape part differ; case is folded by the index.
    private static string Squash(string? text) => Spaces.Replace((text ?? "").Trim(), " ");

    private static CommandOutcome<T> NotFound<T>(string id) where T : class =>
        Refused<T>(RefusalCodes.HypothesisNotFound, FailureReason.NotFound,
            $"Hypothesis '{id}' was not found. Use an id that list hypotheses returns, such as H-1.", id);

    private static CommandOutcome<T> Invalid<T>(string message) where T : class =>
        Refused<T>(RefusalCodes.HypothesisInvalid, FailureReason.InvalidArgument, message);

    private static CommandOutcome<T> Rule<T>(string message) where T : class =>
        Refused<T>(RefusalCodes.HypothesisRule, FailureReason.Refused, message);

    private static CommandOutcome<T> Refused<T>(string code, FailureReason reason, string message, string? id = null)
        where T : class =>
        CommandOutcome<T>.Refused(new Refusal(code, reason, message,
            id is null ? null : new Dictionary<string, string>(StringComparer.Ordinal) { ["hypothesisId"] = id }));
}
