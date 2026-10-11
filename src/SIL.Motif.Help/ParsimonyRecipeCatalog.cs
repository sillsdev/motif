using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Help;

/// <summary>Versioned metadata that describes one shared Parsimony Guide recipe.</summary>
/// <param name="MeasureId">The stable measure inventory ID this page explains.</param>
/// <param name="RecipeVersion">The version of the recipe's instructions.</param>
/// <param name="Title">The Guide title, limited to thirty characters.</param>
/// <param name="Axes">The closed review axes this recipe covers.</param>
/// <param name="AttachesTo">The closed identity category used by its findings.</param>
/// <param name="AuthoredObjectKind">The authored object type when the recipe attaches to an object.</param>
/// <param name="GroupKind">The group type when the recipe attaches to a group.</param>
/// <param name="EvidenceViews">The fixed named views the recipe reads.</param>
/// <param name="ShortTitle">The measure's name as a lowercase phrase, such as "broad allomorph conditions".</param>
/// <param name="RequiredCapabilities">The named data and authoring capabilities it needs.</param>
/// <param name="UpdateIntents">The semantic intents the recipe may propose.</param>
/// <param name="HelpPath">The path under the English Guide resource root.</param>
/// <param name="Sources">The recipe's distinct literature and contract sources.</param>
/// <param name="CitationKeys">The stable keys exposed by the measure catalog.</param>
public sealed record ParsimonyRecipeMetadata(
    string MeasureId,
    int RecipeVersion,
    string Title,
    string ShortTitle,
    IReadOnlyList<string> Axes,
    string AttachesTo,
    string? AuthoredObjectKind,
    string? GroupKind,
    IReadOnlyList<string> EvidenceViews,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> UpdateIntents,
    string HelpPath,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> CitationKeys)
{
    /// <summary>
    /// The stable Guide code used by Help and Parsimony findings: the measure id in lowercase, because the site
    /// publishes Guide pages at lowercase routes.
    /// </summary>
    public string RecipeLink => $"guide:parsimony/recipes/{MeasureId.ToLowerInvariant()}";
}

/// <summary>A recipe's versioned metadata and its exact embedded CommonMark page.</summary>
/// <param name="Metadata">The recipe metadata.</param>
/// <param name="Page">The page bytes decoded as UTF-8 text.</param>
public sealed record ParsimonyRecipe(ParsimonyRecipeMetadata Metadata, string Page)
{
    /// <summary>The stable measure inventory ID.</summary>
    public string MeasureId => Metadata.MeasureId;

    /// <summary>The short Guide title.</summary>
    public string Title => Metadata.Title;

    /// <summary>The measure's name as a plain phrase, for a list of checks; it never ends with a full stop.</summary>
    public string ShortTitle => Metadata.ShortTitle;

    /// <summary>The page path under the English Guide resource root.</summary>
    public string HelpPath => Metadata.HelpPath;

    /// <summary>The exact Guide code referenced by findings.</summary>
    public string RecipeLink => Metadata.RecipeLink;

    /// <summary>The named capabilities the recipe requires.</summary>
    public IReadOnlyList<string> RequiredCapabilities => Metadata.RequiredCapabilities;

    /// <summary>The source references recorded for this recipe.</summary>
    public IReadOnlyList<string> Sources => Metadata.Sources;
}

/// <summary>
/// Loads the recipes: Help owns each recipe's prose (titles, sources, update intents) and its page, and every
/// other field comes from the measure catalog, so a measure's metadata is edited in one place.
/// </summary>
public sealed class ParsimonyRecipeCatalog
{
    private const string ManifestResourceName = "help/en/guide/parsimony/parsimony-recipes.json";
    private const string RecipeResourcePrefix = "help/en/guide/parsimony/recipes/";
    private static readonly string[] RequiredSteps = ["Analyse", "Disposition", "Ask the linguist", "Update", "Verify"];
    private static readonly string[] RequiredHeadings = [.. RequiredSteps, "Grounding"];
    private readonly IReadOnlyDictionary<string, ParsimonyRecipe> _byMeasureId;

