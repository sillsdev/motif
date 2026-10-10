using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    /// <summary>The stable Guide code used by Help and Parsimony findings.</summary>
    public string RecipeLink => $"guide:parsimony/recipes/{MeasureId}";
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

/// <summary>Loads the closed metadata manifest and embedded pages for the recipes.</summary>
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
        foreach (var metadata in manifest.Recipes)
        {
            ValidateMetadata(metadata, names, paths);
            var page = ReadResource(assembly, "help/en/" + metadata.HelpPath);
            ValidatePage(metadata, page);
            recipes.Add(new ParsimonyRecipe(metadata, page));
        }

        ValidateRecipePages(assembly, paths);
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
        ValidateDistinctNames(metadata.MeasureId, "axis", metadata.Axes, IsAxis);
        ValidateDistinctNames(metadata.MeasureId, "evidence view", metadata.EvidenceViews, IsCode);
        ValidateDistinctNames(metadata.MeasureId, "capability", metadata.RequiredCapabilities, IsCode);
        ValidateDistinctNames(metadata.MeasureId, "update intent", metadata.UpdateIntents, IsCode);
        ValidateDistinctNames(metadata.MeasureId, "source", metadata.Sources, value => !string.IsNullOrWhiteSpace(value));
        ValidateDistinctNames(metadata.MeasureId, "citation key", metadata.CitationKeys, IsCode);
        if (metadata.Axes.Count == 0 || metadata.EvidenceViews.Count == 0 ||
            metadata.RequiredCapabilities.Count == 0 || metadata.Sources.Count == 0 ||
            metadata.CitationKeys is null)
            throw new InvalidDataException($"Recipe '{metadata.MeasureId}' has an empty required metadata list.");
        if (!IsAttachment(metadata.AttachesTo))
            throw new InvalidDataException($"Recipe '{metadata.MeasureId}' has an unsupported attachment kind '{metadata.AttachesTo}'.");
        var isAuthoredObject = metadata.AttachesTo == "authored-object";
        var isGroup = metadata.AttachesTo == "group";
        if (isAuthoredObject != (metadata.AuthoredObjectKind is not null) ||
            isGroup != (metadata.GroupKind is not null))
            throw new InvalidDataException($"Recipe '{metadata.MeasureId}' has an inconsistent attachment type.");
        if (metadata.AuthoredObjectKind is not null && !IsAuthoredObjectKind(metadata.AuthoredObjectKind))
            throw new InvalidDataException($"Recipe '{metadata.MeasureId}' has an unsupported authored object type.");
        if (metadata.GroupKind is not null && !IsGroupKind(metadata.GroupKind))
            throw new InvalidDataException($"Recipe '{metadata.MeasureId}' has an unsupported group type.");

        var expectedPath = $"guide/parsimony/recipes/{metadata.MeasureId}.md";
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

    private static bool IsAxis(string value) =>
        value is "parsimony" or "restrictiveness" or "both";

    private static bool IsAttachment(string value) =>
        value is "project" or "group" or "authored-object" or "word-case";

    private static bool IsAuthoredObjectKind(string value) =>
        value is "adhoc-prohibition" or "inflectional-msa" or "affix-template" or "affix-slot" or
            "allomorph" or "natural-class";

    private static bool IsGroupKind(string value) =>
        value is "alternation-family" or "adhoc-slot-order" or "allomorph-duplicate-forms" or "unused-statement";

    private static bool IsCode(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.All(character =>
            character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-');

    private sealed class RecipeManifest
    {
        public required int FormatVersion { get; init; }
        public required IReadOnlyList<ParsimonyRecipeMetadata> Recipes { get; init; }
    }
}
