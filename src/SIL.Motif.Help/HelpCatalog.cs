using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SIL.Motif.Help;

/// <summary>The type of item described by the shared help content.</summary>
public enum HelpEntryKind
{
    /// <summary>A released command available from the CLI.</summary>
    Command,

    /// <summary>A user interface control identified by its AutomationId.</summary>
    Ui,

    /// <summary>A product term defined in the shared glossary.</summary>
    Term,
}

/// <summary>A localized help entry with its stable code, presentation fields, optional page, and online route.</summary>
/// <param name="Kind">The kind of command, interface control, or glossary term.</param>
/// <param name="Code">The stable command name, AutomationId, or glossary code.</param>
/// <param name="Slug">The route-safe slug derived from <paramref name="Code"/>.</param>
/// <param name="Title">The short human-readable heading.</param>
/// <param name="Description">The short explanation shown in lists and summaries.</param>
/// <param name="Source">The recorded origin of the localized prose.</param>
/// <param name="HelpPage">The optional full Markdown page.</param>
/// <param name="Url">The online documentation route for this entry.</param>
public sealed record HelpEntry(
    HelpEntryKind Kind,
    string Code,
    string Slug,
    string Title,
    string Description,
    string Source,
    string? HelpPage,
    string Url);

/// <summary>Loads embedded Motif help content and resolves its stable cross-links and documentation URLs.</summary>
public sealed class HelpCatalog
{
    private const string EnglishLocale = "en";
    private const string SiteRootValue = "https://motif-docs.pages.dev";
    private static readonly Regex HelpLinkPattern = new(
        @"\]\((?<kind>cmd|term|ui):(?<code>[^)]+)\)", RegexOptions.CultureInvariant);
    private readonly Dictionary<(HelpEntryKind Kind, string Code), HelpEntry> _byCode;

    private HelpCatalog(string locale, IReadOnlyList<HelpEntry> entries)
    {
        Locale = locale;
        Entries = entries;
        _byCode = entries.ToDictionary(entry => (entry.Kind, entry.Code));
    }

    /// <summary>The placeholder root used for links to the Motif documentation site.</summary>
    public static string SiteRoot => SiteRootValue;

    /// <summary>The selected two-letter locale code.</summary>
    public string Locale { get; }

    /// <summary>All command, interface, and glossary entries in stable kind and code order.</summary>
    public IReadOnlyList<HelpEntry> Entries { get; }

    /// <summary>Loads the selected culture, using English for each missing metadata field and page.</summary>
    /// <param name="culture">The requested culture, or the current UI culture when omitted.</param>
    public static HelpCatalog Load(CultureInfo? culture = null)
    {
        var selectedCulture = culture ?? CultureInfo.CurrentUICulture;
        var locale = selectedCulture.TwoLetterISOLanguageName;
        if (string.IsNullOrWhiteSpace(locale) || locale == "iv")
            locale = EnglishLocale;

        var entries = new List<HelpEntry>();
        foreach (var kind in Enum.GetValues<HelpEntryKind>())
        {
            var english = ReadMetadata(EnglishLocale, kind);
            var localized = locale == EnglishLocale ? new Dictionary<string, Metadata>() : ReadMetadata(locale, kind);
            foreach (var code in english.Keys.Concat(localized.Keys).Distinct(StringComparer.Ordinal)
                         .Order(StringComparer.Ordinal))
            {
                english.TryGetValue(code, out var englishMetadata);
                localized.TryGetValue(code, out var localizedMetadata);
                var title = localizedMetadata?.Title ?? englishMetadata?.Title;
                var description = localizedMetadata?.Description ?? englishMetadata?.Description;
                var source = localizedMetadata?.Source ?? englishMetadata?.Source;
                if (title is null || description is null || source is null)
                    throw new InvalidDataException($"Help metadata for '{code}' is incomplete.");

                var page = ReadPage(locale, kind, code) ?? ReadPage(EnglishLocale, kind, code);
                var slug = CreateSlug(code);
                entries.Add(new HelpEntry(
                    kind, code, slug, title, description, source, page, BuildUrl(locale, kind, slug)));
            }
        }

        return new HelpCatalog(locale, entries);
    }

    /// <summary>Finds an entry by its kind and exact code.</summary>
    /// <param name="kind">The entry category.</param>
    /// <param name="code">The exact command name, AutomationId, or glossary code.</param>
    public HelpEntry? Find(HelpEntryKind kind, string code) =>
        _byCode.GetValueOrDefault((kind, code));

