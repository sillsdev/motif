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

/// <summary>One new circumfix entry: a prefix part and a suffix part that attach together, with one sense.</summary>
/// <param name="Prefix">The prefix part, without affix hyphens.</param>
/// <param name="Suffix">The suffix part, without affix hyphens.</param>
/// <param name="FormWritingSystem">The vernacular writing system tag of both parts.</param>
/// <param name="Gloss">The sense's gloss.</param>
/// <param name="AnalysisWritingSystem">The writing system tag of the gloss and definition.</param>
/// <param name="Msa">The grammatical info; inflectional, derivational or unclassified, never a stem.</param>
/// <param name="Definition">The sense's optional definition.</param>
/// <param name="PrefixEnvironments">The environments the prefix part occurs in; absent means any.</param>
/// <param name="SuffixEnvironments">The environments the suffix part occurs in; absent means any.</param>
/// <param name="InflectionClasses">For an inflectional circumfix, the classes both parts attach to.</param>
public sealed record AuthorCircumfixIntent(
    string Prefix,
    string Suffix,
    string FormWritingSystem,
    string Gloss,
    string AnalysisWritingSystem,
    LexicalEntryMsa Msa,
    string? Definition = null,
    IReadOnlyList<CanonicalId>? PrefixEnvironments = null,
    IReadOnlyList<CanonicalId>? SuffixEnvironments = null,
    IReadOnlyList<CanonicalId>? InflectionClasses = null);

/// <summary>Parses the closed JSON intent for creating one circumfix entry.</summary>
public static class AuthorCircumfixIntentParser
{
    private const string ConstructName = "AuthorCircumfix";

    /// <summary>Reads both parts, the gloss, the grammatical info and the optional conditions.</summary>
    public static AuthorCircumfixIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "prefix", "suffix", "formWs", "gloss", "analysisWs",
            "definition", "msa", "prefixEnvironments", "suffixEnvironments", "inflectionClasses");
        return new AuthorCircumfixIntent(
            SoundSystemIntentParsing.Text(authored, "prefix"), SoundSystemIntentParsing.Text(authored, "suffix"),
            SoundSystemIntentParsing.Text(authored, "formWs"), SoundSystemIntentParsing.Text(authored, "gloss"),
            SoundSystemIntentParsing.Text(authored, "analysisWs"),
            AuthorLexicalEntryIntentParser.ParseMsa(authored, ConstructName),
            authored.TryGetProperty("definition", out _) ? SoundSystemIntentParsing.Text(authored, "definition") : null,
            AuthorLexicalEntryIntentParser.NonEmptyIds(authored, "prefixEnvironments"),
            AuthorLexicalEntryIntentParser.NonEmptyIds(authored, "suffixEnvironments"),
            AuthorLexicalEntryIntentParser.NonEmptyIds(authored, "inflectionClasses"));
    }
}

/// <summary>
/// Lowers one <see cref="AuthorCircumfixIntent"/> into the operations that make a circumfix entry the way FieldWorks
/// holds it: an abstract lexeme form of the circumfix type, written <c>prefix- -suffix</c>, and a prefix and a suffix alternate
/// form that carry the conditions. FieldWorks' loader pairs the two parts into one rule, so neither part alone parses.
/// </summary>
public static class AuthorCircumfixComposer
{
    private const string ConstructName = "AuthorCircumfix";
    private const string Rationale = "Authored by the AuthorCircumfix composer.";

    /// <summary>Authors one circumfix entry without mutating the caller's project.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored circumfix.</param>
    /// <param name="mintId">Overrides id minting; tests supply a deterministic source.</param>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, AuthorCircumfixIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(intent.Msa);
        var prefix = Part(intent.Prefix, "prefix");
        var suffix = Part(intent.Suffix, "suffix");
        LexicalFormAuthoring.RequireWritingSystem(cache, intent.FormWritingSystem, vernacular: true, ConstructName);
        if (intent.Msa.Kind == LexicalEntryMsaKind.Stem)
            throw new InvalidOperationException($"'{ConstructName}': a circumfix is an affix, so its msa cannot be a stem.");
        if (intent.InflectionClasses is not null && intent.Msa.Kind == LexicalEntryMsaKind.Derivational)
            throw new InvalidOperationException(LexicalFormAuthoring.DerivationalClassesRefusal(ConstructName));

