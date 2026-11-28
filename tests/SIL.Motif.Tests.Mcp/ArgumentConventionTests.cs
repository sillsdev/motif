using System.Text.RegularExpressions;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

/// <summary>
/// The agent API names every tool argument in snake_case (epic #572 item 6, issue #567), so an agent
/// never has to guess between <c>reportId</c> and <c>report_id</c>. The test reads every tool the server can
/// offer, so a new tool or argument that breaks the convention fails here.
/// </summary>
[Trait("MotifTestLevel", "Unit")]
public sealed class ArgumentConventionTests
{
    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    [Fact]
    public void EveryToolArgumentIsSnakeCase()
    {
        var offenders = AgentTools.All
            .SelectMany(tool => (tool.InputSchema["properties"]?.AsObject() ?? []).Select(property => (tool.Name, property.Key)))
            .Where(argument => !SnakeCase.IsMatch(argument.Key))
            .Select(argument => $"{argument.Name}.{argument.Key}").Order(StringComparer.Ordinal).ToArray();
        Assert.True(offenders.Length == 0, "Arguments not in snake_case: " + string.Join(", ", offenders));
    }

    private static readonly string[] RetiredPagingArguments =
        ["page", "page_size", "pageSize", "offset", "occurrence_offset", "occurrence_limit", "continuation", "next", "after_cursor"];

    [Fact]
    public void NoToolExposesAnOldPagingArgument()
    {
        var offenders = AgentTools.All
            .SelectMany(tool => PropertyNames(tool.InputSchema).Select(name => (tool.Name, name)))
            .Where(argument => RetiredPagingArguments.Contains(argument.name, StringComparer.Ordinal))
            .Select(argument => $"{argument.Name}.{argument.name}").Order(StringComparer.Ordinal).ToArray();
        Assert.True(offenders.Length == 0, "Old paging arguments: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryToolThatTakesACursorAlsoTakesALimit()
    {
        var offenders = AgentTools.All
            .Where(tool => PropertyNames(tool.InputSchema).Contains("cursor", StringComparer.Ordinal)
                && !PropertyNames(tool.InputSchema).Contains("limit", StringComparer.Ordinal))
            .Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.True(offenders.Length == 0, "Tools with a cursor but no limit: " + string.Join(", ", offenders));
    }

    private static IEnumerable<string> PropertyNames(System.Text.Json.Nodes.JsonNode? schema)
    {
        if (schema is not System.Text.Json.Nodes.JsonObject node) yield break;
        if (node["properties"] is System.Text.Json.Nodes.JsonObject properties)
            foreach (var property in properties)
            {
                yield return property.Key;
                foreach (var nested in PropertyNames(property.Value)) yield return nested;
            }
        foreach (var nested in PropertyNames(node["items"])) yield return nested;
    }
}
