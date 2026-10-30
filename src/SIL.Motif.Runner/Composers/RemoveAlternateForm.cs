using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Removes one alternate form (allomorph) of an entry that nothing refers to.</summary>
/// <param name="Entry">The entry that owns the alternate form.</param>
/// <param name="Form">The alternate form to remove.</param>
public sealed record RemoveAlternateFormIntent(CanonicalId Entry, CanonicalId Form);

/// <summary>Parses the closed JSON intent for removing an alternate form.</summary>
public static class RemoveAlternateFormIntentParser
{
    /// <summary>Reads the entry and the form; both are required.</summary>
    public static RemoveAlternateFormIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "RemoveAlternateForm", "entry", "form");
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "entry")),
            LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "form")));
    }
}

/// <summary>
/// Lowers an alternate-form removal to one <see cref="AlternateFormRemoveOperationKinds.Remove"/>. The lexeme form
/// cannot be removed this way, and a form that a morph bundle, an ad hoc prohibition or any other object refers to
/// is refused naming the referrer.
/// </summary>
public static class RemoveAlternateFormComposer
{
    private const string ConstructName = "RemoveAlternateForm";
    private const string Rationale = "Authored by the RemoveAlternateForm composer: the alternate form is redundant and unused.";

    /// <summary>Composes the removal without mutating the caller's project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, RemoveAlternateFormIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var entry = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Entry, ConstructName) as ILexEntry
            ?? throw new InvalidOperationException($"'{ConstructName}': the entry id must name an entry.");
        AlternateFormRemoval.Inspect(entry, intent.Form.ToGuid());
        var after = JsonSerializer.SerializeToElement(new { form = LexicalFormAuthoring.Normalize(intent.Form).Value });
        return [new(CanonicalId.Mint(), AlternateFormRemoveOperationKinds.Remove,
            target: LexicalFormAuthoring.Normalize(intent.Entry), after: after, rationale: Rationale)];
    }
}