    private ParsimonyRecipeCatalog(int formatVersion, IReadOnlyList<ParsimonyRecipe> recipes)
    {
        FormatVersion = formatVersion;
        Recipes = recipes;
        _byMeasureId = recipes.ToDictionary(recipe => recipe.MeasureId, StringComparer.Ordinal);
    }

    /// <summary>The metadata format version.</summary>
    public int FormatVersion { get; }

    /// <summary>All recipes in stable measure-ID order.</summary>
    public IReadOnlyList<ParsimonyRecipe> Recipes { get; }

    /// <summary>Loads and validates the manifest and pages embedded in this Help assembly.</summary>
    public static ParsimonyRecipeCatalog Load()
    {
        var assembly = typeof(ParsimonyRecipeCatalog).Assembly;
        var manifest = ReadManifest(assembly);
        if (manifest.FormatVersion != 1)
            throw new InvalidDataException($"Unsupported Parsimony recipe format version {manifest.FormatVersion}.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var recipes = new List<ParsimonyRecipe>(manifest.Recipes.Count);
        foreach (var prose in manifest.Recipes)
        {
            var metadata = Combine(prose);
            ValidateMetadata(metadata, names, paths);
            var page = ReadResource(assembly, "help/en/" + metadata.HelpPath);
            ValidatePage(metadata, page);
            recipes.Add(new ParsimonyRecipe(metadata, page));
        }

        ValidateRecipePages(assembly, paths);
        var missing = MeasureCatalog.All.Select(measure => measure.Id).Except(names, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
            throw new InvalidDataException($"The recipe manifest has no recipe for measure(s): {string.Join(", ", missing)}.");
        return new ParsimonyRecipeCatalog(
            manifest.FormatVersion,
            Array.AsReadOnly(recipes.OrderBy(recipe => recipe.MeasureId, StringComparer.Ordinal).ToArray()));
    }

    /// <summary>Finds a recipe by its exact stable measure ID.</summary>
    /// <param name="measureId">The exact inventory ID.</param>
    public ParsimonyRecipe? Find(string measureId) => _byMeasureId.GetValueOrDefault(measureId);

    /// <summary>Returns the embedded page for an exact measure ID, or <see langword="null"/> when absent.</summary>
    /// <param name="measureId">The exact inventory ID.</param>
    public string? GetPage(string measureId) => Find(measureId)?.Page;

    private static RecipeManifest ReadManifest(Assembly assembly)
    {
        using var stream = ManifestResourceLookup.GetStream(assembly, ManifestResourceName)
            ?? throw new InvalidDataException($"Missing embedded recipe manifest '{ManifestResourceName}'.");
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        return JsonSerializer.Deserialize<RecipeManifest>(stream, options)
            ?? throw new InvalidDataException("The embedded Parsimony recipe manifest is empty.");
    }

    private static void ValidateMetadata(
        ParsimonyRecipeMetadata metadata,
        HashSet<string> measureIds,
        HashSet<string> paths)
    {
        if (string.IsNullOrWhiteSpace(metadata.MeasureId) || !measureIds.Add(metadata.MeasureId))
            throw new InvalidDataException($"The recipe manifest has a missing or duplicate measure ID '{metadata.MeasureId}'.");
        if (metadata.RecipeVersion < 1)
            throw new InvalidDataException($"Recipe '{metadata.MeasureId}' needs a positive recipe version.");
        if (string.IsNullOrWhiteSpace(metadata.Title) || metadata.Title.Length > 30)
            throw new InvalidDataException($"Recipe '{metadata.MeasureId}' needs a title of at most 30 characters.");
        ValidateDistinctNames(metadata.MeasureId, "update intent", metadata.UpdateIntents, IsCode);
        ValidateDistinctNames(metadata.MeasureId, "source", metadata.Sources, value => !string.IsNullOrWhiteSpace(value));
        if (metadata.Sources.Count == 0)
            throw new InvalidDataException($"Recipe '{metadata.MeasureId}' has an empty required metadata list.");

        var expectedPath = $"guide/parsimony/recipes/{metadata.MeasureId.ToLowerInvariant()}.md";
        if (!string.Equals(metadata.HelpPath, expectedPath, StringComparison.Ordinal) || !paths.Add(metadata.HelpPath))
            throw new InvalidDataException($"Recipe '{metadata.MeasureId}' has an invalid or duplicate Help path.");
    }

    private static void ValidatePage(ParsimonyRecipeMetadata metadata, string page)
    {
        if (!page.StartsWith("# " + metadata.Title + "\n", StringComparison.Ordinal))
            throw new InvalidDataException($"Recipe '{metadata.MeasureId}' page title does not match its metadata.");

        var headings = page.Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line[3..])
            .ToArray();
        if (!headings.SequenceEqual(RequiredHeadings, StringComparer.Ordinal))
            throw new InvalidDataException($"Recipe '{metadata.MeasureId}' must contain the ordered recipe steps and Grounding.");
        var grounding = page.LastIndexOf("## Grounding\n", StringComparison.Ordinal);
        if (grounding < 0 || page[(grounding + "## Grounding\n".Length)..].Trim().Length == 0)
            throw new InvalidDataException($"Recipe '{metadata.MeasureId}' needs a non-empty Grounding section.");
    }

    private static void ValidateRecipePages(Assembly assembly, HashSet<string> paths)
    {
        var pageNames = ManifestResourceLookup.Enumerate(assembly)
            .Select(resource => resource.LogicalName)
            .Where(name => name.StartsWith(RecipeResourcePrefix, StringComparison.Ordinal) &&
                           name.EndsWith(".md", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var expectedNames = paths.Select(path => "help/en/" + path).ToHashSet(StringComparer.Ordinal);
        if (!pageNames.SetEquals(expectedNames))
            throw new InvalidDataException("The embedded recipe pages do not match the recipe metadata manifest.");
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = ManifestResourceLookup.GetStream(assembly, name)
            ?? throw new InvalidDataException($"Missing embedded recipe page '{name}'.");
        using var reader = new StreamReader(stream);
        return ManifestResourceLookup.NormalizeLineEndings(reader.ReadToEnd());
    }

    private static void ValidateDistinctNames(
        string measureId,
        string kind,
        IReadOnlyList<string> values,
        Func<string, bool> isValid)
    {
        if (values.Any(value => !isValid(value)) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Count)
            throw new InvalidDataException($"Recipe '{measureId}' has an invalid or duplicate {kind}.");
    }

    private static bool IsCode(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.All(character =>
            character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-');

    private sealed class RecipeManifest
    {
        public required int FormatVersion { get; init; }
        public required IReadOnlyList<RecipeProse> Recipes { get; init; }
    }

    // The fields only Help owns; the catalog supplies the rest, so the manifest never repeats it.
    private sealed record RecipeProse(
        string MeasureId,
        int RecipeVersion,
        string Title,
        string ShortTitle,
        IReadOnlyList<string> UpdateIntents,
        IReadOnlyList<string> Sources);

    private static ParsimonyRecipeMetadata Combine(RecipeProse prose)
    {
        var measure = MeasureCatalog.Find(prose.MeasureId)
            ?? throw new InvalidDataException($"Recipe '{prose.MeasureId}' names no catalog measure.");
        return new ParsimonyRecipeMetadata(
            measure.Id,
            prose.RecipeVersion,
            prose.Title,
            prose.ShortTitle,
            measure.Axis == ParsimonyAxis.Both
                ? ["parsimony", "restrictiveness"]
                : [WireName(measure.Axis)],
            WireName(measure.AttachesTo),
            measure.AuthoredObjectKind is { } objectKind ? WireName(objectKind) : null,
            measure.GroupKind is { } groupKind ? WireName(groupKind) : null,
            measure.EvidenceViews,
            measure.RequiredCapabilities,
            prose.UpdateIntents,
            $"guide/parsimony/recipes/{measure.Id.ToLowerInvariant()}.md",
            prose.Sources,
            measure.CitationKeys);
    }

    private static string WireName<T>(T value) where T : struct, Enum =>
        JsonSerializer.Serialize(value).Trim('"');
}
