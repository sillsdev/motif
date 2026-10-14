using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

internal static class GrammarReadTextRenderer
{
    public static string Render(GrammarReadResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Grammar objects: {response.Kind} ({response.Returned} of {response.Total}; offset {response.Offset}, limit {response.Limit})");
        text.AppendLine($"Baseline: {(response.IsStale ? "saved since capture" : "current")}");
        foreach (var item in response.Items) RenderItem(text, item);
        if (response.Items.Count == 0) text.AppendLine("No matching objects.");
        return text.ToString();
    }

    private static void RenderItem(StringBuilder text, GrammarReadItem item)
    {
        switch (item)
        {
            case GrammarPhonemeRead phoneme:
                text.AppendLine($"{Display(phoneme.Name)}  {phoneme.Id}");
                foreach (var representation in phoneme.Representations)
                    text.AppendLine($"  Representation: {Display(representation.Text)} ({Display(representation.WritingSystem)})");
                break;
            case GrammarNaturalClassRead naturalClass:
                text.AppendLine($"{Display(naturalClass.Name)}  {naturalClass.Id}  {naturalClass.ClassKind}");
                foreach (var member in naturalClass.Members)
                    text.AppendLine($"  Member: {Display(member.Name)}  {member.Id}");
                foreach (var feature in naturalClass.Features)
                    text.AppendLine($"  Feature: {Display(feature.Feature.Name)}  {feature.Feature.Id}" +
                        (feature.Value is null ? string.Empty : $" = {Display(feature.Value.Name)}  {feature.Value.Id}"));
                break;
            case GrammarEnvironmentRead environment:
                text.AppendLine($"{Display(environment.Name)}  {environment.Id}");
                text.AppendLine($"  Notation: {Display(environment.Notation)}");
                break;
            case GrammarPartOfSpeechRead category:
                text.AppendLine($"{Display(category.Name)}  {category.Id}");
                if (category.Parent is not null)
                    text.AppendLine($"  Parent: {Display(category.Parent.Name)}  {category.Parent.Id}");
                break;
            case GrammarInflectionFeatureRead feature:
                text.AppendLine($"{Display(feature.Name)}  {feature.Id}  {feature.FeatureKind}");
                foreach (var value in feature.Values)
                    text.AppendLine($"  Value: {Display(value.Name)}  {value.Id}");
                break;
            case GrammarLexicalEntryRead entry:
                text.AppendLine($"{Display(entry.Headword)}  {entry.Id}");
                if (entry.LexemeForm is { } lexeme) RenderForm(text, "Lexeme form", lexeme);
                foreach (var sense in entry.Senses)
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
                foreach (var form in entry.AlternateForms) RenderForm(text, "Alternate form", form);
                break;
        }
    }

    private static void RenderForm(StringBuilder text, string label, GrammarFormRead form)
    {
        text.AppendLine($"  {label}: {Display(form.Form)}  {form.Id}");
        if (form.MorphType is not null)
            text.AppendLine($"    Morph type: {form.MorphType}  {form.MorphTypeId}");
        foreach (var environment in form.Environments)
            text.AppendLine($"    Environment: {Display(environment.Notation)}  {environment.Id}");
    }

    private static string Display(string? value) => string.IsNullOrEmpty(value) ? "(unnamed)" : value;

    private static string DisplayWithWritingSystem(string? value, string? writingSystem) =>
        writingSystem is null ? Display(value) : $"{Display(value)} ({writingSystem})";
}
