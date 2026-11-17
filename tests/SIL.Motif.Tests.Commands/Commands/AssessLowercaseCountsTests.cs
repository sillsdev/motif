using System.Linq;
using System.Text.Json.Nodes;
using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>An assess result tells the words the caller gave from the lowercase forms the second pass added.</summary>
[Trait("MotifTestLevel", "Unit")]
public sealed class AssessLowercaseCountsTests
{
    private static AssessCommandResponse Respond(params string[] given)
    {
        var (words, origins) = LowercasePass.For(new CoreWritingSystemDefinition("ceb")).Expand(given);
        var rows = words.Select(word => new AssessmentWordResult(word, "analysed", false, "Search completed", 1, null)
            { LowercaseOf = origins.GetValueOrDefault(word) }).ToArray();
        return new AssessCommandResponse(null!, null!, ["a-1"], "")
            { Words = rows, Measurements = [new ProducedAssessmentReference("a-1", "ParseTime", "inv")] };
    }

    [Fact]
    public void AMixedCaseListReportsInputAndAddedCountsAndMarksTheAddedRows()
    {
        var response = Respond("Balay", "balay", "Pidru", "TAO", "kaon");

        Assert.Equal(5, response.InputWords);
        Assert.Equal(2, response.LowercaseWords);
        Assert.Equal(7, response.Words.Count);
        Assert.Equal(["pidru", "tao"], response.Words.Where(row => row.LowercaseOf is not null).Select(row => row.Word));
        Assert.Equal("Pidru", response.Words.Single(row => row.Word == "pidru").LowercaseOf);
        Assert.Equal("TAO", response.Words.Single(row => row.Word == "tao").LowercaseOf);
        Assert.Null(response.Words.Single(row => row.Word == "Balay").LowercaseOf);
    }

    [Fact]
    public void AnAllLowercaseListAddsNothing()
    {
        var response = Respond("balay", "kaon");

        Assert.Equal(2, response.InputWords);
        Assert.Equal(0, response.LowercaseWords);
        Assert.All(response.Words, row => Assert.Null(row.LowercaseOf));
    }

    [Fact]
    public void AListThatAlreadyHoldsBothFormsAddsNothing()
    {
        var response = Respond("Balay", "balay");

        Assert.Equal(2, response.InputWords);
        Assert.Equal(0, response.LowercaseWords);
    }

    [Fact]
    public void ThePageAndTheFullJsonReportBothCountsAndKeepTotalAsTheRowCount()
    {
        var response = Respond("Balay", "Pidru");

        var page = AssessSummary.Page(response, 0, 25, detailed: false);
        Assert.Equal(2, page["inputWords"]!.GetValue<int>());
        Assert.Equal(2, page["lowercaseWords"]!.GetValue<int>());
        Assert.Equal(4, page["total"]!.GetValue<int>());
        Assert.Equal("Balay", page["words"]!.AsArray().Single(row => row!["word"]!.GetValue<string>() == "balay")!["lowercaseOf"]!.GetValue<string>());
        Assert.Null(page["words"]!.AsArray().Single(row => row!["word"]!.GetValue<string>() == "Balay")!["lowercaseOf"]);

        var full = AssessSummary.Full(response);
        Assert.Equal(2, full["inputWords"]!.GetValue<int>());
        Assert.Equal(2, full["lowercaseWords"]!.GetValue<int>());
        Assert.Equal("Pidru", full["words"]!.AsArray().Single(row => row!["word"]!.GetValue<string>() == "pidru")!["lowercaseOf"]!.GetValue<string>());
    }
}
