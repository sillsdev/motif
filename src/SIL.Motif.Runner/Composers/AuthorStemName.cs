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

/// <summary>One closed inflection feature and the value a stem-name region requires.</summary>
public sealed record InflectionFeatureChoice(CanonicalId Feature, CanonicalId Value);

/// <summary>Authors a category-owned stem name with one or more explicit feature regions.</summary>
public sealed record AuthorStemNameIntent(CanonicalId Category, string Name, string WritingSystem,
    IReadOnlyList<IReadOnlyList<InflectionFeatureChoice>> Regions, string? Abbreviation = null);

/// <summary>Parses closed stem-name input and each region's closed feature/value pairs.</summary>
public static class AuthorStemNameIntentParser
{
    public static AuthorStemNameIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "AuthorStemName", "category", "name", "ws", "regions", "abbreviation");
        if (!input.TryGetProperty("regions", out var regions) || regions.ValueKind != JsonValueKind.Array)
            throw new ContractParseException("'AuthorStemName': 'regions' must be an array of feature-choice arrays.");
        var parsed = regions.EnumerateArray().Select(region =>
        {
            if (region.ValueKind != JsonValueKind.Array) throw new ContractParseException("A stem-name region must be an array.");
            return (IReadOnlyList<InflectionFeatureChoice>)region.EnumerateArray().Select(choice =>
            {
                SoundSystemIntentParsing.Object(choice, "stem-name choice", "feature", "value");
                return new InflectionFeatureChoice(SoundSystemIntentParsing.Id(choice, "feature"),
                    SoundSystemIntentParsing.Id(choice, "value"));
            }).ToArray();
        }).ToArray();
        return new(SoundSystemIntentParsing.Id(input, "category"), SoundSystemIntentParsing.Text(input, "name"),
            SoundSystemIntentParsing.Text(input, "ws"), parsed,
            input.TryGetProperty("abbreviation", out _) ? SoundSystemIntentParsing.Text(input, "abbreviation") : null);
    }
}

/// <summary>Creates a stem name and complete regions without attaching it to an allomorph.</summary>
public static class AuthorStemNameComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorStemNameIntent intent,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var category = InflectionAuthoring.Category(cache, intent.Category);
        var ws = InflectionAuthoring.WritingSystem(cache, intent.WritingSystem);
        var name = InflectionAuthoring.Text(intent.Name, "name");
        var abbreviation = intent.Abbreviation is null ? null : InflectionAuthoring.Text(intent.Abbreviation, "abbreviation");
        if (intent.Regions is not { Count: > 0 }) throw new InvalidOperationException("A stem name needs at least one nonempty region.");
        foreach (var region in intent.Regions)
        {
            if (region is not { Count: > 0 }) throw new InvalidOperationException("A stem-name region must not be empty.");
            var features = new HashSet<Guid>();
            foreach (var choice in region)
            {
                var feature = ReferenceFieldLowering.Resolve<IFsClosedFeature>(cache, choice.Feature, "AuthorStemName");
                var value = ReferenceFieldLowering.Resolve<IFsSymFeatVal>(cache, choice.Value, "AuthorStemName");
                if (!cache.LangProject.MsFeatureSystemOA.FeaturesOC.Contains(feature) || !feature.ValuesOC.Contains(value))
                    throw new InvalidOperationException("Each region choice must name a value of its closed morphosyntactic feature.");
                if (!features.Add(feature.Guid)) throw new InvalidOperationException("A feature may occur only once in one region.");
            }
        }
        InflectionAuthoring.UniqueName(category.StemNamesOC, item => item.Name.get_String(ws)?.Text, name);
        var builder = new SoundSystemAuthoring.Builder(mintId, "Authored by the AuthorStemName composer.");
        var stemName = builder.Create(PartOfSpeechStemNamesOperationKinds.Create, intent.Category);
        builder.Text(MoStemNameNameOperationKinds.SetName, stemName, cache, ws, name);
        if (abbreviation is not null) builder.Text(MoStemNameAbbreviationOperationKinds.SetAbbreviation, stemName, cache, ws, abbreviation);
        foreach (var region in intent.Regions)
        {
            var structure = builder.Create(MoStemNameRegionsOperationKinds.Create, stemName);
            foreach (var choice in region.OrderBy(item => item.Feature.Value, StringComparer.Ordinal))
            {
                var spec = builder.Create(FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs, structure);
                builder.Add(FsFeatureSpecificationFeatureOperationKinds.SetFeature, spec, new { @ref = choice.Feature.Value });
                builder.Add(FsClosedValueValueOperationKinds.SetValue, spec, new { @ref = choice.Value.Value });
            }
        }
        return builder.Operations;
    }
}
