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

/// <summary>One named choice of a phonological feature, such as a plus or a minus.</summary>
public sealed record PhonologicalFeatureOption(string Name, string? Abbreviation = null);

/// <summary>Authors a closed phonological feature and its values in the project's phonological feature system.</summary>
public sealed record AuthorPhonologicalFeatureIntent(string Name, IReadOnlyList<PhonologicalFeatureOption> Values,
    string? Abbreviation = null);

/// <summary>Adds one value to an existing closed phonological feature.</summary>
public sealed record AddPhonologicalFeatureValueIntent(CanonicalId Feature, PhonologicalFeatureOption Value);

/// <summary>Parses the closed feature intent.</summary>
public static class AuthorPhonologicalFeatureIntentParser
{
    public static AuthorPhonologicalFeatureIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "AuthorPhonologicalFeature", "name", "abbreviation", "values");
        return new(SoundSystemIntentParsing.Text(input, "name"),
            SoundSystemIntentParsing.Array(input, "values").Select(Option).ToArray(),
            input.TryGetProperty("abbreviation", out _) ? SoundSystemIntentParsing.Text(input, "abbreviation") : null);
    }

    internal static PhonologicalFeatureOption Option(JsonElement value)
    {
        SoundSystemIntentParsing.Object(value, "phonological feature value", "name", "abbreviation");
        return new(SoundSystemIntentParsing.Text(value, "name"),
            value.TryGetProperty("abbreviation", out _) ? SoundSystemIntentParsing.Text(value, "abbreviation") : null);
    }
}

/// <summary>Parses the closed add-value intent.</summary>
public static class AddPhonologicalFeatureValueIntentParser
{
    public static AddPhonologicalFeatureValueIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "AddPhonologicalFeatureValue", "feature", "name", "abbreviation");
        return new(SoundSystemIntentParsing.Id(input, "feature"), AuthorPhonologicalFeatureIntentParser.Option(input));
    }
}

/// <summary>
/// Creates a closed feature in <c>LangProject.PhFeatureSystemOA</c>.
/// </summary>
public static class AuthorPhonologicalFeatureComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorPhonologicalFeatureIntent intent,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var system = cache.LangProject.PhFeatureSystemOA ??
            throw new InvalidOperationException("The project has no phonological feature system.");
        var ws = cache.DefaultAnalWs;
        var name = InflectionAuthoring.Text(intent.Name, "name");
        var abbreviation = intent.Abbreviation is null ? null : InflectionAuthoring.Text(intent.Abbreviation, "abbreviation");
        if (intent.Values is not { Count: > 0 }) throw new InvalidOperationException("A closed feature needs at least one value.");
        InflectionAuthoring.UniqueName(system.FeaturesOC, feature => feature.Name.get_String(ws)?.Text, name);
        if (abbreviation is not null)
            InflectionAuthoring.UniqueName(system.FeaturesOC, feature => feature.Abbreviation.get_String(ws)?.Text, abbreviation);
        var values = Normalize(intent.Values);
        var builder = new SoundSystemAuthoring.Builder(mintId, "Authored by the AuthorPhonologicalFeature composer.");
        var feature = builder.Create(FsFeatureSystemFeaturesOperationKinds.Create, SoundSystemAuthoring.Id(system),
            new { @class = "FsClosedFeature" });
        builder.Text(FsFeatDefnNameOperationKinds.SetName, feature, cache, ws, name);
        if (abbreviation is not null) builder.Text(FsFeatDefnAbbreviationOperationKinds.SetAbbreviation, feature, cache, ws, abbreviation);
        foreach (var (valueName, valueAbbreviation) in values) AddValue(builder, cache, feature, valueName, valueAbbreviation);
        return builder.Operations;
    }

    internal static (string Name, string? Abbreviation)[] Normalize(IReadOnlyList<PhonologicalFeatureOption> options)
    {
        var values = options.Select(o => (Name: InflectionAuthoring.Text(o.Name, "value name"),
            Abbreviation: o.Abbreviation is null ? null : InflectionAuthoring.Text(o.Abbreviation, "value abbreviation"))).ToArray();
        if (values.Select(v => v.Name).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidOperationException("Feature value names must be distinct after normalization.");
        var abbreviations = values.Where(v => v.Abbreviation is not null).Select(v => v.Abbreviation).ToArray();
        if (abbreviations.Distinct(StringComparer.Ordinal).Count() != abbreviations.Length)
            throw new InvalidOperationException("Feature value abbreviations must be distinct after normalization.");
        return values;
    }

    internal static void AddValue(SoundSystemAuthoring.Builder builder, LcmCache cache, CanonicalId feature, string name,
        string? abbreviation)
    {
        var value = builder.Create(FsClosedFeatureValuesOperationKinds.Create, feature);
        builder.Text(FsSymFeatValNameOperationKinds.SetName, value, cache, cache.DefaultAnalWs, name);
        if (abbreviation is not null)
            builder.Text(FsSymFeatValAbbreviationOperationKinds.SetAbbreviation, value, cache, cache.DefaultAnalWs, abbreviation);
    }
}

