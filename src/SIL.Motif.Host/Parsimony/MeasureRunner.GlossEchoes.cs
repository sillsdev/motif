using System.Text.Json;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

public static partial class MeasureRunner
{
    internal static ParsimonyMeasureResult DecideGlossEchoesForm(MeasureDefinition measure, GlossEchoInput input,
        MeasureContext context)
    {
        // One echo per sense: the smallest gloss and form, so the finding does not depend on the row order.
        var echoes = new Dictionary<string, (GlossEchoCandidate Candidate, string Gloss, string Form)>(StringComparer.Ordinal);
        foreach (var candidate in input.Candidates)
        {
            var gloss = TrimMarks(candidate.Gloss);
            var form = TrimMarks(candidate.Form);
            if (form.Length == 0 || !GlossEchoes(gloss, form)) continue;
            if (echoes.TryGetValue(candidate.SenseGuid, out var kept))
            {
                var order = string.CompareOrdinal(kept.Gloss, gloss);
                if (order == 0) order = string.CompareOrdinal(kept.Form, form);
                if (order <= 0) continue;
            }
            echoes[candidate.SenseGuid] = (candidate, gloss, form);
        }
        var findings = echoes.Values.OrderBy(item => item.Candidate.EntryGuid, StringComparer.Ordinal)
            .ThenBy(item => item.Candidate.SenseGuid, StringComparer.Ordinal)
            .Select(item => GlossEchoFinding(measure, context, item.Candidate, item.Gloss, item.Form, input.GlossedSenses))
            .ToArray();
        return Result(measure, ParsimonyMeasureStatus.Computed, input.GlossedSenses, findings.LongLength, findings, null);
    }

    private static ParsimonyFinding GlossEchoFinding(MeasureDefinition measure, MeasureContext context,
        GlossEchoCandidate candidate, string gloss, string form, long glossedSenses)
    {
        var identity = ParsimonyFindingIdentity.SenseIdentity(candidate.EntryGuid, candidate.SenseGuid);
        var name = context.Describe(candidate.EntryGuid) is { } words ? $"\"{words}\"" : candidate.EntryGuid;
        var shape = gloss.Equals(form, StringComparison.OrdinalIgnoreCase)
            ? "the form itself"
            : "the form plus a dotted suffix, which is the mark of a clone entry";
        var references = candidate.LexemeAllomorphGuid is null ? []
            : new[] { EvidenceReference(context.BundleId, ParsimonyViews.AllomorphContext,
                new { objectGuid = candidate.LexemeAllomorphGuid }) };
        return new ParsimonyFinding(
            ParsimonyFindingIdentity.GroupFindingId(measure.Id, identity), measure.Id, measure.Axis, measure.Tier,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                GroupKind: ParsimonyGroupKind.GlossEchoesForm),
            ParsimonyFindingIdentity.EntryGroupKey(candidate.EntryGuid),
            new ParsimonyMeasureNumber(1, glossedSenses, measure.Unit), measure.Threshold!,
            MeasureEvidence.Compute(measure, context.Digest, JsonSerializer.Serialize(new
            {
                candidate.EntryGuid, candidate.SenseGuid, gloss, form,
            })),
            references, measure.RecipeLink,
            [$"The gloss \"{gloss}\" of entry {name} is {shape}, \"{form}\", ignoring hyphens and case.",
             "Only the grammar facts were read; Text content was not used."],
            ParsimonyVerification.NotRun);
    }

    /// <summary>
    /// Whether a trimmed gloss is the trimmed form, or the form followed by a dot and a non-empty suffix.
    /// </summary>
    internal static bool GlossEchoes(string gloss, string form) =>
        gloss.Equals(form, StringComparison.OrdinalIgnoreCase) ||
        (gloss.Length > form.Length + 1 && gloss[form.Length] == '.' &&
         gloss.StartsWith(form, StringComparison.OrdinalIgnoreCase));
}
