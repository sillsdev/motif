using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Moves one written representation of a phoneme out into a new phoneme of its own, which undoes a merge. The new
/// phoneme starts with no natural class and no features, so a person adds those with the composers that own them.
/// Each refusal begins with a stable name.
/// </summary>
public static class SplitPhonemeComposer
{
    /// <summary>Refusal name: the phoneme has only this representation, so nothing would stay behind.</summary>
    public const string LastRepresentation = "phoneme-split-last-representation";

    /// <summary>Refusal name: the new phoneme has no name.</summary>
    public const string NameMissing = "phoneme-split-name-missing";

    /// <summary>Composes the removal of the representation and the new phoneme that carries it.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SplitPhonemeIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var source = EditPhonemeComposer.Resolve(cache, intent.Source);
        var spelling = intent.Representation.Normalize(NormalizationForm.FormD);
        var code = source.CodesOS.FirstOrDefault(item => EditPhonemeComposer.Spelling(cache, item) == spelling) ??
            throw new InvalidOperationException(
                $"{EditPhonemeComposer.NotFound}: the phoneme has no representation '{intent.Representation}'.");
        if (source.CodesOS.Count < 2)
            throw new InvalidOperationException(
                $"{LastRepresentation}: '{intent.Representation}' is the only way this phoneme is written, so nothing would stay behind.");
        if (string.IsNullOrWhiteSpace(intent.Name))
            throw new InvalidOperationException($"{NameMissing}: name the new phoneme.");
        if (EditPhonemeComposer.ReadEnvironmentsWriting(cache, spelling).FirstOrDefault() is { } environment)
            throw new InvalidOperationException(
                $"{EditPhonemeComposer.Referenced}: environment '{environment.Name}' writes '{intent.Representation}'; " +
                "change that environment first.");

        var ws = cache.DefaultVernWs;
        var builder = new SoundSystemAuthoring.Builder(mintId, "Authored by the SplitPhoneme composer.");
        var removal = builder.Add(PhonemeDeleteOperationKinds.DeleteCodes, SoundSystemAuthoring.Id(code), new { });
        var phoneme = builder.Create(PhPhonemeSetPhonemesOperationKinds.Create, SoundSystemAuthoring.Id(SoundSystemAuthoring.FirstSet(cache)),
            placement: null, requires: [removal]);
        builder.Text(PhTerminalUnitNameOperationKinds.SetName, phoneme, cache, ws, intent.Name);
        var created = builder.Create(PhTerminalUnitCodesOperationKinds.Create, phoneme);
        builder.Text(PhCodeRepresentationOperationKinds.SetRepresentation, created, cache, ws, SoundSystemAuthoring.RequireLiteral(spelling));
        return builder.Operations;
    }

    /// <summary>Returns the environments that write the representation being moved.</summary>
    public static IReadOnlyList<ComposedRelatedObject> ReadAffectedUsers(LcmCache cache, SplitPhonemeIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        _ = EditPhonemeComposer.Resolve(cache, intent.Source);
        return EditPhonemeComposer.ReadEnvironmentsWriting(cache, intent.Representation.Normalize(NormalizationForm.FormD))
            .OrderBy(user => user.Id, StringComparer.Ordinal).ToArray();
    }
}