    /// <summary>Returns the page Markdown for an entry, or <see langword="null"/> when it has no page.</summary>
    /// <param name="kind">The entry category.</param>
    /// <param name="code">The exact entry code.</param>
    public string? GetHelpPage(HelpEntryKind kind, string code) => Find(kind, code)?.HelpPage;

    /// <summary>Builds the online URL for an existing entry.</summary>
    /// <param name="kind">The entry category.</param>
    /// <param name="code">The exact entry code.</param>
    public string BuildUrl(HelpEntryKind kind, string code) =>
        Find(kind, code)?.Url ?? throw new ArgumentException($"No {KindCode(kind)} entry has code '{code}'.", nameof(code));

    /// <summary>Builds the online URL for a Walkthrough.</summary>
    /// <param name="id">The stable Walkthrough id.</param>
    public string BuildWalkthroughUrl(string id) =>
        SiteRoot + LocalePrefix(Locale) + "/guide/walkthroughs/" + Uri.EscapeDataString(id) + "/";

    /// <summary>Returns one diagnostic for each unresolved cmd, term, or ui link in a help page.</summary>
    public IReadOnlyList<string> ValidateLinks()
    {
        var errors = new List<string>();
        foreach (var entry in Entries.Where(entry => entry.HelpPage is not null))
        {
            foreach (Match match in HelpLinkPattern.Matches(entry.HelpPage!))
            {
                var kind = ParseKind(match.Groups["kind"].Value);
                var code = Uri.UnescapeDataString(match.Groups["code"].Value);
                if (Find(kind, code) is null)
                    errors.Add($"{KindCode(entry.Kind)} '{entry.Code}' links to missing {KindCode(kind)} '{code}'.");
            }
        }

        return errors;
    }

    private static Dictionary<string, Metadata> ReadMetadata(string locale, HelpEntryKind kind)
    {
        var resource = ReadResource($"help/{locale}/{KindPlural(kind)}.json");
        if (resource is null)
            return new Dictionary<string, Metadata>(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(resource);
        var result = new Dictionary<string, Metadata>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var value = property.Value;
            result.Add(property.Name, new Metadata(
                value.GetProperty("title").GetString(),
                value.GetProperty("description").GetString(),
                value.GetProperty("source").GetString()));
        }

        return result;
    }

    private static string? ReadPage(string locale, HelpEntryKind kind, string code) =>
        ReadResource($"help/{locale}/{KindFolder(kind)}/{CreateSlug(code)}.md");

    private static string? ReadResource(string logicalName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => string.Equals(
                name.Replace('\\', '/'), logicalName, StringComparison.Ordinal));
        if (resourceName is null)
            return null;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string BuildUrl(string locale, HelpEntryKind kind, string slug)
    {
        var section = kind switch
        {
            HelpEntryKind.Command => "commands",
            HelpEntryKind.Ui => "ui",
            HelpEntryKind.Term => "terms",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return SiteRoot + LocalePrefix(locale) + "/reference/" + section + "/" + slug + "/";
    }

    private static string LocalePrefix(string locale) => locale == EnglishLocale ? string.Empty : "/" + locale;

    private static string KindPlural(HelpEntryKind kind) => kind switch
    {
        HelpEntryKind.Command => "commands",
        HelpEntryKind.Ui => "ui",
        HelpEntryKind.Term => "terms",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string KindFolder(HelpEntryKind kind) => kind switch
    {
        HelpEntryKind.Command => "commands",
        HelpEntryKind.Ui => "ui",
        HelpEntryKind.Term => "terms",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string KindCode(HelpEntryKind kind) => kind switch
    {
        HelpEntryKind.Command => "command",
        HelpEntryKind.Ui => "ui",
        HelpEntryKind.Term => "term",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static HelpEntryKind ParseKind(string code) => code switch
    {
        "cmd" => HelpEntryKind.Command,
        "ui" => HelpEntryKind.Ui,
        "term" => HelpEntryKind.Term,
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };

    private static string CreateSlug(string code)
    {
        var slug = Regex.Replace(code.ToLowerInvariant(), @"[^a-z0-9]+", "-");
        return slug.Trim('-');
    }

    private sealed record Metadata(string? Title, string? Description, string? Source);
}
