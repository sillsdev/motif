using ModelContextProtocol.Protocol;
using SIL.Motif.Help;

namespace SIL.Motif.Mcp;

internal static class EncodingGuides
{
    internal const string UriPrefix = "motif://guides/";
    internal const string ParsimonyRecipeUriPrefix = "motif://parsimony/recipes/";

    private static readonly Guide[] Guides =
    [
        new("suffix-prefix-slots", "Prefix and suffix slots",
            "Encode affixes in ordered template slots and check required positions.",
            "suffix-prefix-slots.md"),
        new("conditioned-allomorphs", "Phonologically conditioned allomorphs",
            "Encode conditioned forms under one morpheme when the evidence supports it.",
            "conditioned-allomorphs.md"),
        new("feature-conditioned-slots", "Feature-conditioned template slots",
            "Use grammatical feature values to restrict which affix fills a slot.",
            "feature-conditioned-slots.md"),
        new("nasal-assimilation", "Segmental nasal assimilation",
            "Encode a supported nasal sound change as a phonological rule.",
            "nasal-assimilation.md"),
        new("no-inflection", "A supported finding of no inflection",
            "Avoid inventing affixes when comparable forms show no inflection.",
            "no-inflection.md"),
        new("workflow", "Work through one grammar question",
            "Inspect the project, make a small Proposal, run a Trial and hand off the evidence.",
            "workflow.md"),
        new("ask-the-linguist", "Ask the linguist when evidence runs out",
            "Choose when to guess, ask one evidence-backed question or defer.",
            "ask-the-linguist.md"),
        new("parsimony-references", "Parsimony references",
            "Resolve the citation keys used by Parsimony measures to their full source citations.",
            null),
    ];

    internal static IReadOnlyList<string> Topics { get; } = Guides.Select(guide => guide.Topic).ToArray();

    internal static IReadOnlyList<Resource> Resources { get; } = Guides.Select(guide => new Resource
        {
            Uri = UriPrefix + guide.Topic,
            Name = guide.Topic,
            Title = guide.Title,
            Description = guide.Description,
            MimeType = "text/markdown",
        })
        .Concat(ParsimonyRecipeCatalog.Load().Recipes.Select(recipe => new Resource
        {
            Uri = ParsimonyRecipeUriPrefix + recipe.MeasureId,
            Name = recipe.MeasureId,
            Title = recipe.Title,
            Description = $"Parsimony recipe for measure {recipe.MeasureId}.",
            MimeType = "text/markdown",
        }))
        .ToArray();

    internal static ListResourcesResult ListResources() => new() { Resources = Resources.ToList() };

    internal static ReadResourceResult ReadResource(string uri)
    {
        if (uri.StartsWith(ParsimonyRecipeUriPrefix, StringComparison.Ordinal))
        {
            var measureId = uri[ParsimonyRecipeUriPrefix.Length..];
            var recipe = ParsimonyRecipeCatalog.Load().Find(measureId)
                ?? throw new InvalidOperationException($"There is no recipe resource at '{uri}'.");
            return new ReadResourceResult
            {
                Contents =
                [
                    new TextResourceContents
                    {
                        Uri = uri,
                        MimeType = "text/markdown",
                        Text = recipe.Page,
                    },
                ],
            };
        }

        var guide = Guides.SingleOrDefault(candidate => UriPrefix + candidate.Topic == uri)
            ?? throw new InvalidOperationException($"There is no guide resource at '{uri}'.");
        return new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = uri,
                    MimeType = "text/markdown",
                    Text = ReadText(guide),
                },
            ],
        };
    }

    internal static string ReadTopic(string topic)
    {
        var guide = Guides.SingleOrDefault(candidate => candidate.Topic == topic)
            ?? throw new ArgumentOutOfRangeException(nameof(topic), topic, "Choose one of the listed guide topics.");
        return ReadText(guide);
    }

    private static string ReadText(Guide guide)
    {
        if (guide.FileName is null) return ParsimonyReferenceCatalog.Load().ToMarkdown();
        using var stream = ManifestResourceLookup.GetStream(typeof(EncodingGuides).Assembly,
            "motif-guides/" + guide.FileName)
            ?? throw new InvalidOperationException($"The embedded guide '{guide.Topic}' is missing.");
        using var reader = new StreamReader(stream);
        return ManifestResourceLookup.NormalizeLineEndings(reader.ReadToEnd());
    }

    private sealed record Guide(string Topic, string Title, string Description, string? FileName);
}
