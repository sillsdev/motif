using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Takes one exception feature off every stem that carries it and out of every phonological rule branch that requires
/// or excludes it, in one Proposal. FieldWorks path: Grammar, Lexicon, each entry's Exception Features, and Grammar,
/// Phonology, Rules, each rule's Required and Excluded Features (<c>MoStemMsa.ProdRestrict</c>,
/// <c>PhSegRuleRHS.ReqRuleFeats</c> and <c>ExclRuleFeats</c>).
/// </summary>
/// <param name="Feature">Canonical id of the exception feature.</param>
/// <param name="ExpectedName">The name the feature must still carry, in any writing system.</param>
public sealed record RemoveExceptionFeatureIntent(CanonicalId Feature, string ExpectedName);

/// <summary>Parses the closed JSON intent for removing an exception feature.</summary>
public static class RemoveExceptionFeatureIntentParser
{
    /// <summary>Reads <c>{ "feature", "expectedName" }</c>.</summary>
    public static RemoveExceptionFeatureIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "RemoveExceptionFeature", "feature", "expectedName");
        return new(SoundSystemIntentParsing.Id(authored, "feature"), SoundSystemIntentParsing.Text(authored, "expectedName"));
    }
}

/// <summary>
/// Finds every reference in one pass, O(analyses + rules), with no reference index (LibLCM builds that lazily at a cost of seconds): the lexicon's analyses, then the phonological rules' right
/// hand sides, whose required and excluded sets hold the Phonological Rule Features wrappers of the feature. It parses
/// nothing and builds nothing per stem. Affix restrictions (From and To Exception Features) are not edited: they only
/// ever require a feature, so removing it would loosen an affix, and they stay for a person to decide.
/// </summary>
public static class RemoveExceptionFeatureComposer
{
    private const string Construct = "RemoveExceptionFeature";
    private const string Rationale = "Authored by the RemoveExceptionFeature composer.";

    /// <summary>The kind a summary carries in the related objects of a composed intent.</summary>
    public const string SummaryKind = "remove-exception-feature-summary";

    /// <summary>Everything that carries the feature now.</summary>
    /// <param name="Feature">The resolved feature.</param>
    /// <param name="Stems">Stem analyses that carry it, ordered by identity.</param>
    /// <param name="Excluding">Rule branches that exclude it, with the wrappers that hold it.</param>
    /// <param name="Requiring">Rule branches that require it, with the wrappers that hold it.</param>
    /// <param name="OtherReferrers">Affix analyses whose From or To Exception Features still hold it afterwards.</param>
    public sealed record Plan(ICmPossibility Feature, IReadOnlyList<IMoStemMsa> Stems,
        IReadOnlyList<(IPhSegRuleRHS Branch, IPhPhonRuleFeat[] Wrappers)> Excluding,
        IReadOnlyList<(IPhSegRuleRHS Branch, IPhPhonRuleFeat[] Wrappers)> Requiring, int OtherReferrers);

