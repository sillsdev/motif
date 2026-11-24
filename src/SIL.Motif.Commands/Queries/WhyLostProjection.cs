using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Diffs a Baseline trace against a Trial copy's trace by the canonical ids of each Reading's morphs, and for each lost
/// Reading reports the stop PanGloss recorded. It runs no parser and infers no cause: a lost Reading with no recorded
/// stop is reported as not recorded, and an operation is named only through an id the recorded stop itself names.
/// </summary>
internal static class WhyLostProjection
{
    /// <summary>The one stop reason this slice reads; <c>allomorph_blocked</c> in the parser's own words.</summary>
    internal const string BlockedReason = "allomorph_blocked";

    /// <summary>Reads one id of an authored object: its form text and owning entry, when the Trial copy holds them.</summary>
    internal interface IFormReader
    {
        /// <summary>The form's written text, or null when it cannot be read.</summary>
        string? TextOf(string formId);

        /// <summary>The id of the entry that owns the form, or null when none is known.</summary>
        string? EntryOf(string formId);
    }

    /// <summary>A reader that knows nothing, used when no copy could be opened.</summary>
    internal sealed class NoForms : IFormReader
    {
        public string? TextOf(string formId) => null;

        public string? EntryOf(string formId) => null;
    }

    /// <summary>Compares the two traces and names, for each lost Reading, what PanGloss recorded.</summary>
    internal static WhyLostResponse Project(string word, string proposalId, string? trialJobId,
        RejectionTraceResponse baseline, RejectionTraceResponse trial, bool baselineDescribesBlocked,
        bool trialDescribesBlocked, Proposal proposal, RegressionContext context, IFormReader forms)
    {
        var trialRemaining = trial.Outputs.ToList();
        var kept = new List<WhyLostReading>();
        var lostOutputs = new List<RejectionTraceOutput>();
        foreach (var output in baseline.Outputs)
        {
            var key = KeyOf(output);
            var match = trialRemaining.FindIndex(candidate => KeyOf(candidate) == key);
            if (match < 0) { lostOutputs.Add(output); continue; }
            trialRemaining.RemoveAt(match);
            kept.Add(ToReading(output));
        }
        var gained = trialRemaining.Select(ToReading).ToList();
        var blocks = RecordedBlocks(trial);
        var incomplete = Incompleteness(trial);
        var lost = lostOutputs
            .Select(output => ToReading(output) with
            {
                Causes = CausesFor(output, blocks, trialDescribesBlocked, incomplete, proposal, context, forms),
            })
            .ToList();

        var notes = new List<string>
        {
            "A recorded stop is one PanGloss recorded on some Trial path that names a form this Reading uses; where it recorded " +
            "none the answer is 'not recorded' and Motif names no operation.",
            "An operation is listed only when it creates, edits or places a form beside the earlier form's id, or names an " +
            "object that form belongs to; the list is an overlap of ids, not a cause.",
        };
        if (baseline.Outputs.Count == 0) notes.Add("The word has no Reading at the Baseline, so nothing can be lost.");
        else if (lost.Count == 0) notes.Add("No Reading the Baseline has is missing from the Trial copy.");
        if (baseline.Bound.OutputsOmittedByCap > 0 || trial.Bound.OutputsOmittedByCap > 0)
            notes.Add("A side has more outputs than the preview keeps, so the lost and gained lists may be incomplete.");
        if (baseline.Outputs.Concat(trial.Outputs).Any(output => output.Morphs.Any(morph => morph.Kind != "authored")))
            notes.Add("A morph has no authored identity, so a Reading that uses it can appear as both lost and gained.");
        if (trial.Search.Status != "complete" || baseline.Search.Status != "complete")
            notes.Add("A search did not run to completion, so a missing Reading may only be one the search did not reach.");
        return new WhyLostResponse(word, proposalId, trialJobId,
            Side(baseline, baselineDescribesBlocked), Side(trial, trialDescribesBlocked), lost, gained, kept)
        { Notes = notes };
    }

    private static WhyLostSide Side(RejectionTraceResponse response, bool described) =>
        new(response.TracedAgainst, response.Producer, response.Search, response.Outputs.Count, described);

