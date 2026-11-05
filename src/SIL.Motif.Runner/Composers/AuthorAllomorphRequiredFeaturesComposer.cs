using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Authors an empty Required Features structure on an ordinary affix allomorph, preserving any occupied slot.
/// Populate the returned structure with AuthorFeatureValue; no restriction on the entry's MSA kind is imposed.
/// </summary>
public static class AuthorAllomorphRequiredFeaturesComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorAllomorphRequiredFeaturesIntent intent,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var form = ReferenceFieldLowering.Resolve<IMoAffixAllomorph>(cache, intent.Allomorph,
            "AuthorAllomorphRequiredFeatures");
        if (form.MsEnvFeaturesOA is not null)
            throw new InvalidOperationException("AuthorAllomorphRequiredFeatures: the allomorph already has Required Features; creation cannot replace them.");
        var mint = mintId ?? (() => CanonicalId.Mint());
        return [new OperationEnvelope(mint(), MoAffixAllomorphMsEnvFeaturesOperationKinds.CreateMsEnvFeatures,
            target: intent.Allomorph, entityId: mint(), after: JsonSerializer.SerializeToElement(new { }),
            rationale: "Authored by the AuthorAllomorphRequiredFeatures composer.")];
    }
}
