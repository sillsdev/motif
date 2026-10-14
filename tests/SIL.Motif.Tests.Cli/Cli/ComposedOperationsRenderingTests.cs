using System.Text.Json;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ComposedOperationsRenderingTests
{
    [Fact]
    public void TextOutputNamesTheEntityEachCreateMints_SoALaterCommandCanReferToIt()
    {
        var response = new ComposedOperationsResponse("pos", "AuthorPartOfSpeech",
        [
            new OperationSummary("op-create", "lists/cmPossibilityList/createPossibilities", "new-category"),
            new OperationSummary("op-name", "lists/cmPossibility/setName"),
        ], 2);

        var output = ProposalCommandRenderer.Render(CommandOutcome<ComposedOperationsResponse>.Success(response),
            asJson: false).Output;

        Assert.Contains("op-create  (lists/cmPossibilityList/createPossibilities) creates new-category", output,
            StringComparison.Ordinal);
        Assert.Contains("op-name  (lists/cmPossibility/setName)" + Environment.NewLine, output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonOutputIncludesTypedCreatedObjects()
    {
        var response = new ComposedOperationsResponse("pos", "AuthorPartOfSpeech",
        [
            new OperationSummary("op-create", "lists/cmPossibilityList/createPossibilities", "new-category"),
        ], 1)
        {
            CreatedObjects = [new CreatedObject("new-category", "lists/cmPossibilityList/createPossibilities")],
        };

        var output = ProposalCommandRenderer.Render(CommandOutcome<ComposedOperationsResponse>.Success(response),
            asJson: true).Output;
        using var json = JsonDocument.Parse(output);
        var created = Assert.Single(json.RootElement.GetProperty("createdObjects").EnumerateArray());

        Assert.Equal("new-category", created.GetProperty("id").GetString());
        Assert.Equal("lists/cmPossibilityList/createPossibilities", created.GetProperty("kind").GetString());
    }
}