    private static WhyLostReading ToReading(RejectionTraceOutput output) => new(output.Surface, output.Morphs);

    /// <summary>One morph's identity: its entry, analysis and form ids, or the parser's key when unbound.</summary>
    private static string KeyOf(RejectionTraceOutput output) =>
        string.Join(">", output.Morphs.Select(morph => string.Join("/", new[] { morph.Entry, morph.Msa }
            .Concat(morph.Forms).Select(Identity))));

    private static string Identity(RejectionTraceObject? value) =>
        value is null ? "-" : SubjectIds(value) is { Length: > 0 } ids ? string.Join("+", ids) : "key:" + value.Key;

    /// <summary>The canonical ids that name the object itself.</summary>
    private static string[] SubjectIds(RejectionTraceObject value) =>
        [.. value.Ids.Where(id => id.IsSubject).Select(id => id.Id).Distinct(StringComparer.Ordinal)];

    private static IEnumerable<string> FormIds(RejectionTraceOutput output) =>
        output.Morphs.SelectMany(morph => morph.Forms).SelectMany(SubjectIds);

    private sealed record Block(RejectionTraceEvent Stop, RejectionTraceObject? Blocker, string[] BlockedIds,
        string[] BlockerIds);

    /// <summary>The recorded allomorph_blocked stops of the Trial copy, once per blocked and blocker pair.</summary>
    private static List<Block> RecordedBlocks(RejectionTraceResponse trial)
    {
        var blocks = new List<Block>();
        foreach (var path in trial.Paths)
        {
            if (path.EarliestRejection is not { Reason: BlockedReason } stop) continue;
            var blocker = stop.Related.FirstOrDefault(related => related.Role == "blocker")?.Object;
            var blockedIds = FormIdsOf(stop.Subject);
            var blockerIds = blocker is null ? [] : FormIdsOf(blocker);
            if (blocks.Any(block => block.BlockedIds.SequenceEqual(blockedIds) && block.BlockerIds.SequenceEqual(blockerIds)))
                continue;
            blocks.Add(new Block(stop, blocker, blockedIds, blockerIds));
        }
        return blocks;
    }

    private static string[] FormIdsOf(RejectionTraceObject value) =>
        [.. value.Ids.Where(id => id.IsSubject && id.Kind == "form").Select(id => id.Id).Distinct(StringComparer.Ordinal)];

    /// <summary>Why a stop could be missing from what was read, or empty when nothing was cut off.</summary>
    private static string Incompleteness(RejectionTraceResponse trial)
    {
        var reasons = new List<string>();
        if (trial.Search.Status != "complete") reasons.Add("the Trial search did not run to completion");
        if (trial.Bound.PathsOmittedByCap > 0)
            reasons.Add($"{trial.Bound.PathsOmittedByCap} terminal path(s) were not read");
        if (trial.Capture.TruncatedBy.Count > 0)
            reasons.Add("PanGloss's capture was cut off by " + string.Join(", ", trial.Capture.TruncatedBy));
        return reasons.Count == 0 ? string.Empty : " A stop may be among what was not read: " + string.Join("; ", reasons) + ".";
    }

    private static IReadOnlyList<WhyLostCause> CausesFor(RejectionTraceOutput output, List<Block> blocks, bool described,
        string incomplete, Proposal proposal, RegressionContext context, IFormReader forms)
    {
        if (!described)
            return [new WhyLostCause("not-recorded",
                "Not recorded: the PanGloss that ran does not describe an allomorph_blocked stop, so a Reading lost to an " +
                "earlier allomorph leaves no recorded stop. Motif does not infer one." + incomplete)];
        var formIds = FormIds(output).ToHashSet(StringComparer.Ordinal);
        var causes = blocks.Where(block => block.BlockedIds.Any(formIds.Contains))
            .Select(block => Cause(block, proposal, context, forms)).ToList();
        if (causes.Count > 0) return causes;
        return [new WhyLostCause("not-recorded",
            "Not recorded: PanGloss recorded no stop on the Trial copy that names a form of this Reading. " +
            "Motif does not infer one." + incomplete)];
    }

