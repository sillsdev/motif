using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names the existing form and its requested morph type.</summary>
public sealed record EditMorphTypeIntent(CanonicalId Target, CanonicalId MorphType);

/// <summary>Reads a closed in-place morph-type edit.</summary>
public static class EditMorphTypeIntentParser
{
    public static EditMorphTypeIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "EditMorphType", "target", "morphType");
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, "target")),
            LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, "morphType")));
    }
}

/// <summary>Stages a stem-family type change without replacing the form.</summary>
public static class EditMorphTypeComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditMorphTypeIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var form = LexicalFormAuthoring.Resolve<IMoForm>(cache, intent.Target, "EditMorphType");
        var type = LexicalFormAuthoring.Resolve<IMoMorphType>(cache, intent.MorphType, "EditMorphType");
        MorphTypeEditSafety.Check(form, type);
        if (form.MorphTypeRA.Guid == type.Guid) return [];
        return [new(CanonicalId.Mint(), EditMorphTypeOperationKinds.Set, target: intent.Target,
            after: JsonSerializer.SerializeToElement(new { morphType = intent.MorphType.Value,
                expectedMorphType = CanonicalId.FromGuid(form.MorphTypeRA.Guid).Value }),
            rationale: "Change the existing form's morph type within the stem family.")];
    }
}
