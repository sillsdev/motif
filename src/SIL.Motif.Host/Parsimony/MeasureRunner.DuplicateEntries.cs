using System.Text.Json;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

public static partial class MeasureRunner
{
    internal static ParsimonyMeasureResult DecideDuplicateEntries(MeasureDefinition measure, LexiconInput input,
        MeasureContext context)
    {
        var duplicates = new Dictionary<string, List<EntryView>>(StringComparer.Ordinal);
        var homographs = new Dictionary<string, List<EntryView>>(StringComparer.Ordinal);
        var entryCount = input.Entries.Count;
        foreach (var fact in input.Entries)
        {
            var view = PrepareEntry(fact);
            if (IsDuplicateCandidate(view))
                AddTo(duplicates, $"{view.Class}\0{view.FormKey}\0{view.GlossKey}\0{view.MsaKey}", view);
            if (view.FormKey.Length > 0) AddTo(homographs, view.Class + "\0" + view.FormKey, view);
        }
        var groups = new List<EntryGroup>();
        foreach (var members in duplicates.Values)
            if (members.Count > 1)
                groups.Add(new EntryGroup("duplicate", members.OrderBy(item => item.Entry.EntryGuid, StringComparer.Ordinal).ToArray(),
                    members.Count - 1));
        foreach (var bucket in homographs.Values)
            if (bucket.Count > 1) AddCloneGroups(bucket, groups);
        groups.Sort((a, b) => a.Kind != b.Kind ? string.CompareOrdinal(a.Kind, b.Kind)
            : string.CompareOrdinal(a.Members[0].Entry.EntryGuid, b.Members[0].Entry.EntryGuid));
        var findings = new List<ParsimonyFinding>(groups.Count);
        var redundant = 0L;
        foreach (var group in groups)
        {
            redundant += group.Redundant;
            findings.Add(EntryDuplicateFinding(measure, context, group, entryCount));
        }
        return Result(measure, ParsimonyMeasureStatus.Computed, entryCount, redundant, findings, null);
    }

