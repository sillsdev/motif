using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Resolves the identifiers in a parser reading — allomorph, grammatical info, inflection type — into the
/// form, gloss and category the project gives them, so a reading reads like an interlinear gloss.
/// </summary>
public static class ParserReadingReader
{
    /// <summary>
    /// Every reading in <paramref name="evidence"/>, in order; an identifier the project does not contain
    /// shows as a short marker in place of its text rather than being dropped.
    /// </summary>
    public static IReadOnlyList<ParserReading> Read(LcmCache cache, string projectName, ParseWordEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentNullException.ThrowIfNull(evidence);
        var objects = cache.ServiceLocator.ObjectRepository;
        return evidence.Analyses
            .Select(analysis => new ParserReading(ReadMorphs(cache, objects, projectName, analysis.Morphs)))
            .ToArray();
    }

    /// <summary>
    /// Resolves one ordered morph list on its own — the same resolution <see cref="Read"/> applies to every
    /// analysis of a batch result, reused wherever another reader (a project's own analyses, an approved
    /// reading the parser missed) needs the identical form/gloss/category rendering.
    /// </summary>
    public static IReadOnlyList<ParserReadingMorph> ReadMorphs(
        LcmCache cache, string projectName, IReadOnlyList<ParseMorph> morphs)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentNullException.ThrowIfNull(morphs);
        return ReadMorphs(cache, cache.ServiceLocator.ObjectRepository, projectName, morphs);
    }

    private static IReadOnlyList<ParserReadingMorph> ReadMorphs(
        LcmCache cache, ICmObjectRepository objects, string projectName, IReadOnlyList<ParseMorph> morphs) =>
        morphs.Select(morph => ReadMorph(cache, objects, projectName, morph)).ToArray();

    private static ParserReadingMorph ReadMorph(
        LcmCache cache, ICmObjectRepository objects, string projectName, ParseMorph morph)
    {
        var form = Find<IMoForm>(objects, morph.Form);
        var msa = Find<IMoMorphSynAnalysis>(objects, morph.Msa);
        var inflectionType = Find<ILexEntryInflType>(objects, morph.InflType);
        var entry = (ICmObject?)form?.Owner ?? msa?.Owner;

        var formText = morph.GuessedString
            ?? (form is null ? Missing(morph.Form) : Marked(form));
        var gloss = msa?.Owner is ILexEntry owner
            ? owner.AllSenses.FirstOrDefault(sense => sense.MorphoSyntaxAnalysisRA == msa)?.Gloss
                .BestAnalysisAlternative.Text ?? string.Empty
            : Missing(morph.Msa);
        var category = msa is null ? string.Empty : msa.InterlinearAbbr ?? string.Empty;
        return new ParserReadingMorph(
            formText,
            gloss == "***" ? string.Empty : gloss,
            category,
            inflectionType?.Abbreviation.BestAnalysisAlternative.Text,
            morph.GuessedString is not null,
            entry is null ? null : FieldWorksLinks.For(cache, projectName, entry));
    }

    private static T? Find<T>(ICmObjectRepository objects, string? id) where T : class, ICmObject =>
        Guid.TryParse(id, out var guid) && objects.TryGetObject(guid, out var found) ? found as T : null;

    private static string Marked(IMoForm form)
    {
        var text = form.Form.VernacularDefaultWritingSystem.Text ?? string.Empty;
        var type = form.MorphTypeRA;
        return type is null ? text : (type.Prefix ?? string.Empty) + text + (type.Postfix ?? string.Empty);
    }

    private static string Missing(string? id) => id is null ? string.Empty : $"(missing {id[..Math.Min(8, id.Length)]})";
}
