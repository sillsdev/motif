using SIL.Motif.Commands.Catalog;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class ApprovedReadingsSchemaTests
{
    [Fact]
    public void TheCorpusFormsRequestNamesTheApprovedFileInSnakeCaseWithoutAddingATool()
    {
        var command = CommandCatalog.All.Single(item => item.Name == "query corpus forms");
        Assert.False(command.ExposeAsAgentTool);
        var schema = CatalogTools.Prepare(CatalogTools.Create(command)).InputSchema;
        Assert.NotNull(schema["properties"]!["approved"]);
        Assert.DoesNotContain(AgentTools.All, tool => tool.Requests.Contains(command.RequestType));
    }
}
