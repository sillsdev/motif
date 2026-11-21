using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Points an existing sense at another analysis of the same entry.</summary>
/// <param name="Sense">The sense, top level or a sub-sense, whose analysis changes.</param>
/// <param name="Analysis">An analysis the sense's entry already has.</param>
public sealed record SetSenseMsaIntent(CanonicalId Sense, CanonicalId Analysis);

/// <summary>Parses the closed JSON intent for pointing a sense at another analysis.</summary>
public static class SetSenseMsaIntentParser
{
    private const string ConstructName = "SetSenseMsa";

    /// <summary>Reads the sense and the analysis it should use.</summary>
    public static SetSenseMsaIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "sense", "analysis");
        return new SetSenseMsaIntent(
            SoundSystemIntentParsing.Id(authored, "sense"), SoundSystemIntentParsing.Id(authored, "analysis"));
    }
}

/// <summary>
/// Lowers a <see cref="SetSenseMsaIntent"/> into one re-pointing of the sense's analysis. The analysis must belong to
/// the sense's own entry and suit its lexeme form (a stem analysis for a stem, an affix analysis for an affix). When
/// no sense would use the old analysis afterwards, LibLCM removes it, and the refusal or note says so.
/// </summary>
public static class SetSenseMsaComposer
{
    private const string ConstructName = "SetSenseMsa";
    private const string Rationale = "Authored by the SetSenseMsa composer: the sense belongs under this analysis.";

    /// <summary>Builds the operation without mutating the caller's project.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored re-pointing.</param>
    /// <param name="mintId">Overrides id minting; the Draft supplies one so ids stay unique within it.</param>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SetSenseMsaIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var senseId = LexicalFormAuthoring.Normalize(intent.Sense);
        var msaId = LexicalFormAuthoring.Normalize(intent.Analysis);
        var sense = LexicalFormAuthoring.Resolve<ICmObject>(cache, senseId, ConstructName) as ILexSense
            ?? throw new InvalidOperationException($"'{ConstructName}': 'sense' must name a sense.");
        var entry = sense.Entry;
        var analyses = entry.MorphoSyntaxAnalysesOC.ToArray();
        var entryId = CanonicalId.FromGuid(entry.Guid).Value;
        var target = analyses.FirstOrDefault(item => item.Guid == msaId.ToGuid());
        if (target is null)
            throw new InvalidOperationException(
                $"'{ConstructName}': '{msaId.Value}' is not an analysis of the sense's entry '{entryId}'. " +
                $"Its analyses are {AddSenseComposer.Listing(analyses)}. For an analysis the entry lacks, use " +
                $"compose-add-morphosyntax-analysis with {{\"entry\":\"{entryId}\",\"msa\":{{...}},\"senses\":[\"{senseId.Value}\"]}}.");
        if (sense.MorphoSyntaxAnalysisRA?.Guid == target.Guid)
            throw new InvalidOperationException(
                $"'{ConstructName}': sense '{senseId.Value}' already uses analysis '{msaId.Value}'.");
        var isStemForm = entry.LexemeFormOA switch
        {
            IMoStemAllomorph => true,
            IMoAffixAllomorph or IMoAffixProcess => false,
            _ => throw new InvalidOperationException(
                $"'{ConstructName}': entry '{entryId}' has no lexeme form, so it is neither a stem nor an affix."),
        };
        if (isStemForm != (target is IMoStemMsa))
            throw new InvalidOperationException(
                $"'{ConstructName}': entry '{entryId}' has " + (isStemForm
                    ? "a stem form, so its senses use a stem analysis; analysis '" + msaId.Value + "' is not one."
                    : "an affix form, so its senses use an affix analysis; analysis '" + msaId.Value + "' is a stem analysis."));
        var notes = AddMorphoSyntaxAnalysisComposer.RequireNoRemainingReferences(entry, [senseId]);
        var source = mintId ?? (() => CanonicalId.Mint());
        return [new OperationEnvelope(LexicalFormAuthoring.Normalize(source()),
            LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis, target: senseId,
            after: JsonSerializer.SerializeToElement(new { @ref = msaId.Value }),
            rationale: notes.TryGetValue(sense.Guid, out var note) ? note : Rationale)];
    }
}
