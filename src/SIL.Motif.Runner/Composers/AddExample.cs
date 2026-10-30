using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Adds a new example sentence, with optional translations, to a sense.</summary>
/// <param name="Sense">The sense that gets the example.</param>
/// <param name="Text">The example, one entry per writing system.</param>
/// <param name="Reference">The example's reference string, or null.</param>
/// <param name="Translations">Translations: a type and text in one or more writing systems each.</param>
/// <param name="After">The example of the sense to place it after; absent with <paramref name="Before"/> means last.</param>
/// <param name="Before">The example of the sense to place it before.</param>
public sealed record AddExampleIntent(
    CanonicalId Sense, IReadOnlyList<ExampleForm> Text, string? Reference,
    IReadOnlyList<AddExampleTranslation> Translations, CanonicalId? After = null, CanonicalId? Before = null);

/// <summary>One authored translation.</summary>
/// <param name="Type"><c>free</c>, <c>literal</c> or <c>back</c>.</param>
/// <param name="Text">The translation, one entry per writing system.</param>
public sealed record AddExampleTranslation(string Type, IReadOnlyList<ExampleForm> Text);

/// <summary>Parses the closed JSON intent for adding an example.</summary>
public static class AddExampleIntentParser
{
    private const string ConstructName = "AddExample";

    /// <summary>Reads the sense and the example text; translations, reference and a neighbour are optional.</summary>
    public static AddExampleIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "sense", "text", "reference", "translations", "after", "before");
        if (authored.TryGetProperty("after", out _) && authored.TryGetProperty("before", out _))
            throw new ContractParseException($"'{ConstructName}': give at most one of 'after' and 'before'.");
        var translations = new List<AddExampleTranslation>();
        if (authored.TryGetProperty("translations", out _))
            foreach (var item in SoundSystemIntentParsing.Array(authored, "translations"))
            {
                SoundSystemIntentParsing.Object(item, ConstructName + ".translations", "type", "text");
                var type = item.TryGetProperty("type", out _) ? SoundSystemIntentParsing.Text(item, "type") : "free";
                if (!LexExampleCreatePayload.TranslationTypes.Contains(type))
                    throw new ContractParseException(
                        $"'{ConstructName}': translation type '{type}' must be {string.Join(", ", LexExampleCreatePayload.TranslationTypes)}.");
                translations.Add(new(type, ParseForms(item, "text")));
            }
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "sense")),
            ParseForms(authored, "text"),
            authored.TryGetProperty("reference", out _) ? SoundSystemIntentParsing.Text(authored, "reference") : null,
            translations,
            authored.TryGetProperty("after", out _) ? LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "after")) : null,
            authored.TryGetProperty("before", out _) ? LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "before")) : null);
    }

    private static IReadOnlyList<ExampleForm> ParseForms(JsonElement owner, string name)
    {
        var forms = new List<ExampleForm>();
        foreach (var item in SoundSystemIntentParsing.Array(owner, name))
        {
            SoundSystemIntentParsing.Object(item, ConstructName + "." + name, "ws", "text");
            var text = SoundSystemIntentParsing.Text(item, "text");
            if (text.Length == 0)
                throw new ContractParseException($"'{ConstructName}': '{name}' holds an empty text.");
            forms.Add(new(SoundSystemIntentParsing.Text(item, "ws"), text));
        }
        if (forms.Count == 0)
            throw new ContractParseException($"'{ConstructName}': '{name}' needs text in at least one writing system.");
        if (forms.Select(form => form.Ws).Distinct(StringComparer.Ordinal).Count() != forms.Count)
            throw new ContractParseException($"'{ConstructName}': '{name}' names a writing system twice.");
        return forms;
    }
}

/// <summary>
/// Lowers an example addition to one <see cref="LexExampleOperationKinds.Create"/>. Every writing system must be one of
/// the project's vernacular or analysis writing systems. The example is placed last unless a neighbour is named.
/// </summary>
public static class AddExampleComposer
{
    private const string ConstructName = "AddExample";
    private const string Rationale = "Authored by the AddExample composer: the sense needs this example.";

    /// <summary>Composes the addition without mutating the caller's project.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored example.</param>
    /// <param name="mintId">Overrides id minting; the Draft supplies one so ids stay unique within it.</param>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, AddExampleIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var mint = mintId ?? (() => CanonicalId.Mint());
        var sense = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Sense, ConstructName) as ILexSense
            ?? throw new InvalidOperationException($"'{ConstructName}': 'sense' must name a sense.");
        var known = cache.ServiceLocator.WritingSystems.VernacularWritingSystems
            .Concat(cache.ServiceLocator.WritingSystems.AnalysisWritingSystems).Select(system => system.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var form in intent.Text.Concat(intent.Translations.SelectMany(item => item.Text)))
            if (!known.Contains(form.Ws))
                throw new InvalidOperationException(
                    $"'{ConstructName}': '{form.Ws}' is not one of this project's vernacular or analysis writing systems.");
        var placement = ExamplePlacement.For(sense, intent.After, intent.Before, ConstructName);
        var after = JsonSerializer.SerializeToElement(new
        {
            text = intent.Text.Select(form => new { ws = form.Ws, text = form.Text }).ToArray(),
            reference = intent.Reference,
            translations = intent.Translations.Select(item => new
            {
                id = CanonicalId.FromGuid(mint().ToGuid()).Value,
                type = item.Type,
                text = item.Text.Select(form => new { ws = form.Ws, text = form.Text }).ToArray(),
            }).ToArray(),
        });
        return [new(mint(), LexExampleOperationKinds.Create, entityId: CanonicalId.FromGuid(mint().ToGuid()),
            target: LexicalFormAuthoring.Normalize(intent.Sense), after: after, placement: placement, rationale: Rationale)];
    }
}
