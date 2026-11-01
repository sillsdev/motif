using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names a new exception feature, the tag FieldWorks keeps in the project's Exception Features list.</summary>
/// <param name="Name">The feature name, unique among the project's exception features in <paramref name="WritingSystem"/>.</param>
/// <param name="WritingSystem">The writing system tag for <paramref name="Name"/> and <paramref name="Abbreviation"/>.</param>
/// <param name="Abbreviation">The optional short label FieldWorks shows beside the name.</param>
public sealed record AuthorExceptionFeatureIntent(string Name, string WritingSystem, string? Abbreviation = null);

/// <summary>Parses the closed JSON intent for creating one exception feature.</summary>
public static class AuthorExceptionFeatureIntentParser
{
    /// <summary>Reads name, writing system and the optional abbreviation.</summary>
    public static AuthorExceptionFeatureIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "AuthorExceptionFeature", "name", "ws", "abbreviation");
        return new AuthorExceptionFeatureIntent(
            SoundSystemIntentParsing.Text(authored, "name"),
            SoundSystemIntentParsing.Text(authored, "ws"),
            authored.TryGetProperty("abbreviation", out _) ? SoundSystemIntentParsing.Text(authored, "abbreviation") : null);
    }
}

/// <summary>
/// Lowers one <see cref="AuthorExceptionFeatureIntent"/> into a create in the Exception Features list, its name and its
/// abbreviation. The list is sorted in FieldWorks, so the stored position carries no meaning.
/// </summary>
public static class AuthorExceptionFeatureComposer
{
    private const string ConstructName = "AuthorExceptionFeature";
    private const string Rationale = "Authored by the AuthorExceptionFeature composer.";

    /// <summary>Authors one exception feature without mutating the caller's project.</summary>
    /// <param name="cache">The project to read the Exception Features list from.</param>
    /// <param name="intent">The authored feature.</param>
    /// <param name="mintId">Overrides id minting; tests supply a deterministic source.</param>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, AuthorExceptionFeatureIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        SoundSystemAuthoring.RequireName(intent.Name);
        var ws = cache.WritingSystemFactory.GetWsFromStr(intent.WritingSystem);
        if (string.IsNullOrWhiteSpace(intent.WritingSystem) || ws == 0)
            throw new InvalidOperationException(
                $"'{ConstructName}': writing system tag '{intent.WritingSystem}' is not known to this project.");
        if (intent.Abbreviation is { } abbreviation && string.IsNullOrWhiteSpace(abbreviation))
            throw new InvalidOperationException($"'{ConstructName}': an abbreviation, when given, must not be blank.");
        var list = cache.LangProject.MorphologicalDataOA?.ProdRestrictOA ??
            throw new InvalidOperationException(
                $"'{ConstructName}': the project has no Exception Features list. Open Grammar, Exception Features in " +
                "FieldWorks once, save, and reload the project.");
        var name = intent.Name.Trim().Normalize(NormalizationForm.FormD);
        var clash = list.ReallyReallyAllPossibilities.FirstOrDefault(existing =>
            string.Equals(existing.Name.get_String(ws)?.Text?.Normalize(NormalizationForm.FormD), name,
                StringComparison.Ordinal));
        if (clash is not null)
            throw new InvalidOperationException(
                $"'{ConstructName}': the exception feature '{intent.Name}' already exists as " +
                $"'{CanonicalId.FromGuid(clash.Guid).Value}'; refer to it instead of creating a second one.");

        var builder = new SoundSystemAuthoring.Builder(mintId, Rationale);
        var created = builder.Create(CmPossibilityListPossibilitiesOperationKinds.Create,
            CanonicalId.FromGuid(list.Guid), new { @class = "CmPossibility" });
        builder.Text(CmPossibilityNameOperationKinds.SetName, created, cache, ws, name);
        if (intent.Abbreviation is { } text)
            builder.Text(CmPossibilityAbbreviationOperationKinds.SetAbbreviation, created, cache, ws,
                text.Trim().Normalize(NormalizationForm.FormD));
        return builder.Operations;
    }
}
