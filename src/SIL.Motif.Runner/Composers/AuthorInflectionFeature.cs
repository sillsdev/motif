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

/// <summary>One named choice in a closed inflection feature.</summary>
public sealed record InflectionFeatureOption(string Name, string? Abbreviation = null);

/// <summary>Authors a closed morphosyntactic feature and its explicit choices.</summary>
public sealed record AuthorInflectionFeatureIntent(string Name, string WritingSystem,
    IReadOnlyList<InflectionFeatureOption> Values, string? Abbreviation = null);

/// <summary>Parses the closed feature input, including a nonempty list of named value objects.</summary>
public static class AuthorInflectionFeatureIntentParser
{
    public static AuthorInflectionFeatureIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "AuthorInflectionFeature", "name", "ws", "values", "abbreviation");
        if (!input.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            throw new ContractParseException("'AuthorInflectionFeature': 'values' must be an array of named choices.");
        var choices = values.EnumerateArray().Select(value =>
        {
            SoundSystemIntentParsing.Object(value, "inflection feature value", "name", "abbreviation");
            return new InflectionFeatureOption(SoundSystemIntentParsing.Text(value, "name"),
                value.TryGetProperty("abbreviation", out _) ? SoundSystemIntentParsing.Text(value, "abbreviation") : null);
        }).ToArray();
        return new(SoundSystemIntentParsing.Text(input, "name"), SoundSystemIntentParsing.Text(input, "ws"), choices,
            input.TryGetProperty("abbreviation", out _) ? SoundSystemIntentParsing.Text(input, "abbreviation") : null);
    }
}

/// <summary>Creates a feature in the morphosyntactic system without changing the caller's model.</summary>
public static class AuthorInflectionFeatureComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorInflectionFeatureIntent intent,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var name = InflectionAuthoring.Text(intent.Name, "name");
        var ws = InflectionAuthoring.WritingSystem(cache, intent.WritingSystem);
        var abbreviation = intent.Abbreviation is null ? null : InflectionAuthoring.Text(intent.Abbreviation, "abbreviation");
        if (intent.Values is not { Count: > 0 }) throw new InvalidOperationException("A closed feature needs at least one value.");
        var names = intent.Values.Select(value => InflectionAuthoring.Text(value.Name, "value name")).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new InvalidOperationException("Feature value names must be distinct after normalization.");
        var abbreviations = intent.Values.Select(value => value.Abbreviation is null ? null :
            InflectionAuthoring.Text(value.Abbreviation, "value abbreviation")).ToArray();
        var system = cache.LangProject.MsFeatureSystemOA ?? throw new InvalidOperationException("The project has no morphosyntactic feature system.");
        InflectionAuthoring.UniqueName(system.FeaturesOC, feature => feature.Name.get_String(ws)?.Text, name);
        var builder = new SoundSystemAuthoring.Builder(mintId, "Authored by the AuthorInflectionFeature composer.");
        var feature = builder.Create(FsFeatureSystemFeaturesOperationKinds.Create, CanonicalId.FromGuid(system.Guid),
            new { @class = "FsClosedFeature" });
        builder.Text(FsFeatDefnNameOperationKinds.SetName, feature, cache, ws, name);
        if (abbreviation is not null) builder.Text(FsFeatDefnAbbreviationOperationKinds.SetAbbreviation, feature, cache, ws, abbreviation);
        for (var index = 0; index < names.Length; index++)
        {
            var value = builder.Create(FsClosedFeatureValuesOperationKinds.Create, feature);
            builder.Text(FsSymFeatValNameOperationKinds.SetName, value, cache, ws, names[index]);
            if (abbreviations[index] is { } shortName)
                builder.Text(FsSymFeatValAbbreviationOperationKinds.SetAbbreviation, value, cache, ws, shortName);
        }
        return builder.Operations;
    }
}