    /// <summary>Resolves the feature and collects its stems and rule branches.</summary>
    public static Plan Evaluate(LcmCache cache, RemoveExceptionFeatureIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var id = LexicalFormAuthoring.Normalize(intent.Feature);
        ExceptionFeatureTargets.RequireProjectFeatures(cache, [id], Construct);
        var feature = ReferenceFieldLowering.Resolve<ICmPossibility>(cache, id, Construct);
        var names = feature.Name.AvailableWritingSystemIds.Select(ws => feature.Name.get_String(ws).Text)
            .Where(text => !string.IsNullOrEmpty(text)).ToArray();
        if (!names.Contains(intent.ExpectedName, StringComparer.Ordinal))
            throw new InvalidOperationException($"'{Construct}': '{id.Value}' has the name " +
                $"{(names.Length == 0 ? "(empty)" : string.Join(" / ", names.Select(n => $"'{n}'")))}, not '{intent.ExpectedName}'.");

        var held = feature.Guid;
        var stems = new List<IMoStemMsa>();
        var affixUsers = 0;
        foreach (var entry in cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances())
        foreach (var msa in entry.MorphoSyntaxAnalysesOC)
            switch (msa)
            {
                case IMoStemMsa stem when stem.ProdRestrictRC.Any(item => item.Guid == held): stems.Add(stem); break;
                case IMoInflAffMsa inflectional when inflectional.FromProdRestrictRC.Any(item => item.Guid == held): affixUsers++; break;
                case IMoDerivAffMsa derivational when derivational.FromProdRestrictRC.Any(item => item.Guid == held) ||
                    derivational.ToProdRestrictRC.Any(item => item.Guid == held): affixUsers++; break;
            }

        var wrappers = (cache.LangProject.PhonologicalDataOA.PhonRuleFeatsOA?.PossibilitiesOS ?? Enumerable.Empty<ICmPossibility>())
            .OfType<IPhPhonRuleFeat>().Where(w => w.ItemRA?.Guid == held).Select(w => w.Guid).ToHashSet();
        var excluding = new List<(IPhSegRuleRHS, IPhPhonRuleFeat[])>();
        var requiring = new List<(IPhSegRuleRHS, IPhPhonRuleFeat[])>();
        if (wrappers.Count > 0)
            foreach (var rule in cache.LangProject.PhonologicalDataOA.PhonRulesOS.OfType<IPhRegularRule>())
            foreach (var rhs in rule.RightHandSidesOS)
            {
                var excl = rhs.ExclRuleFeatsRC.Where(w => wrappers.Contains(w.Guid)).ToArray();
                if (excl.Length > 0) excluding.Add((rhs, excl));
                var req = rhs.ReqRuleFeatsRC.Where(w => wrappers.Contains(w.Guid)).ToArray();
                if (req.Length > 0) requiring.Add((rhs, req));
            }

        return new Plan(feature, stems.OrderBy(msa => msa.Guid).ToArray(), excluding, requiring, affixUsers);
    }

    /// <summary>Builds one removal per stem and per rule branch wrapper; a feature nothing carries builds nothing.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, RemoveExceptionFeatureIntent intent)
    {
        var plan = Evaluate(cache, intent);
        var feature = CanonicalId.FromGuid(plan.Feature.Guid);
        var operations = new List<OperationEnvelope>();
        OperationEnvelope Remove(string kind, ICmObject target, CanonicalId member) => new(CanonicalId.Mint(), kind,
            target: CanonicalId.FromGuid(target.Guid), after: JsonSerializer.SerializeToElement(new { member = member.Value }),
            rationale: Rationale);
        operations.AddRange(plan.Stems.Select(msa => Remove(MoStemMsaProdRestrictOperationKinds.RemoveRefProdRestrict, msa, feature)));
        foreach (var (branch, wrappers) in plan.Excluding.OrderBy(item => item.Branch.Guid))
            operations.AddRange(wrappers.OrderBy(w => w.Guid).Select(w => Remove(
                PhSegRuleRHSExclRuleFeatsOperationKinds.RemoveRefExclRuleFeats, branch, CanonicalId.FromGuid(w.Guid))));
        foreach (var (branch, wrappers) in plan.Requiring.OrderBy(item => item.Branch.Guid))
            operations.AddRange(wrappers.OrderBy(w => w.Guid).Select(w => Remove(
                PhSegRuleRHSReqRuleFeatsOperationKinds.RemoveRefReqRuleFeats, branch, CanonicalId.FromGuid(w.Guid))));
        return operations;
    }

    /// <summary>One summary: the counts first, then what stays.</summary>
    public static IReadOnlyList<ComposedRelatedObject> Summarize(LcmCache cache, RemoveExceptionFeatureIntent intent)
    {
        var plan = Evaluate(cache, intent);
        var branches = plan.Excluding.Count + plan.Requiring.Count;
        var text = $"{plan.Stems.Count} stems and {branches} rule branches carry this feature " +
                   $"({plan.Excluding.Count} excluded, {plan.Requiring.Count} required); all are removed. " +
                   $"{plan.OtherReferrers} other objects (affix restrictions) still refer to it and are not edited. " +
                   "The feature item itself stays in the Exception Features list, and so does its Phonological Rule Features " +
                   "wrapper, because Motif has no operation that deletes the wrapper; delete them in FieldWorks if unwanted.";
        return [new ComposedRelatedObject(SummaryKind, CanonicalId.FromGuid(plan.Feature.Guid).Value, text)];
    }
}
