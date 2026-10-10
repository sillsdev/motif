using System.IO.Pipelines;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SIL.Motif.Help;
using SIL.Motif.Commands;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class EncodingGuidesTests
{
    [Fact]
    public void EveryGuideIsEmbeddedAndListedAsAResource()
    {
        var result = EncodingGuides.ListResources();
        var resources = result.Resources
            .Where(resource => resource.Uri.StartsWith(EncodingGuides.UriPrefix, StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(EncodingGuides.Topics, resources.Select(resource => resource.Name));
        Assert.All(resources, resource =>
        {
            Assert.Equal("text/markdown", resource.MimeType);
            var contents = Assert.IsType<TextResourceContents>(
                Assert.Single(EncodingGuides.ReadResource(resource.Uri).Contents));
            Assert.Equal(resource.Uri, contents.Uri);
            Assert.Equal("text/markdown", contents.MimeType);
            Assert.Equal(EncodingGuides.ReadTopic(resource.Name), contents.Text);
            Assert.StartsWith("# ", contents.Text);
        });
    }

    [Fact]
    public void EveryCatalogRecipeIsListedAndReadFromTheSharedHelpPage()
    {
        var recipes = ParsimonyRecipeCatalog.Load().Recipes;
        var resources = EncodingGuides.ListResources().Resources;
        var help = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));

        Assert.Equal(12, recipes.Count(recipe => recipe.MeasureId is
            "P-adhoc-duplicate" or "R-word-negative-accepted" or "R-word-disapproved-produced" or
            "B-affix-unslotted" or "R-tmpl-precedence" or "R-slot-blocking" or "R-allo-unconditioned" or
            "R-env-broad" or "R-nc-excess" or "P-allo-alternation-family" or "B-adhoc-is-slot-order" or
            "B-affix-null-vs-optional"));

        foreach (var recipe in recipes.Where(recipe => recipe.MeasureId is
                     "P-adhoc-duplicate" or "R-word-negative-accepted" or "R-word-disapproved-produced" or
                     "B-affix-unslotted" or "R-tmpl-precedence" or "R-slot-blocking" or "R-allo-unconditioned" or
                     "R-env-broad" or "R-nc-excess" or "P-allo-alternation-family" or "B-adhoc-is-slot-order" or
                     "B-affix-null-vs-optional"))
        {
            var uri = "motif://parsimony/recipes/" + recipe.MeasureId;
            Assert.Contains(resources, resource => resource.Uri == uri);

            var content = Assert.IsType<TextResourceContents>(
                Assert.Single(EncodingGuides.ReadResource(uri).Contents));
            Assert.Equal(recipe.Page, content.Text);
            Assert.Equal(recipe.Page,
                help.GetHelpPage(HelpEntryKind.Guide, $"parsimony/recipes/{recipe.MeasureId}"));
        }
    }

    [Fact]
    public async Task McpClientReadsARecipeFromTheSharedHelpCatalog()
    {
        const string measureId = "P-adhoc-duplicate";
        var recipe = ParsimonyRecipeCatalog.Load().Find(measureId)!;
        var uri = EncodingGuides.ParsimonyRecipeUriPrefix + measureId;
        var toServer = new Pipe();
        var fromServer = new Pipe();
        using var stop = new CancellationTokenSource();
        var server = Task.Run(() => MotifMcpServer.RunAsync(
            new McpLaunchOptions(AdvancedAiModeEnabled: true),
            toServer.Reader.AsStream(), fromServer.Writer.AsStream(), TextWriter.Null, stop.Token));
        var client = await McpClient.CreateAsync(
            new StreamClientTransport(toServer.Writer.AsStream(), fromServer.Reader.AsStream()));

        try
        {
            var resources = await client.ListResourcesAsync();
            Assert.Contains(resources, resource => resource.Uri == uri);
            var result = await client.ReadResourceAsync(uri);
            var content = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
            Assert.Equal(recipe.Page, content.Text);
        }
        finally
        {
            await client.DisposeAsync();
            stop.Cancel();
            try { await server.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException) { }
        }
    }

    [Fact]
    public async Task TheReadToolReturnsEveryListedGuide()
    {
        var context = new ServerContext("unused.fwdata", "1.0",
            new NoRunnerLauncher(new JobRunnerLaunchOptions("root", null)), new ActivityLog(null),
            ToolProfile.Builtin, TextWriter.Null);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);
        Assert.Contains(tools, tool => tool.Name == "motif_guide");

        foreach (var topic in EncodingGuides.Topics)
        {
            var arguments = new Dictionary<string, JsonElement>
            {
                ["topic"] = JsonSerializer.SerializeToElement(topic),
            };
            var result = await MotifMcpServer.CallAsync(context, tools, "motif_guide", arguments, CancellationToken.None);

            Assert.False(result.IsError == true);
            var value = result.StructuredContent!.Value.GetProperty("result");
            Assert.Equal(topic, value.GetProperty("topic").GetString());
            Assert.Equal(EncodingGuides.UriPrefix + topic, value.GetProperty("uri").GetString());
            Assert.Equal(EncodingGuides.ReadTopic(topic), value.GetProperty("guide").GetString());
        }
    }

    [Fact]
    public void TheWorkflowNamesOnlyToolsThatExist()
    {
        var workflow = EncodingGuides.ReadTopic("workflow");
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
            workflow, @"motif_[a-z_]+"))
            Assert.Contains(match.Value, AgentTools.Names);
        Assert.DoesNotContain("motif_finish_proposal", workflow);
    }

    [Fact]
    public void ServerInstructionsFitTheStandaloneBudget()
    {
        var firstParagraph = DefaultInstructions.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.InRange(firstParagraph.Length, 1, 512);
        Assert.Contains("do not invent affixes, allomorphs or example words", firstParagraph, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsimonyReferencesAreAvailableThroughTheSharedGuideResource()
    {
        Assert.Contains("parsimony-references", EncodingGuides.Topics);
        var guide = EncodingGuides.ReadTopic("parsimony-references");

        Assert.StartsWith("# Parsimony references\n", guide);
        Assert.Contains("**black-2018**", guide, StringComparison.Ordinal);
        Assert.Contains("**wexler-1993**", guide, StringComparison.Ordinal);
        Assert.Contains("**wexler-manzini-1987**", guide, StringComparison.Ordinal);
    }
}
