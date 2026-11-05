using SIL.Motif.Cli;
using SIL.Motif.Commands.Catalog;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class CliCommandNamesTests
{
    [Theory]
    [InlineData("add", "proposal comment add")]
    [InlineData("edit", "proposal comment edit")]
    [InlineData("delete", "proposal comment delete")]
    public void ACommentVerbResolvesToItsCatalogCommandSoTheSurfaceGateApplies(string action, string expected)
    {
        var name = CliCommandNames.Resolve("proposal", new Dictionary<string, string>(), ["comment", action, "p/one"]);

        Assert.Equal(expected, name);
        Assert.Contains(CommandCatalog.All, command => command.Name == name);
    }
}