    private static void AddTo(Dictionary<string, List<EntryView>> map, string key, EntryView view)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(view);
    }

    private static ParsimonyFinding EntryDuplicateFinding(MeasureDefinition measure, MeasureContext context,
        EntryGroup group, int entryCount)
    {
        var members = group.Members;
        var ids = new string[members.Length];
        for (var i = 0; i < ids.Length; i++) ids[i] = members[i].Entry.EntryGuid;
        var identity = ParsimonyFindingIdentity.DuplicateEntriesIdentity(group.Kind, ids);
        string Label(EntryView member) => context.Describe(member.Entry.EntryGuid) is { } words
            ? $"\"{words}\" ({member.Entry.MorphType}, {member.Entry.EntryGuid})"
            : $"{member.Entry.EntryGuid} ({member.Entry.MorphType})";
        var clone = group.Kind == "clone";
        var limits = new List<string>
        {
            $"{(clone ? "Clone" : "Duplicate")} entries: {string.Join("; ", members.Select(Label))}.",
            clone
                ? "The first entry is the clone: every one of its glosses is a gloss of a same-form homograph plus a " +
                  "dotted suffix, or the capitalised form plus a dotted suffix. It carries a feature as a copy."
                : "These entries have the same form, morph type class, glosses and MSA signatures.",
        };
        if (clone && members.Length == 1)
            limits.Add("No same-form homograph carries the base gloss; the glosses are the capitalised form plus a dotted suffix.");
        if (members[0].FormKey.Length == 0)
            limits.Add("The lexeme form has no form text, so the entries were grouped by morph type class, glosses and MSA signatures alone.");
        return new ParsimonyFinding(
            ParsimonyFindingIdentity.GroupFindingId(measure.Id, identity), measure.Id, measure.Axis, measure.Tier,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                GroupKind: ParsimonyGroupKind.DuplicateEntries),
            ParsimonyFindingIdentity.DuplicateEntriesGroupKey(group.Kind),
            new ParsimonyMeasureNumber(group.Redundant, entryCount, measure.Unit), measure.Threshold!,
            MeasureEvidence.Compute(measure, context.Digest, JsonSerializer.Serialize(new
            {
                kind = group.Kind, members = ids, members[0].Class, members[0].FormKey,
            })),
            members.Where(member => member.Entry.LexemeAllomorphGuid is not null).Select(member =>
                EvidenceReference(context.BundleId, ParsimonyViews.AllomorphContext,
                    new { objectGuid = member.Entry.LexemeAllomorphGuid })).ToArray(),
            measure.RecipeLink, limits, ParsimonyVerification.NotRun);
    }

    // A pure variant entry has no sense and no MSA; a blank entry needs a gloss or MSA, or all blanks would match.
    private static bool IsDuplicateCandidate(EntryView item) =>
        (item.Entry.HasSense || item.Entry.MsaSignatures.Count > 0) &&
        (item.FormKey.Length > 0 || item.GlossKey.Length > 0 || item.MsaKey.Length > 0);

    // Each clone is its own group: the clone first, then only the homographs whose glosses it extends.
    private static void AddCloneGroups(List<EntryView> bucket, List<EntryGroup> groups)
    {
        foreach (var clone in bucket)
        {
            if (!clone.EveryGlossDotted) continue;
            var origins = new List<EntryView>();
            if (!clone.EveryGlossFromForm)
            {
                foreach (var other in bucket)
                    if (!ReferenceEquals(other, clone) && ExtendsHomograph(clone, other)) origins.Add(other);
                if (origins.Count == 0) continue;
                origins.Sort((a, b) => string.CompareOrdinal(a.Entry.EntryGuid, b.Entry.EntryGuid));
            }
            origins.Insert(0, clone);
            groups.Add(new EntryGroup("clone", origins.ToArray(), 1));
        }
    }

    // Every gloss of the clone is a gloss of the origin or the capitalised form, plus a dotted suffix.
    private static bool ExtendsHomograph(EntryView clone, EntryView origin)
    {
        foreach (var gloss in clone.GlossTexts)
        {
            if (clone.GlossFromForm(gloss)) continue;
            var found = false;
            for (var dot = gloss.IndexOf('.'); dot >= 0 && !found; dot = gloss.IndexOf('.', dot + 1))
                found = dot > 0 && dot < gloss.Length - 1 && origin.GlossSet.Contains(gloss[..dot]);
            if (!found) return false;
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
        var formKeys = new List<string>(entry.LexemeForms.Count);
        var formTexts = new List<string>(entry.LexemeForms.Count);
        foreach (var form in entry.LexemeForms)
        {
            var text = TrimMarks(form.Text);
            if (text.Length == 0) continue;
            var key = form.WritingSystem + "=" + text;
            if (formKeys.Contains(key)) continue;
            formKeys.Add(key);
            if (!formTexts.Contains(text)) formTexts.Add(text);
        }
        var glossKeys = new List<string>(entry.Glosses.Count);
        var glossTexts = new List<string>(entry.Glosses.Count);
        foreach (var gloss in entry.Glosses)
        {
            var trimmed = gloss.Text.Trim();
            if (trimmed.Length == 0) continue;
            var key = gloss.WritingSystem + "=" + trimmed;
            if (glossKeys.Contains(key)) continue;
            glossKeys.Add(key);
            var text = TrimMarks(trimmed);
            if (text.Length > 0 && !glossTexts.Contains(text)) glossTexts.Add(text);
        }
        var msas = new List<string>(entry.MsaSignatures);
        if (msas.Count > 1)
        {
            msas.Sort(StringComparer.Ordinal);
            for (var i = msas.Count - 1; i > 0; i--)
                if (msas[i] == msas[i - 1]) msas.RemoveAt(i);
        }
        return new EntryView(entry, MorphTypeClass(entry.MorphType), JoinSorted(formKeys), JoinSorted(glossKeys),
            string.Join("\n", msas), glossTexts.ToArray(), formTexts.ToArray());
    }

    private static string JoinSorted(List<string> keys)
    {
        if (keys.Count > 1) keys.Sort(StringComparer.Ordinal);
        return keys.Count == 1 ? keys[0] : string.Join("\n", keys);
    }

    private sealed record EntryGroup(string Kind, EntryView[] Members, int Redundant);

    private sealed class EntryView(LexiconEntryFact entry, string morphClass, string formKey, string glossKey,
        string msaKey, string[] glossTexts, string[] formTexts)
    {
        private HashSet<string>? _glossSet;
        private string[]? _upperForms;

        public LexiconEntryFact Entry => entry;
        public string Class => morphClass;
        public string FormKey => formKey;
        public string GlossKey => glossKey;
        public string MsaKey => msaKey;
        public string[] GlossTexts => glossTexts;

        public HashSet<string> GlossSet => _glossSet ??= new HashSet<string>(glossTexts, StringComparer.OrdinalIgnoreCase);

        public bool EveryGlossDotted => glossTexts.Length > 0 && glossTexts.All(text => text.Contains('.'));

        public bool EveryGlossFromForm => glossTexts.All(GlossFromForm);

        public bool GlossFromForm(string gloss)
        {
            _upperForms ??= formTexts.Select(text => text.ToUpperInvariant()).ToArray();
            foreach (var upper in _upperForms)
                if (gloss.Length > upper.Length + 1 && gloss[upper.Length] == '.' &&
                    gloss.StartsWith(upper, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
