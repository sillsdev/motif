using System;
using System.Globalization;
using System.Linq;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Help;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// The registries a Measure is spread across must agree: adding a Measure to the catalog fails here, by
/// name, until its runner, views and Help recipe exist.
/// </summary>
public sealed class ParsimonyRegistryInvariantTests
{
    [Fact]
    public void EveryCatalogMeasureHasARunnerOrDeclaresItselfNotRunnable()
    {
        Assert.All(MeasureCatalog.All, measure =>
        {
            if (measure.QueryId is null)
                Assert.False(MeasureRunner.Supports(measure.Id), measure.Id);
            else
                Assert.True(MeasureRunner.Supports(measure.Id), $"{measure.Id} has no runner for {measure.QueryId}");
        });
    }

    [Fact]
    public void EveryRunnerHasACatalogEntry()
    {
        var catalogQueries = MeasureCatalog.All.Where(measure => measure.QueryId is not null)
            .Select(measure => measure.QueryId!).Order(StringComparer.Ordinal);

        Assert.Equal(catalogQueries, MeasureRunner.RegisteredQueryIds.Order(StringComparer.Ordinal));
        Assert.Equal(ParsimonyQueryIds.All.Order(StringComparer.Ordinal),
            MeasureRunner.RegisteredQueryIds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryEvidenceViewAMeasureNamesExistsInTheViewCatalogAndTheViewsQuery()
    {
        var catalogViews = ParsimonyViewCatalog.All.Select(view => view.Code).ToHashSet(StringComparer.Ordinal);

        Assert.All(MeasureCatalog.All, measure => Assert.All(measure.EvidenceViews, view =>
        {
            Assert.True(catalogViews.Contains(view), $"{measure.Id} names view '{view}' absent from the catalog");
            Assert.True(ParsimonyViewsQuery.ImplementedViews.Contains(view),
                $"{measure.Id} names view '{view}' the views query cannot run");
        }));
    }

    [Fact]
    public void TheViewCatalogTheClosedCodesAndTheViewsQueryNameTheSameViews()
    {
        var catalogViews = ParsimonyViewCatalog.All.Select(view => view.Code).Order(StringComparer.Ordinal);

        Assert.Equal(catalogViews, ParsimonyViews.All.Order(StringComparer.Ordinal));
        Assert.Equal(catalogViews, ParsimonyViewsQuery.ImplementedViews.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryCapabilityAMeasureOrViewRequiresIsAClosedName()
    {
        Assert.All(MeasureCatalog.All, measure =>
            Assert.All(measure.RequiredCapabilities, name => Assert.Contains(name, ParsimonyCapabilities.All)));
        Assert.All(ParsimonyViewCatalog.All, view =>
            Assert.All(view.RequiredCapabilities, name => Assert.Contains(name, ParsimonyCapabilities.All)));
    }

    [Fact]
    public void EveryCatalogMeasureHasARecipeAndAHelpPageAndNothingElseDoes()
    {
        var recipes = ParsimonyRecipeCatalog.Load();
        var help = HelpCatalog.Load(CultureInfo.GetCultureInfo("en"));

        Assert.Equal(MeasureCatalog.All.Select(measure => measure.Id),
            recipes.Recipes.Select(recipe => recipe.MeasureId));
        Assert.All(MeasureCatalog.All, measure =>
        {
            var recipe = recipes.Find(measure.Id)!;
            Assert.Equal(recipe.Page, help.GetHelpPage(HelpEntryKind.Guide,
                $"parsimony/recipes/{measure.Id.ToLowerInvariant()}"));
            Assert.Equal(measure.RecipeLink, recipe.RecipeLink);
        });
    }

    [Fact]
    public void RecipeMetadataIsReadFromTheCatalog()
    {
        var recipes = ParsimonyRecipeCatalog.Load();

        Assert.All(MeasureCatalog.All, measure =>
        {
            var metadata = recipes.Find(measure.Id)!.Metadata;
            Assert.Equal(measure.RequiredCapabilities, metadata.RequiredCapabilities);
            Assert.Equal(measure.EvidenceViews, metadata.EvidenceViews);
            Assert.Equal(measure.CitationKeys, metadata.CitationKeys);
        });
        Assert.Equal(["parsimony", "restrictiveness"], recipes.Find("B-affix-unslotted")!.Metadata.Axes);
        Assert.Equal(["restrictiveness"], recipes.Find("R-env-broad")!.Metadata.Axes);
        Assert.Equal("word-case", recipes.Find("R-word-negative-accepted")!.Metadata.AttachesTo);
    }
}
