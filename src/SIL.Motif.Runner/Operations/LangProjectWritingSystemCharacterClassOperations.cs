using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.WritingSystems;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the operation kind that moves named characters of one writing system between word-forming and
/// punctuation. It targets the project root and addresses one slot per writing system and character.
/// </summary>
/// <remarks>
/// <para>
/// The closed <c>after</c> payload is <c>{ "ws": tag, "class": "wordForming"|"punctuation", "characters":
/// [one character, ...] }</c>. Word-forming adds each character to the writing system's <c>main</c> character
/// set and removes it from the <c>punctuation</c> set; punctuation does the reverse, pinned by
/// `PunctuationMovesTheCharactersFromMainToThePunctuationSetAndBackAgain`. A letter, mark or modifier cannot be
/// made punctuation and is refused, pinned by `ALetterCannotBeMadePunctuation`.
/// </para>
/// <para>
/// The effect records, for each named character, the class the writing system gives it and the set that lists it,
/// read back after the write. Texts that hold a changed character are parsed again, pinned by
/// `TextsAreRetokenisedSoTheQuotesStopFormingWords`, when the character's effective class changed, pinned by
/// `ListingAnUnlistedNonLetterAsPunctuationParsesNothing`, and when any run of the paragraph is in the writing
/// system, pinned by `AParagraphWhoseFirstRunIsAnotherWritingSystemIsRetokenised`. The writing system sits outside the unit of work, so only
/// discarding the cache undoes the change.
/// </para>
/// </remarks>
public static class LangProjectWritingSystemOperationKinds
{
    public const string SetCharacterClass = "writingSystems/langProject/setCharacterClass";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(SetCharacterClass);
        OperationHandlerRegistry.Register(SetCharacterClass, WritingSystemCharacterClassHandler.Instance);
        OperationSlotRegistry.RegisterMany(SetCharacterClass, operation =>
        {
            var payload = WritingSystemCharacterClassPayload.Parse(OperationSlotRegistry.After(operation));
            return payload.Characters.Select(character => new OperationSlot(
                OperationSlotRegistry.Target(operation), SnapshotFields.LangProjectWritingSystemCharacterClass,
                $"{payload.WritingSystem}/{Notation(character)}")).ToArray();
        });
    }
#pragma warning restore CA2255

    internal static string Notation(int codePoint) => "U+" + codePoint.ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The class a character is given.</summary>
public enum WritingSystemCharacterClass
{
    /// <summary>The character is part of a word.</summary>
    WordForming,

    /// <summary>The character separates words.</summary>
    Punctuation,
}

/// <summary>The closed <c>after</c> payload of the set-character-class operation.</summary>
public sealed record WritingSystemCharacterClassPayload(
    string WritingSystem, WritingSystemCharacterClass Class, IReadOnlyList<int> Characters)
{
    /// <summary>The wire spelling of a class.</summary>
    public static string Name(WritingSystemCharacterClass value) =>
        value == WritingSystemCharacterClass.WordForming ? "wordForming" : "punctuation";

    /// <summary>Reads the payload, refusing an unknown property, class or a character that is not one visible character.</summary>
    public static WritingSystemCharacterClassPayload Parse(JsonElement after)
    {
        var kind = LangProjectWritingSystemOperationKinds.SetCharacterClass;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["ws", "class", "characters"], kind);
        var tag = ClosedPayloadParsing.GetRequiredString(after, "ws", kind);
        if (string.IsNullOrWhiteSpace(tag))
            throw new ContractParseException($"'{kind}' operation 'after.ws' must name a writing system.");
        var className = ClosedPayloadParsing.GetRequiredString(after, "class", kind);
        var characterClass = className switch
        {
            "wordForming" => WritingSystemCharacterClass.WordForming,
            "punctuation" => WritingSystemCharacterClass.Punctuation,
            _ => throw new ContractParseException(
                $"'{kind}' operation 'after.class' must be 'wordForming' or 'punctuation', not '{className}'."),
        };
        if (!after.TryGetProperty("characters", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{kind}' operation 'after.characters' is required and must be an array.");
        var characters = new SortedSet<int>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new ContractParseException($"'{kind}' operation 'after.characters' must hold strings.");
            characters.Add(OneVisibleCharacter(item.GetString()!, kind));
        }
        if (characters.Count == 0)
            throw new ContractParseException($"'{kind}' operation 'after.characters' must name at least one character.");
        return new WritingSystemCharacterClassPayload(tag, characterClass, characters.ToArray());
    }

    /// <summary>The one code point of <paramref name="text"/>, refusing anything that is not a single visible character.</summary>
    public static int OneVisibleCharacter(string text, string kind)
    {
        var runes = text.EnumerateRunes().ToArray();
        if (runes.Length != 1 || Rune.IsWhiteSpace(runes[0]) || Rune.IsControl(runes[0]))
            throw new ContractParseException(
                $"'{kind}': '{text}' is not one visible character; whitespace and control characters cannot be classed.");
        return runes[0].Value;
    }
}