/// <summary>Adds a value to a closed phonological feature, refusing a name or abbreviation the feature already has.</summary>
public static class AddPhonologicalFeatureValueComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AddPhonologicalFeatureValueIntent intent,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var feature = ReferenceFieldLowering.Resolve<IFsClosedFeature>(cache, intent.Feature, "AddPhonologicalFeatureValue");
        if (!cache.LangProject.PhFeatureSystemOA.FeaturesOC.Contains(feature))
            throw new InvalidOperationException("The feature must belong to the phonological feature system.");
        var ws = cache.DefaultAnalWs;
        var (name, abbreviation) = AuthorPhonologicalFeatureComposer.Normalize([intent.Value])[0];
        InflectionAuthoring.UniqueName(feature.ValuesOC, value => value.Name.get_String(ws)?.Text, name);
        if (abbreviation is not null)
            InflectionAuthoring.UniqueName(feature.ValuesOC, value => value.Abbreviation.get_String(ws)?.Text, abbreviation);
        var builder = new SoundSystemAuthoring.Builder(mintId, "Authored by the AddPhonologicalFeatureValue composer.");
        AuthorPhonologicalFeatureComposer.AddValue(builder, cache, intent.Feature, name, abbreviation);
        return builder.Operations;
    }
}

/// <summary>Sets and clears feature values on one existing phoneme or feature-defined natural class.</summary>
/// <param name="Target">The phoneme or natural class.</param>
/// <param name="Set">Feature values to set; a feature already specified takes the new value.</param>
/// <param name="Clear">Features whose specification is removed.</param>
public sealed record EditFeatureValuesIntent(CanonicalId Target, IReadOnlyList<PhonologicalFeatureValue> Set,
    IReadOnlyList<CanonicalId> Clear);

/// <summary>Parses the closed set and clear lists shared by the phoneme and natural-class feature edits.</summary>
public static class EditFeatureValuesIntentParser
{
    public static EditFeatureValuesIntent Parse(JsonElement input, string kind)
    {
        SoundSystemIntentParsing.Object(input, kind, "target", "set", "clear");
        return new(SoundSystemIntentParsing.Id(input, "target"),
            SoundSystemIntentParsing.Features(input, "set") ?? [],
            input.TryGetProperty("clear", out _) ? SoundSystemIntentParsing.Ids(input, "clear", true) : []);
    }
}