        var source = mintId ?? (() => CanonicalId.Mint());
        CanonicalId Mint() => LexicalFormAuthoring.Normalize(source());
        var circumfixType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphCircumfix);
        var operations = AuthorLexicalEntryComposer.BuildEntry(cache,
            new AuthorLexicalEntryIntent("", intent.FormWritingSystem, circumfixType, intent.Gloss,
                intent.AnalysisWritingSystem, intent.Msa, intent.Definition),
            Mint, new { morphType = circumfixType.Value, ws = intent.FormWritingSystem, text = $"{prefix}- -{suffix}" }).ToList();
        var entry = operations.First(operation => operation.Kind == LexDbEntriesOperationKinds.Create);
        var lexemeForm = operations.First(operation => operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm);

        operations.Add(new OperationEnvelope(Mint(), MoFormIsAbstractOperationKinds.SetIsAbstract,
            target: lexemeForm.EntityId, after: JsonSerializer.SerializeToElement(new { value = true }),
            dependsOn: [new OperationDependency(lexemeForm.OperationId)], rationale: Rationale));

        var category = LexicalFormAuthoring.Resolve<IPartOfSpeech>(cache, intent.Msa.Category, ConstructName);
        IReadOnlyList<CanonicalId> classes = intent.InflectionClasses is null ? []
            : LexicalFormAuthoring.RequireInflectionClasses(cache, intent.InflectionClasses, [category], ConstructName);
        var (prefixId, prefixOperation) = AddPart(MoMorphTypeTags.kguidMorphPrefix, prefix, intent.PrefixEnvironments, null, null);
        AddPart(MoMorphTypeTags.kguidMorphSuffix, suffix, intent.SuffixEnvironments, new Placement(prefixId, null), prefixOperation);
        return operations;

        (CanonicalId Form, CanonicalId Operation) AddPart(Guid morphType, string text,
            IReadOnlyList<CanonicalId>? environments, Placement? placement, CanonicalId? after)
        {
            IReadOnlyList<CanonicalId> required = environments is null ? []
                : LexicalFormAuthoring.RequireEnvironments(cache, LexicalFormAuthoring.Normalize(environments)!, ConstructName);
            var formId = Mint();
            var createId = Mint();
            operations.Add(new OperationEnvelope(createId, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms,
                entityId: formId, target: entry.EntityId, placement: placement,
                after: JsonSerializer.SerializeToElement(new
                    { morphType = CanonicalId.FromGuid(morphType).Value, ws = intent.FormWritingSystem, text }),
                dependsOn: Dependencies(after), rationale: Rationale));
            foreach (var (kind, member) in LexicalFormAuthoring.Links(MoFormConcreteClass.AffixAllomorph, required, classes))
                operations.Add(new OperationEnvelope(Mint(), kind, target: formId,
                    after: JsonSerializer.SerializeToElement(new { member = member.Value }),
                    dependsOn: [new OperationDependency(createId)], rationale: Rationale));
            return (formId, createId);
        }

        IReadOnlyList<OperationDependency> Dependencies(CanonicalId? after)
        {
            var dependencies = new List<OperationDependency>
                { new(entry.OperationId), new(lexemeForm.OperationId) };
            if (after is { } earlier) dependencies.Add(new OperationDependency(earlier));
            return dependencies;
        }
    }

    private static string Part(string text, string name)
    {
        LexicalFormAuthoring.RequireText(text, name, ConstructName);
        if (text.Contains('-') || text.Contains('.') || text.Any(char.IsWhiteSpace))
            throw new InvalidOperationException(
                $"'{ConstructName}': '{name}' is written without affix hyphens, spaces or full stops; the composer adds the hyphens and FieldWorks splits the lexeme form on a space or full stop.");
        return text;
    }
}