/// <summary>Reads and edits the class of characters in a writing system's LDML <c>main</c> and <c>punctuation</c> sets.</summary>
internal static class WritingSystemCharacterSets
{
    private const string Main = "main";
    private const string PunctuationSet = "punctuation";

    /// <summary>One token per character: <c>U+2018=punctuation/punctuation</c>, effective class then the listing set.</summary>
    public static string Describe(CoreWritingSystemDefinition ws, IEnumerable<int> characters) =>
        string.Join(' ', characters.Select(codePoint =>
        {
            var text = char.ConvertFromUtf32(codePoint);
            var inMain = Lists(ws, Main, text);
            var inPunctuation = Lists(ws, PunctuationSet, text);
            var listing = inMain && inPunctuation ? "main+punctuation" : inMain ? "main" : inPunctuation ? "punctuation" : "unlisted";
            var effective = ws.get_IsWordForming(codePoint) ? "wordForming" : "punctuation";
            return $"{LangProjectWritingSystemOperationKinds.Notation(codePoint)}={effective}/{listing}";
        }));

    /// <summary>Moves each character to the set that gives it <paramref name="characterClass"/>; true when any set changed.</summary>
    public static bool Set(CoreWritingSystemDefinition ws, WritingSystemCharacterClass characterClass, IEnumerable<int> characters)
    {
        var changed = false;
        foreach (var codePoint in characters)
        {
            var text = char.ConvertFromUtf32(codePoint);
            var (add, remove) = characterClass == WritingSystemCharacterClass.WordForming
                ? (Main, PunctuationSet) : (PunctuationSet, Main);
            if (Lists(ws, remove, text)) { Remove(ws, remove, text); changed = true; }
            if (!Lists(ws, add, text)) { Add(ws, add, text); changed = true; }
        }
        return changed;
    }

    private static bool Lists(CoreWritingSystemDefinition ws, string set, string text) =>
        ws.CharacterSets.TryGet(set, out var found) && found.Characters.Contains(text);

    private static void Add(CoreWritingSystemDefinition ws, string set, string text)
    {
        if (!ws.CharacterSets.TryGet(set, out var found))
        {
            found = new CharacterSetDefinition(set);
            ws.CharacterSets.Add(found);
        }
        found.Characters.Add(text);
    }

    // An emptied set is dropped so the LDML carries no empty exemplar element.
    private static void Remove(CoreWritingSystemDefinition ws, string set, string text)
    {
        if (!ws.CharacterSets.TryGet(set, out var found)) return;
        found.Characters.Remove(text);
        if (found.Characters.Count == 0) ws.CharacterSets.Remove(set);
    }
}

internal sealed class WritingSystemCharacterClassHandler : IOperationHandler
{
    private const string Kind = LangProjectWritingSystemOperationKinds.SetCharacterClass;
    internal static readonly WritingSystemCharacterClassHandler Instance = new();
    private WritingSystemCharacterClassHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var after = operation.After ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'after'.");
        var payload = WritingSystemCharacterClassPayload.Parse(after);
        var (id, ws) = Resolve(cache, operation, payload);
        touchedTargets.Add(id);
        RequireChangeable(payload);

