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

/// <summary>Names a new part of speech, at the top of the category list or under an existing one.</summary>
/// <param name="Name">The category name, unique among the project's categories in <paramref name="WritingSystem"/>.</param>
/// <param name="WritingSystem">The writing system tag for <paramref name="Name"/> and <paramref name="Abbreviation"/>.</param>
/// <param name="Abbreviation">The optional short label FieldWorks shows in interlinear text and grammar lists.</param>
/// <param name="Parent">The optional existing part of speech this one becomes a subcategory of.</param>
public sealed record AuthorPartOfSpeechIntent(
    string Name,
    string WritingSystem,
    string? Abbreviation = null,
    CanonicalId? Parent = null);

/// <summary>Parses the closed JSON intent for creating one part of speech.</summary>
public static class AuthorPartOfSpeechIntentParser
{
    private const string ConstructName = "AuthorPartOfSpeech";

    /// <summary>Reads name, writing system, and the optional abbreviation and parent category.</summary>
    public static AuthorPartOfSpeechIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "name", "ws", "abbreviation", "parent");
        return new AuthorPartOfSpeechIntent(
            SoundSystemIntentParsing.Text(authored, "name"),
            SoundSystemIntentParsing.Text(authored, "ws"),
            authored.TryGetProperty("abbreviation", out _) ? SoundSystemIntentParsing.Text(authored, "abbreviation") : null,
            authored.TryGetProperty("parent", out _) ? SoundSystemIntentParsing.Id(authored, "parent") : null);
    }
}

/// <summary>
/// Lowers one <see cref="AuthorPartOfSpeechIntent"/> into a part-of-speech create, its name, and its
/// abbreviation. A category is appended: FieldWorks shows the list sorted, so its stored order carries no meaning.
/// </summary>
public static class AuthorPartOfSpeechComposer
{
    private const string ConstructName = "AuthorPartOfSpeech";
    private const string Rationale = "Authored by the AuthorPartOfSpeech composer.";

    /// <summary>Authors one part of speech without mutating the caller's project.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored category.</param>
    /// <param name="mintId">Overrides id minting; tests supply a deterministic source.</param>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, AuthorPartOfSpeechIntent intent, Func<CanonicalId>? mintId = null)
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

        var list = cache.LangProject.PartsOfSpeechOA ??
            throw new InvalidOperationException($"'{ConstructName}': the project has no parts-of-speech list.");
        var name = intent.Name.Trim().Normalize(NormalizationForm.FormD);
        var clash = list.ReallyReallyAllPossibilities.FirstOrDefault(existing =>
            string.Equals(existing.Name.get_String(ws)?.Text?.Normalize(NormalizationForm.FormD), name,
                StringComparison.Ordinal));
        if (clash is not null)
            throw new InvalidOperationException(
                $"'{ConstructName}': the category '{intent.Name}' already exists as " +
                $"'{CanonicalId.FromGuid(clash.Guid).Value}'; refer to it instead of creating a second one.");

        CanonicalId owner;
        string createKind;
        if (intent.Parent is { } parentId)
        {
            var parent = ReferenceFieldLowering.Resolve<IPartOfSpeech>(cache, parentId, ConstructName);
            if (!list.ReallyReallyAllPossibilities.Contains(parent))
                throw new InvalidOperationException(
                    $"'{ConstructName}': parent '{parentId.Value}' is not in the project's parts-of-speech list.");
            owner = parentId;
            createKind = CmPossibilitySubPossibilitiesOperationKinds.Create;
        }
        else
        {
            owner = CanonicalId.FromGuid(list.Guid);
            createKind = CmPossibilityListPossibilitiesOperationKinds.Create;
        }

        var mint = mintId ?? (() => CanonicalId.Mint());
        var createOperationId = mint();
        var categoryId = mint();
        var dependency = new[] { new OperationDependency(createOperationId) };
        var operations = new List<OperationEnvelope>
        {
            new(createOperationId, createKind, entityId: categoryId, target: owner,
                after: JsonSerializer.SerializeToElement(new { @class = "PartOfSpeech" }), rationale: Rationale),
            new(mint(), CmPossibilityNameOperationKinds.SetName, target: categoryId,
                after: JsonSerializer.SerializeToElement(new { ws = intent.WritingSystem, text = name }),
                dependsOn: dependency, rationale: Rationale),
        };
        if (intent.Abbreviation is { } text)
            operations.Add(new(mint(), CmPossibilityAbbreviationOperationKinds.SetAbbreviation, target: categoryId,
                after: JsonSerializer.SerializeToElement(new
                {
                    ws = intent.WritingSystem,
                    text = text.Trim().Normalize(NormalizationForm.FormD),
                }), dependsOn: dependency, rationale: Rationale));
        return operations;
    }
}
