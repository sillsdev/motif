using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Removes the one specification a feature has on a stem's MsFeatures or an inflectional affix's InflFeats, so the
/// word no longer carries that feature or the affix no longer requires it. It undoes
/// <see cref="AuthorFeatureValueComposer"/>; the structure itself stays, even when it ends up empty.
/// </summary>
public static class RemoveFeatureValueComposer
{
    private const string ConstructName = "RemoveFeatureValue";

    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored construct.</param>
    /// <param name="mintId">Overrides id minting; tests supply a deterministic source.</param>
    /// <exception cref="InvalidOperationException">
    /// <c>feature-value-not-found</c> when the structure has no specification for the feature, and
    /// <c>feature-structure-not-grammatical-info</c> when the structure belongs to neither a stem nor an inflectional affix.
    /// </exception>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, RemoveFeatureValueIntent intent, Func<CanonicalId>? mintId = null)
    {
        if (cache is null) throw new ArgumentNullException(nameof(cache));
        if (intent is null) throw new ArgumentNullException(nameof(intent));

        var mint = mintId ?? (() => CanonicalId.Mint());
        var structure = ReferenceFieldLowering.Resolve<IFsFeatStruc>(cache, intent.FeatStruc, ConstructName);
        ReferenceFieldLowering.Resolve<IFsFeatDefn>(cache, intent.Feature, ConstructName);
        if (structure.Owner is not (IMoStemMsa or IMoInflAffMsa or IMoDerivAffMsa))
            throw new InvalidOperationException(
                $"{ConstructName}: feature-structure-not-grammatical-info: '{intent.FeatStruc.Value}' is not the " +
                "MsFeatures of a stem, the InflFeats of an inflectional affix, or the FromMsFeatures or ToMsFeatures of a derivational affix.");

        var specification = structure.FeatureSpecsOC.SingleOrDefault(spec => spec.FeatureRA?.Guid == intent.Feature.ToGuid());
        if (specification is null)
            throw new InvalidOperationException(
                $"{ConstructName}: feature-value-not-found: feature structure '{intent.FeatStruc.Value}' has no " +
                $"specification for feature '{intent.Feature.Value}'.");

        using var empty = JsonDocument.Parse("{}");
        return new[]
        {
            new OperationEnvelope(
                operationId: mint(),
                kind: FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs,
                target: CanonicalId.FromGuid(specification.Guid),
                after: empty.RootElement.Clone(),
                rationale: "Authored by the RemoveFeatureValue composer."),
        };
    }
}
