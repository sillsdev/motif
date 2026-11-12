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

    // ---- P-entry-duplicate

    internal static ParsimonyMeasureResult DecideDuplicateEntries(MeasureDefinition measure, LexiconInput input,
        MeasureContext context)
    {
        var prepared = input.Entries.Select(PrepareEntry).ToArray();
        var groups = new List<(string Kind, EntryView[] Members)>();
        foreach (var group in prepared.Where(IsDuplicateCandidate)
                     .GroupBy(item => (item.Class, item.FormKey, item.GlossKey, item.MsaKey))
                     .Where(group => group.Count() > 1))
            groups.Add(("duplicate", group.ToArray()));
        foreach (var bucket in prepared.Where(item => item.FormKey.Length > 0).GroupBy(item => (item.Class, item.FormKey))
                     .Where(bucket => bucket.Count() > 1))
            groups.AddRange(CloneGroups(bucket.ToArray()).Select(members => ("clone", members)));
        var findings = new List<ParsimonyFinding>();
        var redundant = 0L;
        foreach (var (kind, members) in groups.Select(item => (item.Kind, Members: item.Members
                     .OrderBy(member => member.Entry.EntryGuid, StringComparer.Ordinal).ToArray()))
                     .OrderBy(item => item.Kind, StringComparer.Ordinal)
                     .ThenBy(item => item.Members[0].Entry.EntryGuid, StringComparer.Ordinal))
        {
            redundant += members.Length - 1;
            findings.Add(EntryDuplicateFinding(measure, context, kind, members, prepared.Length));
        }
        return Result(measure, ParsimonyMeasureStatus.Computed, prepared.Length, redundant, findings, null);
    }

    private static ParsimonyFinding EntryDuplicateFinding(MeasureDefinition measure, MeasureContext context,
        string kind, EntryView[] members, int entryCount)
    {
        var ids = members.Select(member => member.Entry.EntryGuid).ToArray();
        var groupKey = ParsimonyFindingIdentity.DuplicateEntriesGroupKey(kind);
        var identity = ParsimonyFindingIdentity.DuplicateEntriesIdentity(kind, ids);
        string Label(EntryView member) => context.Describe(member.Entry.EntryGuid) is { } words
            ? $"\"{words}\" ({member.Entry.MorphType}, {member.Entry.EntryGuid})"
            : $"{member.Entry.EntryGuid} ({member.Entry.MorphType})";
        var meaning = kind == "duplicate"
            ? "These entries have the same form, morph type class, glosses and MSA signatures."
            : "Every gloss of one of these entries is a gloss of a same-form homograph plus a dotted suffix, " +
              "or the capitalised form plus a dotted suffix: a clone made to carry a feature.";
        var limits = new List<string>
        {
            $"{(kind == "duplicate" ? "Duplicate" : "Clone")} entries: {string.Join("; ", members.Select(Label))}.",
            meaning,
        };
        if (members[0].FormKey.Length == 0)
            limits.Add("The lexeme form has no form text, so the entries were grouped by morph type class, glosses and MSA signatures alone.");
        return new ParsimonyFinding(
            ParsimonyFindingIdentity.GroupFindingId(measure.Id, identity), measure.Id, measure.Axis, measure.Tier,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                GroupKind: ParsimonyGroupKind.DuplicateEntries),
            groupKey, new ParsimonyMeasureNumber(members.Length - 1, entryCount, measure.Unit), measure.Threshold!,
            MeasureEvidence.Compute(measure, context.Digest, JsonSerializer.Serialize(new
            {
                kind, members = ids, members[0].Class, members[0].FormKey,
            })),
            members.Where(member => member.Entry.LexemeAllomorphGuid is not null).Select(member =>
                EvidenceReference(context.BundleId, ParsimonyViews.AllomorphContext,
                    new { objectGuid = member.Entry.LexemeAllomorphGuid })).ToArray(),
            measure.RecipeLink, limits, ParsimonyVerification.NotRun);
    }

    // A pure variant entry has no sense and no MSA; a blank entry needs a gloss or MSA, or all blanks would match.
    private static bool IsDuplicateCandidate(EntryView item) =>
        (item.Entry.Senses.Count > 0 || item.Entry.MsaSignatures.Count > 0) &&
        (item.FormKey.Length > 0 || item.GlossKey.Length > 0 || item.MsaKey.Length > 0);

    // Clone pairs link entries; each connected set of two or more is one group.
    private static IEnumerable<EntryView[]> CloneGroups(EntryView[] bucket)
    {
        var parent = Enumerable.Range(0, bucket.Length).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        var linked = new bool[bucket.Length];
        for (var a = 0; a < bucket.Length; a++)
            for (var b = 0; b < bucket.Length; b++)
            {
                if (a == b || !IsClone(bucket[a], bucket[b])) continue;
                parent[Find(a)] = Find(b);
                linked[a] = linked[b] = true;
            }
        return Enumerable.Range(0, bucket.Length).Where(i => linked[i]).GroupBy(Find)
            .Select(group => group.Select(i => bucket[i]).ToArray());
    }

    // Every gloss of the clone is an origin gloss or the capitalised form, plus a dotted suffix.
    private static bool IsClone(EntryView clone, EntryView origin)
    {
        if (clone.GlossTexts.Length == 0) return false;
        foreach (var gloss in clone.GlossTexts)
        {
            var fromForm = clone.UpperForms.Any(upper =>
                gloss.Length > upper.Length + 1 && gloss[upper.Length] == '.' &&
                gloss.StartsWith(upper, StringComparison.Ordinal));
            if (fromForm) continue;
            var fromHomograph = false;
            for (var dot = gloss.IndexOf('.'); dot >= 0 && !fromHomograph; dot = gloss.IndexOf('.', dot + 1))
                fromHomograph = dot > 0 && dot < gloss.Length - 1 && origin.GlossSet.Contains(gloss[..dot]);
            if (!fromHomograph) return false;
        }
        return true;
    }

    internal static string MorphTypeClass(string morphType) => morphType switch
    {
        "prefix" or "prefixingInterfix" => "prefix",
        "suffix" or "suffixingInterfix" => "suffix",
        "infix" or "infixingInterfix" => "infix",
        "stem" or "root" or "boundStem" or "boundRoot" => "stem",
        "phrase" or "discontigPhrase" => "phrase",
        _ => morphType,
    };

    private static EntryView PrepareEntry(LexiconEntryFact entry)
    {
        var forms = entry.LexemeForms.Select(form => (form.WritingSystem, Form: TrimMarks(form.Text)))
            .Where(form => form.Form.Length > 0).Distinct().OrderBy(form => form.WritingSystem, StringComparer.Ordinal)
            .ThenBy(form => form.Form, StringComparer.Ordinal).ToArray();
        var glosses = entry.Senses.SelectMany(sense => sense.Glosses).Select(gloss => (gloss.WritingSystem, Text: gloss.Text.Trim()))
            .Where(gloss => gloss.Text.Length > 0).Distinct().ToArray();
        var texts = glosses.Select(gloss => TrimMarks(gloss.Text)).Where(text => text.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        return new EntryView(entry, MorphTypeClass(entry.MorphType),
            string.Join("\n", forms.Select(form => form.WritingSystem + "=" + form.Form)),
            string.Join("\n", glosses.Select(gloss => gloss.WritingSystem + "=" + gloss.Text).Order(StringComparer.Ordinal)),
            string.Join("\n", entry.MsaSignatures), texts,
            new HashSet<string>(texts, StringComparer.OrdinalIgnoreCase),
            forms.Select(form => form.Form.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray());
    }

    private sealed record EntryView(LexiconEntryFact Entry, string Class, string FormKey, string GlossKey, string MsaKey,
        string[] GlossTexts, HashSet<string> GlossSet, string[] UpperForms);
}
