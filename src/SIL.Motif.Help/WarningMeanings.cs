using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace SIL.Motif.Help;

/// <summary>What one kind of grammar warning means, in the words a linguist uses.</summary>
/// <param name="Title">A short name for the kind of warning, shown as its group.</param>
/// <param name="Meaning">
/// One plain sentence saying what the warning means for the grammar and its words, or <see langword="null"/>
/// when Motif's table does not know the code, so the parser's own sentence has to stand alone.
/// </param>
/// <param name="IsKnown">Whether Motif's table has an entry for the code.</param>
public sealed record WarningMeaning(string Title, string? Meaning, bool IsKnown);

/// <summary>
/// Motif's table of plain meanings for PanGloss's grammar warnings, keyed by the warning's stable code and
/// read from the <c>warnings.json</c> Help file beside the glossary, so the meanings are translated the same
/// way. A code the table does not know keeps the parser's own group name, or a readable form of the code,
/// and never shows the code itself.
/// </summary>
public sealed class WarningMeanings
{
    private const string EnglishLocale = "en";
    private const string MissingCodeTitle = "Grammar warning";
    private const string NoSubjectKind = "Grammar-wide";
    private static readonly string[] ClassPrefixes = ["Lex", "Mo", "Ph", "Fs", "Cm"];
    private readonly IReadOnlyDictionary<string, (string Title, string Meaning)> _codes;
    private readonly IReadOnlyDictionary<string, string> _kinds;

    private WarningMeanings(
        IReadOnlyDictionary<string, (string Title, string Meaning)> codes, IReadOnlyDictionary<string, string> kinds)
    {
        _codes = codes;
        _kinds = kinds;
    }

    /// <summary>Every warning code the table has a meaning for, in ordinal order.</summary>
    public IReadOnlyList<string> Codes => [.. _codes.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Loads the table for a culture, taking English for each code or kind the culture lacks.</summary>
    /// <param name="culture">The requested culture, or the current UI culture when omitted.</param>
    public static WarningMeanings Load(CultureInfo? culture = null)
    {
        var locale = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;
        var (codes, kinds) = Read(EnglishLocale);
        if (!string.IsNullOrWhiteSpace(locale) && locale is not (EnglishLocale or "iv"))
        {
            var (localCodes, localKinds) = Read(locale);
            foreach (var (code, entry) in localCodes) codes[code] = entry;
            foreach (var (kind, label) in localKinds) kinds[kind] = label;
        }
        return new WarningMeanings(codes, kinds);
    }

    /// <summary>The title and meaning for <paramref name="code"/>.</summary>
    /// <param name="code">The warning's stable code, as the grammar check reported it.</param>
    /// <param name="parserGroupName">The parser's own name for the kind, used when the table lacks the code.</param>
    public WarningMeaning For(string? code, string? parserGroupName = null)
    {
        if (code is { } key && _codes.TryGetValue(key.Trim(), out var entry))
            return new WarningMeaning(entry.Title, entry.Meaning, IsKnown: true);
        var title = parserGroupName is { Length: > 0 } name && !string.IsNullOrWhiteSpace(name) ? name.Trim()
            : string.IsNullOrWhiteSpace(code) ? MissingCodeTitle
            : Humanise(LastSegment(code.Trim()));
        return new WarningMeaning(title, null, IsKnown: false);
    }

    /// <summary>
    /// The FieldWorks name for the kind of object a warning is about, such as "Allomorph" for <c>MoForm</c>,
    /// or "Grammar-wide" when the warning names no object.
    /// </summary>
    /// <param name="fieldWorksKind">The object's FieldWorks class as the parser reported it, in any casing.</param>
    public string KindLabel(string? fieldWorksKind)
    {
        if (string.IsNullOrWhiteSpace(fieldWorksKind)) return NoSubjectKind;
        var key = Normalise(fieldWorksKind);
        if (_kinds.TryGetValue(key, out var label)) return label;
        var words = Humanise(fieldWorksKind.Trim());
        var prefix = ClassPrefixes.FirstOrDefault(prefix =>
            fieldWorksKind.Length > prefix.Length && fieldWorksKind.StartsWith(prefix, StringComparison.Ordinal) &&
            char.IsUpper(fieldWorksKind[prefix.Length]));
        return prefix is null ? words : Humanise(fieldWorksKind[prefix.Length..]);
    }

    private static string LastSegment(string code)
    {
        var body = code[(code.LastIndexOf('.') + 1)..];
        return body.StartsWith("hc-", StringComparison.Ordinal) ? body[3..] : body;
    }

    // "some-new_check" and "SomeNewCheck" both read "Some new check".
    private static string Humanise(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var character in text)
        {
            if (character is '-' or '_' or ' ' || (char.IsUpper(character) && current.Length > 0))
            {
                if (current.Length > 0) words.Add(current.ToString());
                current.Clear();
                if (!char.IsUpper(character)) continue;
            }
            current.Append(character);
        }
        if (current.Length > 0) words.Add(current.ToString());
        if (words.Count == 0) return MissingCodeTitle;
        var sentence = string.Join(' ', words).ToLowerInvariant();
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    private static string Normalise(string kind) => kind.Replace("_", string.Empty).Trim().ToLowerInvariant();

    private static (Dictionary<string, (string Title, string Meaning)> Codes, Dictionary<string, string> Kinds) Read(
        string locale)
    {
        var codes = new Dictionary<string, (string Title, string Meaning)>(StringComparer.Ordinal);
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        var resource = ReadResource($"help/{locale}/warnings.json");
        if (resource is null) return (codes, kinds);

        using var document = JsonDocument.Parse(resource);
        var root = document.RootElement;
        if (root.TryGetProperty("codes", out var codeTable))
        {
            foreach (var property in codeTable.EnumerateObject())
            {
                var title = property.Value.GetProperty("title").GetString();
                var meaning = property.Value.GetProperty("meaning").GetString();
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(meaning))
                    throw new InvalidDataException($"The warning meaning for '{property.Name}' is incomplete.");
                codes.Add(property.Name, (title, meaning));
            }
        }
        if (root.TryGetProperty("kinds", out var kindTable))
        {
            foreach (var property in kindTable.EnumerateObject())
                kinds.Add(Normalise(property.Name), property.Value.GetString()
                    ?? throw new InvalidDataException($"The kind label for '{property.Name}' is missing."));
        }
        return (codes, kinds);
    }

    private static string? ReadResource(string logicalName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => string.Equals(name.Replace('\\', '/'), logicalName, StringComparison.Ordinal));
        if (resourceName is null) return null;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"The embedded Help resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
