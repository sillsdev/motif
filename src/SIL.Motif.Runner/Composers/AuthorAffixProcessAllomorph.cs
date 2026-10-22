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

/// <summary>One further affix-process form of an existing affix entry that already realizes itself as a process.</summary>
/// <param name="Entry">The existing, or earlier-staged, affix entry that gains the form.</param>
/// <param name="Input">The ordered flat stem pattern, as for a new affix process.</param>
/// <param name="Output">The realization, as for a new affix process.</param>
/// <param name="InflectionClasses">Inflection classes for inflectional grammatical info; derivational grammatical info uses its MSA restriction.</param>
/// <param name="Placement">The neighbouring alternate forms that define the insertion gap; required when any exist.</param>
public sealed record AuthorAffixProcessAllomorphIntent(CanonicalId Entry, IReadOnlyList<AffixProcessInputPart> Input,
    IReadOnlyList<AffixProcessOutputStep> Output, IReadOnlyList<CanonicalId>? InflectionClasses = null,
    Placement? Placement = null);

/// <summary>Parses the closed JSON intent for adding one affix-process allomorph to an entry.</summary>
public static class AuthorAffixProcessAllomorphIntentParser
{
    private const string ConstructName = "AuthorAffixProcessAllomorph";

    /// <summary>Reads the entry, pattern, realization, inflection classes and placement.</summary>
    public static AuthorAffixProcessAllomorphIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "entry", "input", "output", "inflectionClasses", "placement");
        var result = new AuthorAffixProcessAllomorphIntent(SoundSystemIntentParsing.Id(authored, "entry"),
            AuthorAffixProcessIntentParser.ParseInput(authored), AuthorAffixProcessIntentParser.ParseOutput(authored),
            AuthorLexicalEntryIntentParser.NonEmptyIds(authored, "inflectionClasses"),
            AuthorAlternateFormIntentParser.ParsePlacement(authored, ConstructName));
        try { AffixProcessIntentShape.ValidatePattern(result.Input, result.Output); }
        catch (InvalidOperationException exception) { throw new ContractParseException(exception.Message); }
        return result;
    }
}

/// <summary>
/// Lowers one <see cref="AuthorAffixProcessAllomorphIntent"/> into the operations that add an affix process to an
/// existing entry's alternate forms. The morph type is the entry's own, and the form applies to every
/// grammatical info the entry carries.
/// </summary>
public static class AuthorAffixProcessAllomorphComposer
{
    private const string ConstructName = "AuthorAffixProcessAllomorph";
    private const string Rationale = "Authored by the AuthorAffixProcessAllomorph composer.";

    /// <summary>Authors one process allomorph without mutating the caller's project.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored allomorph.</param>
    /// <param name="mintId">Overrides id minting; tests supply a deterministic source.</param>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorAffixProcessAllomorphIntent intent,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        AffixProcessIntentShape.ValidatePattern(intent.Input, intent.Output);
        var entryId = LexicalFormAuthoring.Normalize(intent.Entry);
        var classIds = LexicalFormAuthoring.Normalize(intent.InflectionClasses);
        var placement = intent.Placement is { } gap ? new Placement(
            gap.After is { } left ? LexicalFormAuthoring.Normalize(left) : null,
            gap.Before is { } right ? LexicalFormAuthoring.Normalize(right) : null) : null;
        var entry = LexicalFormAuthoring.Resolve<ILexEntry>(cache, entryId, ConstructName);
        AffixProcessConstruction.RequireProcessEntry(entry, ConstructName);
        var morphType = ((IMoAffixProcess)entry.LexemeFormOA).MorphTypeRA ?? throw new InvalidOperationException(
            $"'{ConstructName}': entry '{entryId.Value}' has an affix process without a morph type.");
        AffixProcessConstruction.RequireAffixMorphType(morphType);
        var categories = AuthorAlternateFormComposer.AffixCategories(entry);
        if (categories.Count == 0)
            throw new InvalidOperationException(
                $"'{ConstructName}': entry '{entryId.Value}' has no affix grammatical info for the form to apply to.");
        AffixProcessGraphBuilder.RequireReferences(cache, intent.Input, intent.Output);
        IReadOnlyList<CanonicalId> classes = [];
        if (classIds is not null)
        {
            var inflectionalCategories = LexicalFormAuthoring.InflectionalAffixCategories(entry, ConstructName);
            classes = LexicalFormAuthoring.RequireInflectionClasses(cache, classIds, inflectionalCategories, ConstructName);
        }
        AuthorAlternateFormComposer.RequirePlacement(entry, placement, ConstructName);
        var signature = Signature(intent);
        foreach (var existing in entry.AlternateFormsOS.Prepend(entry.LexemeFormOA).OfType<IMoAffixProcess>())
            if (Signature(existing) == signature)
                throw new InvalidOperationException(
                    $"'{ConstructName}': entry '{entryId.Value}' already has a process with this pattern and output.");

        var source = mintId ?? (() => CanonicalId.Mint());
        CanonicalId Mint() => LexicalFormAuthoring.Normalize(source());
        var processId = Mint();
        var createId = Mint();
        var operations = new List<OperationEnvelope>
        {
            new(createId, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms, entityId: processId,
                target: entryId, placement: placement,
                after: JsonSerializer.SerializeToElement(new
                    { @class = "MoAffixProcess", morphType = CanonicalId.FromGuid(morphType.Guid).Value }),
                rationale: Rationale),
        };
        var creates = new Dictionary<CanonicalId, CanonicalId>(CanonicalId.ByGuid) { [processId] = createId };
        AffixProcessGraphBuilder.Append(operations, Mint, creates, processId, intent.Input, intent.Output, Rationale);
        foreach (var inflectionClass in classes)
            operations.Add(new OperationEnvelope(Mint(), MoAffixFormInflectionClassesOperationKinds.AddRefInflectionClasses,
                target: processId, after: JsonSerializer.SerializeToElement(new { member = inflectionClass.Value }),
                dependsOn: [new OperationDependency(createId)], rationale: Rationale));
        return operations;
    }

    private static string Signature(AuthorAffixProcessAllomorphIntent intent)
    {
        var names = intent.Input.Select((part, index) => (part.Name, index)).ToDictionary(pair => pair.Name, pair => pair.index);
        var input = intent.Input.Select(part => part.Remaining ? "V" : part.Phoneme is { } phone
            ? "S" + phone.ToGuid() : "N" + part.NaturalClass!.Value.ToGuid());
        var output = intent.Output.SelectMany(step => step.Copy is { } name
            ? new[] { "C" + names[name] } : step.Insert!.Select(phone => "I" + phone.ToGuid()));
        return string.Join(",", input) + ">" + string.Join(",", output);
    }

    private static string Signature(IMoAffixProcess process)
    {
        var positions = process.InputOS.Select((part, index) => (part.Guid, index)).ToDictionary(pair => pair.Guid, pair => pair.index);
        var input = process.InputOS.Select(part => part switch
        {
            IPhVariable => "V",
            IPhSimpleContextSeg { FeatureStructureRA: { } phone } => "S" + phone.Guid,
            IPhSimpleContextNC { FeatureStructureRA: { } naturalClass } => "N" + naturalClass.Guid,
            _ => "?",
        });
        var output = process.OutputOS.SelectMany(part => part switch
        {
            IMoCopyFromInput { ContentRA: { } content } when positions.TryGetValue(content.Guid, out var index) => new[] { "C" + index },
            IMoInsertPhones insert => insert.ContentRS.Select(phone => "I" + phone.Guid),
            _ => ["?"],
        });
        return string.Join(",", input) + ">" + string.Join(",", output);
    }
}