        var before = Alternatives(ws, payload);
        var effectiveBefore = payload.Characters.ToDictionary(codePoint => codePoint, ws.get_IsWordForming);
        WritingSystemCharacterSets.Set(ws, payload.Class, payload.Characters);
        // Only a changed effective class can change a segmentation, so listing an unlisted non-letter parses nothing.
        var reclassed = payload.Characters.Where(codePoint => ws.get_IsWordForming(codePoint) != effectiveBefore[codePoint]).ToArray();
        if (reclassed.Length > 0) Retokenise(cache, ws, reclassed);
        return new ExpectedEffect(id, SnapshotFields.LangProjectWritingSystemCharacterClass, before, Alternatives(ws, payload));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var payload = WritingSystemCharacterClassPayload.Parse(operation.After ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'after'."));
        var (id, ws) = Resolve(cache, operation, payload);
        var current = Alternatives(ws, payload);
        return new ExpectedEffect(id, SnapshotFields.LangProjectWritingSystemCharacterClass, current, current);
    }

    /// <summary>Refuses to class a letter, mark or modifier as punctuation, pinned by `ALetterCannotBeMadePunctuation`.</summary>
    internal static void RequireChangeable(WritingSystemCharacterClassPayload payload)
    {
        if (payload.Class != WritingSystemCharacterClass.Punctuation) return;
        foreach (var codePoint in payload.Characters.Where(TsStringUtils.IsWordForming))
            throw new InvalidOperationException(
                $"'{Kind}': {LangProjectWritingSystemOperationKinds.Notation(codePoint)} " +
                $"('{char.ConvertFromUtf32(codePoint)}') is a letter, mark or modifier by Unicode, so FieldWorks " +
                "keeps it word-forming whatever the writing system lists; it cannot be made punctuation.");
    }

    /// <summary>The project's writing system the payload names, or a refusal saying it is not one of them.</summary>
    internal static CoreWritingSystemDefinition Find(LcmCache cache, string tag) =>
        cache.ServiceLocator.WritingSystems.AllWritingSystems
            .FirstOrDefault(system => string.Equals(system.Id, tag, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"'{Kind}': writing system '{tag}' is not one of this project's writing systems.");

    private static IReadOnlyDictionary<string, string> Alternatives(CoreWritingSystemDefinition ws,
        WritingSystemCharacterClassPayload payload) =>
        new Dictionary<string, string> { [payload.WritingSystem] = WritingSystemCharacterSets.Describe(ws, payload.Characters) };

    private static (CanonicalId Id, CoreWritingSystemDefinition Ws) Resolve(
        LcmCache cache, OperationEnvelope operation, WritingSystemCharacterClassPayload payload)
    {
        var (id, project) = TargetResolution.Resolve<ILangProject>(cache, operation, Kind);
        if (project.Guid != cache.LangProject.Guid)
            throw new InvalidOperationException($"'{Kind}': a writing system is changed only in this project.");
        return (id, Find(cache, payload.WritingSystem));
    }

    // A paragraph can mix writing systems, so the first run alone does not say whether the changed one is used.
    private static bool HasRunIn(ITsString contents, int handle)
    {
        for (var run = 0; run < contents.RunCount; run++)
            if (contents.get_WritingSystem(run) == handle) return true;
        return false;
    }

    // FieldWorks segments a paragraph when it is parsed, so Texts keep the old words until they are parsed again.
    private static void Retokenise(LcmCache cache, CoreWritingSystemDefinition ws, IReadOnlyList<int> characters)
    {
        var needles = characters.Select(char.ConvertFromUtf32).ToArray();
        var paragraphs = cache.ServiceLocator.GetInstance<IStTxtParaRepository>().AllInstances()
            .Where(paragraph => paragraph.Contents is { Length: > 0 } contents &&
                HasRunIn(contents, ws.Handle) &&
                needles.Any(needle => contents.Text.Contains(needle, StringComparison.Ordinal)))
            .ToArray();
        if (paragraphs.Length == 0) return;
        using var parser = new ParagraphParser(cache);
        foreach (var paragraph in paragraphs) parser.ForceParse(paragraph);
    }
}
