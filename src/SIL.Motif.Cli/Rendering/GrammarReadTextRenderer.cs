using System;
using System.Linq;
using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

internal static class GrammarReadTextRenderer
{
    private const int TextListLimit = 10;
    private const string NullAffixForm = "∅";

    public static string Render(GrammarReadResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Grammar objects: {response.Kind} ({response.Returned} of {response.Total}; offset {response.Offset}, limit {response.Limit})");
        text.AppendLine(response.Baseline is { } baseline
            ? $"Baseline: {baseline.ProjectIdentity}; {(response.IsStale ? "saved since capture" : "current")}; " +
                $"snapshot {baseline.SemanticSnapshotDigest}; bundle {baseline.BundleDigest}; captured {baseline.CapturedUtc}."
            : $"Baseline identity unavailable; {(response.IsStale ? "saved since capture" : "current")}.");
        foreach (var item in response.Items.Take(TextListLimit)) RenderItem(text, item);
        AppendOverflow(text, response.Items.Count);
        if (response.Items.Count == 0) text.AppendLine("No matching objects.");
        var shown = Math.Min(response.Items.Count, TextListLimit);
        if (response.Offset + shown < response.Total)
            text.AppendLine($"Next page: --offset {response.Offset + shown} --limit {response.Limit}");
        return text.ToString();
    }

    private static void RenderItem(StringBuilder text, GrammarReadItem item)
    {
        switch (item)
        {
            case GrammarPhonemeRead phoneme:
                text.AppendLine($"{Display(phoneme.Name)}  {phoneme.Id}");
                foreach (var representation in phoneme.Representations.Take(TextListLimit))
                    text.AppendLine($"  Representation: {Display(representation.Text)} ({Display(representation.WritingSystem)})");
                AppendOverflow(text, phoneme.Representations.Count);
                break;
            case GrammarNaturalClassRead naturalClass:
                text.AppendLine($"{DisplayWithAbbreviation(naturalClass.Name, naturalClass.Abbreviation)}  " +
                    $"{naturalClass.Id}  {naturalClass.ClassKind}");
                foreach (var member in naturalClass.Members.Take(TextListLimit))
                    text.AppendLine($"  Member: {Display(member.Name)}  {member.Id}");
                AppendOverflow(text, naturalClass.Members.Count);
                foreach (var feature in naturalClass.Features.Take(TextListLimit))
                    text.AppendLine($"  Feature: {Display(feature.Feature.Name)}  {feature.Feature.Id}" +
                        (feature.Value is null ? string.Empty : $" = {Display(feature.Value.Name)}  {feature.Value.Id}"));
                AppendOverflow(text, naturalClass.Features.Count);
                break;
            case GrammarEnvironmentRead environment:
                text.AppendLine($"{Display(environment.Name)}  {environment.Id}");
                text.AppendLine($"  Notation: {Display(environment.Notation)}");
                break;
            case GrammarPartOfSpeechRead category:
                text.AppendLine($"{DisplayWithAbbreviation(category.Name, category.Abbreviation)}  {category.Id}");
                if (category.Parent is not null)
                    text.AppendLine($"  Parent: {Display(category.Parent.Name)}  {category.Parent.Id}");
                break;
            case GrammarInflectionFeatureRead feature:
                text.AppendLine($"{DisplayWithAbbreviation(feature.Name, feature.Abbreviation)}  " +
                    $"{feature.Id}  {feature.FeatureKind}");
                foreach (var value in feature.Values.Take(TextListLimit))
                    text.AppendLine($"  Value: {Display(value.Name)}  {value.Id}");
                AppendOverflow(text, feature.Values.Count);
                break;
            case GrammarLexicalEntryRead entry:
                text.AppendLine($"{Display(entry.Headword)}  {entry.Id}");
                if (entry.LexemeForm is { } lexeme) RenderForm(text, "Lexeme form", lexeme);
                foreach (var sense in entry.Senses.Take(TextListLimit))
                {
                    text.AppendLine($"  Sense: {DisplayWithWritingSystem(sense.Gloss, sense.GlossWritingSystem)}  {sense.Id}");
                    if (sense.Definition is not null)
                        text.AppendLine($"    Definition: {DisplayWithWritingSystem(sense.Definition, sense.DefinitionWritingSystem)}");
                    if (sense.GrammaticalInfo is { } grammaticalInfo)
                    {
                        text.AppendLine($"    Grammatical information: {grammaticalInfo.Kind}  {grammaticalInfo.Id}");
                        if (grammaticalInfo.PartOfSpeech is { } category)
                            text.AppendLine($"      Part of speech: {Display(category.Name)}  {category.Id}");
                        if (grammaticalInfo.ResultPartOfSpeech is { } resultCategory)
                            text.AppendLine($"      Result part of speech: {Display(resultCategory.Name)}  {resultCategory.Id}");
                    }
                }
                AppendOverflow(text, entry.Senses.Count);
                foreach (var form in entry.AlternateForms.Take(TextListLimit)) RenderForm(text, "Alternate form", form);
                AppendOverflow(text, entry.AlternateForms.Count);
                break;
        }
    }

    private static void RenderForm(StringBuilder text, string label, GrammarFormRead form)
    {
        var shown = form.Form == NullAffixForm && form.FormKind.Contains("Affix", StringComparison.Ordinal)
            ? NullAffixForm + " (null allomorph)" : Display(form.Form);
        text.AppendLine($"  {label}: {shown} ({Display(form.WritingSystem)}) " +
            $"[{form.FormKind}]  {form.Id}");
        if (form.MorphType is not null)
            text.AppendLine($"    Morph type: {form.MorphType}  {form.MorphTypeId}");
        foreach (var environment in form.Environments.Take(TextListLimit))
            text.AppendLine($"    Environment: {Display(environment.Notation)}  {environment.Id}");
        AppendOverflow(text, form.Environments.Count);
    }

    private static string DisplayWithAbbreviation(string? name, string? abbreviation) =>
        abbreviation is null ? Display(name) : $"{Display(name)} [{abbreviation}]";

    private static void AppendOverflow(StringBuilder text, int count)
    {
        if (count > TextListLimit) text.AppendLine($"  and {count - TextListLimit:N0} more; use --json for all");
    }

    private static string Display(string? value) => string.IsNullOrEmpty(value) ? "(unnamed)" : value;

    private static string DisplayWithWritingSystem(string? value, string? writingSystem) =>
        writingSystem is null ? Display(value) : $"{Display(value)} ({writingSystem})";
}
