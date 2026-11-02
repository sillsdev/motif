using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;
using SIL.LCModel;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Authors one empty feature structure on an existing stem or affix MSA. Stems use
/// MsFeatures; inflectional affixes use InflFeats for the inflection features required on the word; derivational
/// affixes use FromMsFeatures (required of the stem) or ToMsFeatures (carried by the derived stem, replacing the
/// stem's value of any feature it names).
/// An occupied structure is preserved: this intent creates one and never replaces one.
/// </summary>
public static class AuthorFeatureStructureComposer
{
    private const string ConstructName = "AuthorFeatureStructure";

    /// <param name="cache">The project to resolve <paramref name="intent"/>'s reference against.</param>
    /// <param name="intent">The authored construct.</param>
    /// <param name="mintId">
    /// Overrides id minting; defaults to <see cref="CanonicalId.Mint(string)"/>. Tests supply a
    /// deterministic source to pin the composer's lowering as reproducible.
    /// </param>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, AuthorFeatureStructureIntent intent, Func<CanonicalId>? mintId = null)
    {
        if (cache is null) throw new ArgumentNullException(nameof(cache));
        if (intent is null) throw new ArgumentNullException(nameof(intent));

        var mint = mintId ?? (() => CanonicalId.Mint());

        var msa = ReferenceFieldLowering.Resolve<IMoMorphSynAnalysis>(cache, intent.Msa, ConstructName);
        if (intent.Side is not null && msa is not IMoDerivAffMsa)
            throw new InvalidOperationException(
                $"'{ConstructName}': 'side' applies only to a derivational affix MSA; '{intent.Msa.Value}' is not one.");
        if (msa is IMoDerivAffMsa && intent.Side is null)
            throw new InvalidOperationException(
                $"'{ConstructName}': a derivational affix MSA needs 'side': \"from\" for the requirement on the stem, " +
                "\"to\" for the features the derived stem carries.");
        var (kind, structure) = msa switch
        {
            IMoStemMsa stem => (MoStemMsaMsFeaturesOperationKinds.CreateMsFeatures, stem.MsFeaturesOA),
            IMoInflAffMsa inflectional => (MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats, inflectional.InflFeatsOA),
            IMoDerivAffMsa derivational when intent.Side == "to" =>
                (MoDerivAffMsaMsFeaturesOperationKinds.CreateToMsFeatures, derivational.ToMsFeaturesOA),
            IMoDerivAffMsa derivational =>
                (MoDerivAffMsaMsFeaturesOperationKinds.CreateFromMsFeatures, derivational.FromMsFeaturesOA),
            _ => throw new InvalidOperationException(
                $"'{ConstructName}': MSA '{intent.Msa.Value}' is not a MoStemMsa, MoInflAffMsa or MoDerivAffMsa."),
        };
        if (structure is not null)
            throw new InvalidOperationException(
                $"'{ConstructName}': MSA '{intent.Msa.Value}' already has a feature structure; " +
                "this construct authors one, it does not replace one.");

        var operationId = mint();
        var newFeatStrucId = mint();

        return new[]
        {
            new OperationEnvelope(
                operationId: operationId,
                kind: kind,
                entityId: newFeatStrucId,
                target: intent.Msa,
                after: EmptyAfter(),
                rationale: Rationale),
        };
    }

    private const string Rationale = "Authored by the AuthorFeatureStructure composer.";

    private static JsonElement EmptyAfter()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
