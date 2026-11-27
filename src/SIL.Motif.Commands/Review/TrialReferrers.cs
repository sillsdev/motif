using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Commands.Review;

/// <summary>An edited grammar object whose referring entries supply sibling forms.</summary>
/// <param name="Id">The object's id.</param>
/// <param name="Target">The id as the Proposal wrote it.</param>
internal sealed record ReferrerSeed(Guid Id, string Target);

/// <summary>The forms of the entries that use the edited grammar objects, and how many entries each object has.</summary>
/// <param name="Forms">Every form of every referring entry, as written.</param>
/// <param name="Referrers">One row per edited object an entry uses; objects of other classes or without a user are absent.</param>
internal sealed record ReferrerForms(IReadOnlyList<string> Forms, IReadOnlyList<TrialReferrer> Referrers);

/// <summary>
/// Tier 1 of the sibling sample: a Proposal that edits an inflection class, stem name, affix slot, phonological environment or
/// affix template changes the words of the entries that use it, whatever form text it writes. Their forms join the sample.
/// </summary>
internal static class TrialReferrers
{
    // Only a seeding create edits what entries use.
    private static readonly string[] GrammarKinds =
        ["phPhonRuleFeat", "phSegRuleRHS", "moInflAffixTemplate", "moMorphData", "moInflClass", "moInflAffixSlot", "phEnvironment", "moStemName"];

    // A create on a class that the entries that use its parent also use.
    private static readonly string[] SeedingCreates = ["createsubclasses", "createregions"];

    private static readonly string[] ReferenceKeys = ["member", "ref", "first"];

    /// <summary>
    /// The existing grammar objects the Proposal edits, and those its grammar operations point at (<c>member</c>, <c>ref</c>,
    /// <c>first</c>, <c>rest</c>); a rename does not edit what entries use. Objects the Proposal creates are not seeds.
    /// </summary>
    public static IReadOnlyList<ReferrerSeed> Seeds(Proposal proposal)
    {
        var created = proposal.Operations.Where(operation => operation.EntityId is not null)
            .Select(operation => operation.EntityId!.Value.ToGuid()).ToHashSet();
        var seeds = new Dictionary<Guid, ReferrerSeed>();
        void Add(string? text)
        {
            if (text is null || !CanonicalId.TryParse(text, out var id) || created.Contains(id.ToGuid())) return;
            seeds.TryAdd(id.ToGuid(), new ReferrerSeed(id.ToGuid(), text));
        }
        foreach (var operation in proposal.Operations)
        {
            var parts = operation.Kind.Split('/');
            var last = parts[^1].ToLowerInvariant();
            var inGrammar = parts[0] == "grammar" && parts.Length > 1 && GrammarKinds.Contains(parts[1]);
            if (!inGrammar) continue;
            var creating = last.StartsWith("create", StringComparison.Ordinal);
            var seeded = (!creating || SeedingCreates.Contains(last)) && !last.StartsWith("setname", StringComparison.Ordinal) &&
                !last.StartsWith("setabbreviation", StringComparison.Ordinal);
            if (seeded && operation.Target is { } target) Add(target.Value);
            if (operation.After is not { ValueKind: JsonValueKind.Object } after) continue;
            foreach (var key in ReferenceKeys)
                if (after.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String) Add(value.GetString());
            if (after.TryGetProperty("rest", out var rest) && rest.ValueKind == JsonValueKind.Array)
                foreach (var item in rest.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String) Add(item.GetString());
        }
        return [.. seeds.Values];
    }

    /// <summary>The forms of the entries that use each seed that the Baseline holds as a referrer class.</summary>
    public static ReferrerForms Find(LcmCache cache, IEnumerable<ReferrerSeed> seeds)
    {
        var forms = new List<string>();
        var rows = new List<TrialReferrer>();
        var seen = new HashSet<Guid>();
        foreach (var seed in seeds)
        {
            if (!cache.ServiceLocator.ObjectRepository.TryGetObject(seed.Id, out var found)) continue;
            var members = Members(found);
            if (members is null) continue;
            var entries = new Dictionary<Guid, ILexEntry>();
            foreach (var member in members)
                foreach (var referrer in member.ReferringObjects)
                    if (EntryOf(referrer) is { } entry) entries[entry.Guid] = entry;
            if (entries.Count == 0) continue;
            rows.Add(new TrialReferrer(seed.Target, found.ClassName, entries.Count));
            foreach (var entry in entries.Values.OrderBy(entry => entry.Guid))
                if (seen.Add(entry.Guid)) forms.AddRange(TrialSiblingResolver.EntryForms(entry));
        }
        return new ReferrerForms(forms, rows);
    }

    // The object and what entries may use instead of it; null for any other class.
    private static List<ICmObject>? Members(ICmObject found)
    {
        var members = new List<ICmObject>();
        switch (found)
        {
            case IMoInflClass inflection:
                var pending = new Stack<IMoInflClass>([inflection]);
                while (pending.Count > 0)
                {
                    var next = pending.Pop();
                    members.Add(next);
                    foreach (var sub in next.SubclassesOC) pending.Push(sub);
                }
                return members;
            case IMoInflAffixTemplate template:
                members.Add(template);
                members.AddRange(template.PrefixSlotsRS.Concat(template.SuffixSlotsRS).Concat(template.SlotsRS));
                return members;
            case IMoStemName or IMoInflAffixSlot or IPhEnvironment:
                return [found];
            default:
                return null;
        }
    }

    private static ILexEntry? EntryOf(ICmObject start)
    {
        var cursor = start;
        for (var depth = 0; cursor is not null && depth < 12; depth++, cursor = cursor.Owner)
            if (cursor is ILexEntry entry) return entry;
        return null;
    }
}