internal static class FeatureStructureEdit
{
    /// <summary>
    /// Lowers set and clear against the owner's current structure: a missing structure is created, a present
    /// feature takes its new value in place, a new feature gains a specification and a cleared one loses it.
    /// </summary>
    internal static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, string construct, EditFeatureValuesIntent intent,
        IFsFeatStruc? current, string createKind, bool requireOne, Func<CanonicalId>? mintId)
    {
        if (intent.Set.Count == 0 && intent.Clear.Count == 0)
            throw new InvalidOperationException($"'{construct}': nothing to set or clear.");
        if (intent.Clear.Distinct().Count() != intent.Clear.Count)
            throw new InvalidOperationException($"'{construct}': 'clear' must not name a feature twice.");
        if (intent.Set.Select(v => v.Feature).Intersect(intent.Clear).Any())
            throw new InvalidOperationException($"'{construct}': a feature cannot be both set and cleared.");
        SoundSystemAuthoring.ValidateFeatures(cache, intent.Set);
        var specs = current?.FeatureSpecsOC.ToArray() ?? [];
        if (specs.Any(s => s is not IFsClosedValue { FeatureRA: not null }))
            throw new InvalidOperationException(
                $"'{construct}': the target holds a specification that is not a closed feature value, which Motif does not edit.");
        IFsFeatureSpecification? Find(CanonicalId feature) => specs.FirstOrDefault(s => s.FeatureRA!.Guid == feature.ToGuid());
        foreach (var feature in intent.Clear)
            if (Find(feature) is null)
                throw new InvalidOperationException($"'{construct}': feature '{feature.Value}' is not specified on the target.");
        var remaining = specs.Length - intent.Clear.Count + intent.Set.Count(v => Find(v.Feature) is null);
        if (requireOne && remaining < 1)
            throw new InvalidOperationException(
                $"'{construct}': a feature-defined natural class needs at least one feature value; define a new class instead of emptying this one.");
        var builder = new SoundSystemAuthoring.Builder(mintId, $"Authored by the {construct} composer.");
        var owner = intent.Target;
        if (current is null) { builder.Features(cache, owner, createKind, intent.Set); return builder.Operations; }
        var structure = SoundSystemAuthoring.Id(current);
        foreach (var feature in intent.Clear)
            builder.Add(FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs, SoundSystemAuthoring.Id(Find(feature)!), new { });
        foreach (var pair in intent.Set)
        {
            if (Find(pair.Feature) is { } existing)
            {
                if (existing is IFsClosedValue { ValueRA: { } value } && value.Guid == pair.Value.ToGuid()) continue;
                builder.Add(FsClosedValueValueOperationKinds.SetValue, SoundSystemAuthoring.Id(existing), new { @ref = pair.Value.Value });
                continue;
            }
            var spec = builder.Create(FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs, structure);
            builder.Add(FsFeatureSpecificationFeatureOperationKinds.SetFeature, spec, new { @ref = pair.Feature.Value });
            builder.Add(FsClosedValueValueOperationKinds.SetValue, spec, new { @ref = pair.Value.Value });
        }
        if (builder.Operations.Count == 0) throw new InvalidOperationException($"'{construct}': the target already has these values.");
        return builder.Operations;
    }
}

/// <summary>Sets or clears phonological feature values on one existing phoneme.</summary>
public static class EditPhonemeFeaturesComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditFeatureValuesIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var phoneme = SoundSystemAuthoring.RequirePhoneme(cache, intent.Target);
        return FeatureStructureEdit.Build(cache, "EditPhonemeFeatures", intent, phoneme.FeaturesOA,
            PhPhonemeFeaturesOperationKinds.Create, false, mintId);
    }
}

/// <summary>Sets or clears the feature constraints of one feature-defined natural class.</summary>
public static class EditNaturalClassFeaturesComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditFeatureValuesIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var naturalClass = ReferenceFieldLowering.Resolve<IPhNaturalClass>(cache, intent.Target, "EditNaturalClassFeatures");
        if (!cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Contains(naturalClass))
            throw new InvalidOperationException("The natural class is not owned by this project.");
        if (naturalClass is not IPhNCFeatures features)
            throw new InvalidOperationException(
                "A segment class has no feature constraints; change its members with compose-edit-natural-class.");
        return FeatureStructureEdit.Build(cache, "EditNaturalClassFeatures", intent, features.FeaturesOA,
            PhNCFeaturesFeaturesOperationKinds.Create, true, mintId);
    }
}
