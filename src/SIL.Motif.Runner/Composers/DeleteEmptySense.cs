using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Deletes one wholly empty sense of an entry, and the analysis only that sense uses.</summary>
/// <param name="Target">The sense to delete.</param>
/// <param name="Entry">The entry that owns the sense, named so a wrong sense id is refused rather than deleted.</param>
public sealed record DeleteEmptySenseIntent(CanonicalId Target, CanonicalId Entry);

/// <summary>Parses a closed sense deletion with both identities required.</summary>
public static class DeleteEmptySenseIntentParser
{
    public static DeleteEmptySenseIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "DeleteEmptySense", "target", "entry");
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, "target")),
            LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, "entry")));
    }
}

/// <summary>
/// Lowers a sense deletion to one operation. There is no flag to delete a sense that has content: a sense with a
/// gloss, definition, example, note or any other populated field is refused, because deleting it loses a person's work.
/// </summary>
public static class DeleteEmptySenseComposer
{
    /// <summary>The rationale each operation carries; a Draft still needs its own Rationale to finalize.</summary>
    public const string OperationRationale =
        "Authored by the DeleteEmptySense composer: the sense is empty and its analysis is safe to remove.";

    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, DeleteEmptySenseIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        const string construct = "DeleteEmptySense";
        var sense = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Target, construct) as ILexSense
            ?? throw new InvalidOperationException("The target must be a sense.");
        if (sense.Owner is not ILexEntry entry)
            throw new InvalidOperationException("Only a top-level sense of an entry can be deleted this way.");
        if (entry.Guid != intent.Entry.ToGuid())
            throw new InvalidOperationException("The sense belongs to a different entry than the one named.");
        if (LexSenseEmptiness.FirstPopulatedField(cache, sense) is { } field)
            throw new InvalidOperationException(
                $"The sense is not empty ('{field}' has content); only a wholly empty sense can be deleted.");
        if (sense.ReferringObjects.Any())
            throw new InvalidOperationException("Other objects still refer to the sense, so it cannot be deleted.");
        var msa = sense.MorphoSyntaxAnalysisRA;
        CanonicalId? deletedMsa = null;
        if (msa is not null && !entry.SensesOS.Any(other => other.Guid != sense.Guid &&
                                                              other.MorphoSyntaxAnalysisRA?.Guid == msa.Guid))
        {
            if (msa.Owner?.Guid != entry.Guid)
                throw new InvalidOperationException("The sense's analysis is not owned by its entry.");
            VariantSenseAnalysisSafety.Check(cache, entry, msa);
            if (msa.ReferringObjects.Any(other => other.Guid != sense.Guid))
                throw new InvalidOperationException(
                    "Other objects, such as parser analyses, still refer to the sense's analysis, so it cannot be deleted with the sense.");
            deletedMsa = CanonicalId.FromGuid(msa.Guid);
        }
        var variant = deletedMsa is not null && VariantSenseAnalysisSafety.PartOfSpeech(msa) is not null
            ? entry.EntryRefsOS.First(reference => reference.RefType == LexEntryRefTags.krtVariant &&
                                                   reference.ComponentLexemesRS.Count > 0) : null;
        var after = JsonSerializer.SerializeToElement(new
        {
            msa = deletedMsa?.Value,
            variant = variant is null ? null : CanonicalId.FromGuid(variant.Guid).Value,
        });
        return [new(CanonicalId.Mint(), LexSenseDeleteOperationKinds.DeleteSense,
            target: LexicalFormAuthoring.Normalize(intent.Target), after: after, rationale: OperationRationale)];
    }
}
