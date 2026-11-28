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
}
