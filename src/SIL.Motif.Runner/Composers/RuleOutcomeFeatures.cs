using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

internal sealed class RuleOutcomeFeatures(LcmCache cache, SoundSystemAuthoring.Builder builder)
{
    private readonly Dictionary<Guid, CanonicalId> _wrappers =
        (cache.LangProject.PhonologicalDataOA.PhonRuleFeatsOA?.PossibilitiesOS ?? Enumerable.Empty<ICmPossibility>())
        .OfType<IPhPhonRuleFeat>().Where(w => w.ItemRA is not null).GroupBy(w => w.ItemRA.Guid)
        .ToDictionary(g => g.Key, g => SoundSystemAuthoring.Id(g.First()));
    private readonly Dictionary<CanonicalId, OperationEnvelope[]> _created = new();

    internal static IReadOnlyList<ICmObject> Items(LcmCache cache, IReadOnlyList<CanonicalId> ids, string field) =>
        EditPhonologicalRuleFeaturesComposer.Items(cache, ids, field)!;

    internal static void Validate(LcmCache cache, IReadOnlyList<CanonicalId>? required, IReadOnlyList<CanonicalId>? excluded)
    {
        var need = Items(cache, required ?? [], "requiredFeatures");
        var avoid = Items(cache, excluded ?? [], "excludedFeatures");
        if (need.Select(x => x.Guid).Intersect(avoid.Select(x => x.Guid)).Any())
            throw new InvalidOperationException("An outcome cannot have a feature both required and excluded.");
        if (need.Count + avoid.Count > 0 && cache.LangProject.PhonologicalDataOA.PhonRuleFeatsOA is null)
            throw new InvalidOperationException("The project has no Phonological Rule Features list. Open Grammar, Phonology, Rules in FieldWorks once, save, and reload the project.");
    }

    internal void Apply(CanonicalId rhsId, IReadOnlyList<ICmObject>? required, IReadOnlyList<ICmObject>? excluded,
        IPhSegRuleRHS? current = null)
    {
        ApplySet(required, current?.ReqRuleFeatsRC ?? Enumerable.Empty<IPhPhonRuleFeat>(),
            PhSegRuleRHSReqRuleFeatsOperationKinds.AddRefReqRuleFeats, PhSegRuleRHSReqRuleFeatsOperationKinds.RemoveRefReqRuleFeats);
        ApplySet(excluded, current?.ExclRuleFeatsRC ?? Enumerable.Empty<IPhPhonRuleFeat>(),
            PhSegRuleRHSExclRuleFeatsOperationKinds.AddRefExclRuleFeats, PhSegRuleRHSExclRuleFeatsOperationKinds.RemoveRefExclRuleFeats);

        void ApplySet(IReadOnlyList<ICmObject>? wanted, IEnumerable<IPhPhonRuleFeat> stored, string add, string remove)
        {
            if (wanted is null) return;
            var have = stored.ToArray();
            var wantedIds = wanted.Select(i => i.Guid).ToHashSet();
            foreach (var wrapper in have.Where(w => w.ItemRA is null || !wantedIds.Contains(w.ItemRA.Guid)).OrderBy(w => w.Guid))
                builder.Add(remove, rhsId, new { member = SoundSystemAuthoring.Id(wrapper).Value });
            var haveIds = have.Where(w => w.ItemRA is not null).Select(w => w.ItemRA.Guid).ToHashSet();
            foreach (var item in wanted.Where(i => !haveIds.Contains(i.Guid)))
            {
                var wrapper = Wrapper(item);
                builder.Add(add, rhsId, new { member = wrapper.Value }, requires: _created.GetValueOrDefault(wrapper));
            }
        }
    }

    private CanonicalId Wrapper(ICmObject item)
    {
        if (_wrappers.TryGetValue(item.Guid, out var id)) return id;
        var list = cache.LangProject.PhonologicalDataOA.PhonRuleFeatsOA ??
            throw new InvalidOperationException("The project has no Phonological Rule Features list.");
        var start = builder.Operations.Count;
        var wrapper = builder.Create(CmPossibilityListPossibilitiesOperationKinds.Create, SoundSystemAuthoring.Id(list),
            new { @class = "PhPhonRuleFeat" });
        var label = ((item as ICmPossibility)?.Name.get_String(cache.DefaultAnalWs)?.Text ?? item.ShortName)
            .Normalize(System.Text.NormalizationForm.FormD);
        builder.Text(CmPossibilityNameOperationKinds.SetName, wrapper, cache, cache.DefaultAnalWs, label);
        builder.Text(CmPossibilityAbbreviationOperationKinds.SetAbbreviation, wrapper, cache, cache.DefaultAnalWs, label);
        builder.Add(PhPhonRuleFeatItemOperationKinds.SetItem, wrapper, new { @ref = SoundSystemAuthoring.Id(item).Value });
        _wrappers[item.Guid] = wrapper;
        _created[wrapper] = builder.Operations.Skip(start).ToArray();
        return wrapper;
    }
}