    private static WhyLostCause Cause(Block block, Proposal proposal, RegressionContext context, IFormReader forms)
    {
        var blocked = FormOf(block.Stop.Subject, forms, context);
        var blocker = block.Blocker is null ? new WhyLostForm(null, null, null) : FormOf(block.Blocker, forms, context);
        var operations = blocker.Id is null ? [] : Join(proposal, context, blocker.Id);
        var text = $"On a Trial path PanGloss recorded {Name(blocked)} as blocked by {Earlier(blocker)} " +
            $"(allomorph_blocked, stage {block.Stop.Stage}); the lost Reading uses the blocked form.";
        if (blocker.Id is null)
            text += " PanGloss did not bind the earlier form to an authored object, so Motif names no operation.";
        else if (operations.Count == 0)
            text += " No Proposal operation names the earlier form.";
        else
            text += " Overlap with the Proposal, not a recorded cause: " +
                string.Join(" ", operations.Select(operation => operation.Sentence));
        return new WhyLostCause("recorded", text)
        {
            Reason = block.Stop.Reason, Stage = block.Stop.Stage, Blocked = blocked, Blocker = blocker, Operations = operations,
        };
    }

    private static string Name(WhyLostForm form) =>
        form.Id is null ? "a form with no authored identity"
        : form.Text is null ? $"the form {form.Id}" : $"the form {form.Text} ({form.Id})";

    private static string Earlier(WhyLostForm form) =>
        form.Id is null ? "an earlier form with no authored identity"
        : form.Text is null ? $"the earlier form {form.Id}" : $"the earlier form {form.Text} ({form.Id})";

    private static WhyLostForm FormOf(RejectionTraceObject value, IFormReader forms, RegressionContext context)
    {
        var id = FormIdsOf(value) is { Length: > 0 } ids ? ids[0] : null;
        var entry = value.Ids.FirstOrDefault(related => related.Kind == "entry")?.Id;
        if (id is null) return new WhyLostForm(null, entry, null);
        return new WhyLostForm(id, entry ?? context.EntryOf(id) ?? forms.EntryOf(id), forms.TextOf(id));
    }

    /// <summary>The operations that name <paramref name="formId"/>, in Proposal order.</summary>
    internal static IReadOnlyList<WhyLostOperation> Join(Proposal proposal, RegressionContext context, string formId)
    {
        var guid = RegressionContext.ToGuid(formId);
        if (guid is null) return [];
        var result = new List<WhyLostOperation>();
        var position = 0;
        foreach (var operation in proposal.Operations)
        {
            position++;
            var role = RoleOf(operation, guid.Value, context.Operations[position - 1].Ids);
            if (role is null) continue;
            var (name, sentence) = role.Value;
            result.Add(new WhyLostOperation(position, operation.OperationId.ToString(), operation.Kind, name,
                $"Operation {position} ({operation.Kind}) {sentence}"));
        }
        return result;
    }

    private static (string Role, string Sentence)? RoleOf(OperationEnvelope operation, Guid id, IReadOnlySet<Guid> expanded)
    {
        if (Is(operation.EntityId, id) || Is(operation.StorageIdOverride, id))
            return ("creates", "creates this form.");
        if (Is(operation.Target, id)) return ("edits", "edits this form.");
        if (Is(operation.Placement?.After, id) || Is(operation.Placement?.Before, id))
            return ("placed-beside", "places a form beside this form.");
        if (OwnerOf(operation) is { } owner && owner == id)
            return ("added-to", "adds a member to this form.");
        return expanded.Contains(id)
            ? ("names-object-it-uses", "names an object this form belongs to or uses.")
            : null;
    }

    private static bool Is(CanonicalId? value, Guid id) => value is { } canonical && canonical.ToGuid() == id;

    private static Guid? OwnerOf(OperationEnvelope operation) =>
        operation.After is { ValueKind: System.Text.Json.JsonValueKind.Object } after &&
        after.TryGetProperty("owner", out var owner) && owner.ValueKind == System.Text.Json.JsonValueKind.String &&
        CanonicalId.TryParse(owner.GetString()!, out var ownerId) ? ownerId.ToGuid() : null;
}
