using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Authors a category's inflection class, optionally under a class it already owns.</summary>
public sealed record AuthorInflectionClassIntent(CanonicalId Category, string Name, string WritingSystem,
    string? Abbreviation = null, CanonicalId? Parent = null);

/// <summary>Parses the closed category, name, writing system and optional abbreviation and parent.</summary>
public static class AuthorInflectionClassIntentParser
{
    public static AuthorInflectionClassIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "AuthorInflectionClass", "category", "name", "ws", "abbreviation", "parent");
        return new(SoundSystemIntentParsing.Id(input, "category"), SoundSystemIntentParsing.Text(input, "name"),
            SoundSystemIntentParsing.Text(input, "ws"),
            input.TryGetProperty("abbreviation", out _) ? SoundSystemIntentParsing.Text(input, "abbreviation") : null,
            input.TryGetProperty("parent", out _) ? SoundSystemIntentParsing.Id(input, "parent") : null);
    }
}

/// <summary>Creates a class or subclass with explicit ownership, leaving the category's default class unchanged.</summary>
public static class AuthorInflectionClassComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorInflectionClassIntent intent,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var category = InflectionAuthoring.Category(cache, intent.Category);
        var ws = InflectionAuthoring.WritingSystem(cache, intent.WritingSystem);
        var name = InflectionAuthoring.Text(intent.Name, "name");
        var abbreviation = intent.Abbreviation is null ? null : InflectionAuthoring.Text(intent.Abbreviation, "abbreviation");
        InflectionAuthoring.UniqueName(InflectionAuthoring.Classes(category), item => item.Name.get_String(ws)?.Text, name);
        var owner = intent.Category;
        var kind = PartOfSpeechInflectionClassesOperationKinds.Create;
        if (intent.Parent is { } parentId)
        {
            var parent = ReferenceFieldLowering.Resolve<IMoInflClass>(cache, parentId, "AuthorInflectionClass");
            if (InflectionAuthoring.OwningCategory(parent)?.Guid != category.Guid)
                throw new InvalidOperationException("The parent class must belong to the declared category.");
            owner = parentId;
            kind = MoInflClassSubclassesOperationKinds.Create;
        }
        var builder = new SoundSystemAuthoring.Builder(mintId, "Authored by the AuthorInflectionClass composer.");
        var created = builder.Create(kind, owner);
        builder.Text(MoInflClassNameOperationKinds.SetName, created, cache, ws, name);
        if (abbreviation is not null) builder.Text(MoInflClassAbbreviationOperationKinds.SetAbbreviation, created, cache, ws, abbreviation);
        return builder.Operations;
    }
}
