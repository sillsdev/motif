using System.Text.Json;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

public static partial class MeasureRunner
{
    internal static ParsimonyMeasureResult DecideStemMsaWithoutCategory(MeasureDefinition measure,
        StemMsaInput input, MeasureContext context)
    {
        var findings = input.Missing.OrderBy(item => item.MsaGuid, StringComparer.Ordinal).Select(item =>
        {
            var identity = ParsimonyFindingIdentity.MsaIdentity(item.EntryGuid, item.MsaGuid);
            var name = context.Describe(item.EntryGuid);
            var entry = name is null ? $"Entry {item.EntryGuid}" : $"Entry \"{name}\"";
            var reading = item.EntryHasGlosses
                ? "its senses have glosses, so a part of speech is probably missing"
                : "none of its senses has a gloss, so it looks like a stub";
            return new ParsimonyFinding(
                ParsimonyFindingIdentity.GroupFindingId(measure.Id, identity), measure.Id, measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                    GroupKind: ParsimonyGroupKind.MsaWithoutCategory),
                ParsimonyFindingIdentity.EntryGroupKey(item.EntryGuid),
                new ParsimonyMeasureNumber(1, input.StemMsaCount, measure.Unit), measure.Threshold!,
                MeasureEvidence.Compute(measure, context.Digest, JsonSerializer.Serialize(new
                {
                    item.EntryGuid, item.MsaGuid, item.EntryHasGlosses,
                })),
                [EvidenceReference(context.BundleId, ParsimonyViews.AffixContext, new { objectGuid = item.MsaGuid })],
                measure.RecipeLink,
                [$"{entry} has a stem MSA with no part of speech; {reading}.",
                 "Only the stem MSA's own part-of-speech row was read; Text content was not used."],
                ParsimonyVerification.NotRun);
        }).ToArray();
        return Result(measure, ParsimonyMeasureStatus.Computed, input.StemMsaCount, findings.LongLength, findings, null);
    }

    // ---- P-gloss-echoes-form

    internal static ParsimonyMeasureResult DecideGlossEchoesForm(MeasureDefinition measure, LexiconInput input,
        MeasureContext context)
    {
        var glossed = 0L;
        var echoes = new List<(LexiconEntryFact Entry, LexiconSenseFact Sense, string Gloss, string Form)>();
        foreach (var entry in input.Entries.OrderBy(item => item.EntryGuid, StringComparer.Ordinal))
        {
            var forms = entry.AllForms.Select(TrimMarks).Where(form => form.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var sense in entry.Senses.OrderBy(item => item.SenseGuid, StringComparer.Ordinal))
            {
                var glosses = sense.Glosses.Select(gloss => TrimMarks(gloss.Text)).Where(text => text.Length > 0)
                    .ToArray();
                if (glosses.Length == 0) continue;
                glossed++;
                foreach (var gloss in glosses)
                {
                    var form = forms.FirstOrDefault(candidate => GlossEchoes(gloss, candidate));
                    if (form is null) continue;
                    echoes.Add((entry, sense, gloss, form));
                    break;
                }
            }
        }
        var findings = echoes.Select(item => GlossEchoFinding(measure, context, item.Entry, item.Sense, item.Gloss,
            item.Form, glossed)).ToArray();
        return Result(measure, ParsimonyMeasureStatus.Computed, glossed, findings.LongLength, findings, null);
    }

    private static ParsimonyFinding GlossEchoFinding(MeasureDefinition measure, MeasureContext context,
        LexiconEntryFact entry, LexiconSenseFact sense, string gloss, string form, long glossedSenses)
    {
        var identity = ParsimonyFindingIdentity.SenseIdentity(entry.EntryGuid, sense.SenseGuid);
        var name = context.Describe(entry.EntryGuid) is { } words ? $"\"{words}\"" : entry.EntryGuid;
        var shape = gloss.Equals(form, StringComparison.OrdinalIgnoreCase)
            ? "the form itself"
            : "the form plus a dotted suffix, which is the mark of a clone entry";
        var references = entry.LexemeAllomorphGuid is null ? []
            : new[] { EvidenceReference(context.BundleId, ParsimonyViews.AllomorphContext,
                new { objectGuid = entry.LexemeAllomorphGuid }) };
        return new ParsimonyFinding(
            ParsimonyFindingIdentity.GroupFindingId(measure.Id, identity), measure.Id, measure.Axis, measure.Tier,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                GroupKind: ParsimonyGroupKind.GlossEchoesForm),
            ParsimonyFindingIdentity.EntryGroupKey(entry.EntryGuid),
            new ParsimonyMeasureNumber(1, glossedSenses, measure.Unit), measure.Threshold!,
            MeasureEvidence.Compute(measure, context.Digest, JsonSerializer.Serialize(new
            {
                entry.EntryGuid, sense.SenseGuid, gloss, form,
            })),
            references, measure.RecipeLink,
            [$"The gloss \"{gloss}\" of entry {name} is {shape}, \"{form}\", ignoring hyphens and case.",
             "Only the grammar facts were read; Text content was not used."],
            ParsimonyVerification.NotRun);
    }

    internal static string TrimMarks(string value) => value.Trim().Trim('-', '=').Trim();

    /// <summary>Whether a trimmed gloss is the trimmed form, or the form followed by a dot and a non-empty suffix.</summary>
    internal static bool GlossEchoes(string gloss, string form) =>
        gloss.Equals(form, StringComparison.OrdinalIgnoreCase) ||
        (gloss.Length > form.Length + 1 && gloss[form.Length] == '.' &&
         gloss.StartsWith(form, StringComparison.OrdinalIgnoreCase));
}
