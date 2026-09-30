using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SIL.Motif.Help;

/// <summary>The type of item described by the shared Help content.</summary>
public enum HelpEntryKind
{
    /// <summary>A released command available from the CLI.</summary>
    Command,

    /// <summary>A user interface control identified by its AutomationId.</summary>
    Ui,

    /// <summary>A product term defined in the shared glossary.</summary>
    Term,

    /// <summary>A user Guide page, including nested agent and Learn pages.</summary>
    Guide,
}

/// <summary>A localized Help entry with its stable code, presentation fields, optional page, and online route.</summary>
/// <param name="Kind">The kind of command, interface control, glossary term, or Guide.</param>
/// <param name="Code">The stable item code; Guide codes retain their slash-separated hierarchy.</param>
/// <param name="Slug">The route slug; Guide slugs retain their slash-separated hierarchy.</param>
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

/// <summary>Loads embedded Motif Help content and resolves its stable cross-links and documentation URLs.</summary>
public sealed class HelpCatalog
{
    private const string EnglishLocale = "en";
    private const string SiteRootValue = "https://motif-docs.pages.dev";
    private static readonly Regex HelpLinkPattern = new(
        @"\]\((?<kind>cmd|term|ui|guide):(?<code>[^)]+)\)", RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownInlineLinkPattern = new(
        @"\[(?<text>[^\]]+)\]\([^)]+\)", RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownMarksPattern = new(@"[*_`]", RegexOptions.CultureInvariant);
    private readonly Dictionary<(HelpEntryKind Kind, string Code), HelpEntry> _byCode;

    private HelpCatalog(string locale, IReadOnlyList<HelpEntry> entries)
    {
        Locale = locale;
        Entries = entries;
        _byCode = entries.ToDictionary(entry => (entry.Kind, entry.Code));
    }

    /// <summary>The root used for links to the Motif documentation site.</summary>
    public static string SiteRoot => SiteRootValue;

    /// <summary>The selected two-letter locale code.</summary>
    public string Locale { get; }

    /// <summary>All command, interface, glossary, and Guide entries in stable kind and code order.</summary>
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
            if (kind == HelpEntryKind.Guide)
            {
                var localizedGuides = locale == EnglishLocale
                    ? Array.Empty<KeyValuePair<string, string>>()
                    : ReadGuidePages(locale);
                entries.AddRange(BuildGuideEntries(locale, ReadGuidePages(EnglishLocale), localizedGuides));
                continue;
            }

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
    /// <param name="code">The exact command name, AutomationId, glossary code, or hierarchical Guide code.</param>
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

    /// <summary>Returns one diagnostic for each unresolved cmd, term, ui, or guide link in a Help page.</summary>
    public IReadOnlyList<string> ValidateLinks() => ValidateLinks(Entries);

    /// <summary>Builds Guide entries from English pages and any available localized pages.</summary>
    /// <param name="locale">The locale used to build online routes.</param>
    /// <param name="englishPages">The canonical Guide pages keyed by their hierarchical codes.</param>
    /// <param name="localizedPages">Translated Guide pages keyed by their hierarchical codes.</param>
    internal static IReadOnlyList<HelpEntry> BuildGuideEntries(
        string locale,
        IEnumerable<KeyValuePair<string, string>> englishPages,
        IEnumerable<KeyValuePair<string, string>> localizedPages)
    {
        var english = IndexGuidePages(englishPages, "English");
        if (english.Count == 0)
            throw new InvalidDataException("No English Guide pages are embedded in Help.");

        var localized = IndexGuidePages(localizedPages, locale);
        var entries = new List<HelpEntry>(english.Count);
        foreach (var (code, englishPage) in english.OrderBy(page => page.Key, StringComparer.Ordinal))
        {
            var englishMetadata = ReadGuideMetadata(englishPage);
            if (englishMetadata.Title is null || englishMetadata.Description is null)
                throw new InvalidDataException($"The English Guide page '{code}' needs a title and opening paragraph.");

            localized.TryGetValue(code, out var localizedPage);
            var localizedMetadata = localizedPage is null
                ? new GuideMetadata(null, null)
                : ReadGuideMetadata(localizedPage);
            var title = localizedMetadata.Title ?? englishMetadata.Title;
            var description = localizedMetadata.Description ?? englishMetadata.Description;
            var page = localizedPage ?? englishPage;
            var slug = code;
            entries.Add(new HelpEntry(
                HelpEntryKind.Guide,
                code,
                slug,
                title,
                description,
                "Guide Markdown",
                page,
                BuildUrl(locale, HelpEntryKind.Guide, slug)));
        }

        return entries;
    }

    /// <summary>Validates Help links against a supplied set of entries.</summary>
    /// <param name="entries">The entries that provide valid link targets.</param>
    internal static IReadOnlyList<string> ValidateLinks(IEnumerable<HelpEntry> entries)
    {
        var allEntries = entries.ToArray();
        var byCode = allEntries.ToDictionary(entry => (entry.Kind, entry.Code));
        var errors = new List<string>();
        foreach (var entry in allEntries.Where(entry => entry.HelpPage is not null))
        {
            foreach (Match match in HelpLinkPattern.Matches(entry.HelpPage!))
            {
                var kind = ParseKind(match.Groups["kind"].Value);
                var code = Uri.UnescapeDataString(match.Groups["code"].Value);
                if (!byCode.ContainsKey((kind, code)))
                    errors.Add($"{KindCode(entry.Kind)} '{entry.Code}' links to missing {KindCode(kind)} '{code}'.");
            }
        }

        return errors;
    }

    private static Dictionary<string, string> IndexGuidePages(
        IEnumerable<KeyValuePair<string, string>> pages,
        string locale)
    {
        var indexed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (rawCode, markdown) in pages)
        {
            var code = rawCode.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(code) || !indexed.TryAdd(code, markdown))
                throw new InvalidDataException($"Guide code '{code}' is empty or duplicated in locale '{locale}'.");
        }

        return indexed;
    }

    private static KeyValuePair<string, string>[] ReadGuidePages(string locale)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = $"help/{locale}/guide/";
        return assembly.GetManifestResourceNames()
            .Select(name => (Name: name, LogicalName: name.Replace('\\', '/')))
            .Where(resource => resource.LogicalName.StartsWith(prefix, StringComparison.Ordinal)
                && resource.LogicalName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(resource => resource.LogicalName, StringComparer.Ordinal)
            .Select(resource => new KeyValuePair<string, string>(
                resource.LogicalName[prefix.Length..^3],
                ReadResource(assembly, resource.Name)))
            .ToArray();
    }

    private static GuideMetadata ReadGuideMetadata(string markdown)
    {
        var lines = markdown.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var titleIndex = Array.FindIndex(lines, line => line.StartsWith("# ", StringComparison.Ordinal));
        var title = titleIndex < 0 ? null : lines[titleIndex][2..].Trim();
        var paragraphIndex = titleIndex < 0 ? 0 : titleIndex + 1;
        while (paragraphIndex < lines.Length
            && (string.IsNullOrWhiteSpace(lines[paragraphIndex])
                || lines[paragraphIndex].StartsWith('#')))
            paragraphIndex++;

        var paragraph = lines.Skip(paragraphIndex).TakeWhile(line => !string.IsNullOrWhiteSpace(line));
        var description = string.Join(" ", paragraph).Trim();
        description = MarkdownInlineLinkPattern.Replace(description, "${text}");
        description = MarkdownMarksPattern.Replace(description, string.Empty);
        return new GuideMetadata(
            string.IsNullOrWhiteSpace(title) ? null : title,
            string.IsNullOrWhiteSpace(description) ? null : description);
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
        return resourceName is null ? null : ReadResource(assembly, resourceName);
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"The embedded Help resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Builds a locale-prefixed documentation route for a Help entry.</summary>
    /// <param name="locale">The two-letter locale code.</param>
    /// <param name="kind">The entry category.</param>
    /// <param name="slug">The entry slug, including its Guide hierarchy when applicable.</param>
    internal static string BuildUrl(string locale, HelpEntryKind kind, string slug)
    {
        var prefix = SiteRoot + LocalePrefix(locale);
        if (kind == HelpEntryKind.Guide)
        {
            if (slug == "learn/index")
                return prefix + "/learn/";
            if (slug.StartsWith("learn/", StringComparison.Ordinal))
                return prefix + "/learn/" + slug["learn/".Length..] + "/";
            return prefix + "/guide/" + slug + "/";
        }

        var section = kind switch
        {
            HelpEntryKind.Command => "commands",
            HelpEntryKind.Ui => "controls",
            HelpEntryKind.Term => "terms",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return prefix + "/reference/" + section + "/" + slug + "/";
    }

    private static string LocalePrefix(string locale) => locale == EnglishLocale ? string.Empty : "/" + locale;

    private static string KindPlural(HelpEntryKind kind) => kind switch
    {
        HelpEntryKind.Command => "commands",
        HelpEntryKind.Ui => "ui",
        HelpEntryKind.Term => "terms",
        HelpEntryKind.Guide => "guide",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string KindFolder(HelpEntryKind kind) => kind switch
    {
        HelpEntryKind.Command => "commands",
        HelpEntryKind.Ui => "ui",
        HelpEntryKind.Term => "terms",
        HelpEntryKind.Guide => "guide",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string KindCode(HelpEntryKind kind) => kind switch
    {
        HelpEntryKind.Command => "command",
        HelpEntryKind.Ui => "ui",
        HelpEntryKind.Term => "term",
        HelpEntryKind.Guide => "guide",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static HelpEntryKind ParseKind(string code) => code switch
    {
        "cmd" => HelpEntryKind.Command,
        "ui" => HelpEntryKind.Ui,
        "term" => HelpEntryKind.Term,
        "guide" => HelpEntryKind.Guide,
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };

    private static string CreateSlug(string code)
    {
        var slug = Regex.Replace(code.ToLowerInvariant(), @"[^a-z0-9]+", "-");
        return slug.Trim('-');
    }

    private sealed record Metadata(string? Title, string? Description, string? Source);

    private sealed record GuideMetadata(string? Title, string? Description);
}
