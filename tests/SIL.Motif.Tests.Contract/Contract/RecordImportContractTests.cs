using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class RecordImportContractTests
{
    private static readonly JsonSchema Schema = CreateValidator();

    [Theory]
    [InlineData("request")]
    [InlineData("dry-run")]
    [InlineData("draft")]
    [InlineData("dir-dry-run")]
    [InlineData("dir-draft")]
    [InlineData("dir-review")]
    public void GoldenConformsToTheClosedSchema(string name)
    {
        var golden = ReadGolden(name);
        var evaluated = Schema.Evaluate(JsonSerializer.SerializeToElement(golden), new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(evaluated.IsValid, JsonSerializer.Serialize(evaluated));
        if (name != "request")
        {
            var response = ProjectionJson.Deserialize<RecordImportResponse>(golden.ToJsonString());
            Assert.True(JsonNode.DeepEquals(golden, JsonNode.Parse(ProjectionJson.Serialize(response))));
        }
    }

    [Fact]
    public void TheRequestTypeCarriesExactlyTheSchemasMembers()
    {
        var request = ProjectionJson.Deserialize<RecordImportRequest>(ReadGolden("request").ToJsonString())!;
        Assert.Equal("import-1", request.DraftName);
        Assert.True(JsonNode.DeepEquals(ReadGolden("request"), JsonNode.Parse(ProjectionJson.Serialize(request))));
    }

    [Fact]
    public void UnknownMembersUnregisteredCodesAndAnUnstagedDraftNameAreRejected()
    {
        var response = ReadGolden("dry-run");
        response["applied"] = false;
        Assert.False(Schema.Evaluate(JsonSerializer.SerializeToElement(response)).IsValid);

        response = ReadGolden("dry-run");
        response["refusals"]![0]!["code"] = "record-import.invented";
        Assert.False(Schema.Evaluate(JsonSerializer.SerializeToElement(response)).IsValid);

        response = ReadGolden("dry-run");
        response["draftName"] = "import-1";
        Assert.False(Schema.Evaluate(JsonSerializer.SerializeToElement(response)).IsValid);

        response = ReadGolden("draft");
        response.AsObject().Remove("proposalId");
        Assert.False(Schema.Evaluate(JsonSerializer.SerializeToElement(response)).IsValid);

        var request = ReadGolden("request");
        request["page"] = 0;
        Assert.False(Schema.Evaluate(JsonSerializer.SerializeToElement(request)).IsValid);
    }

    [Fact]
    public void AFolderSummaryListsAtMostFiveRefusedRowsAndAPageAtMostTwoHundredRows()
    {
        Assert.Equal(5, RecordImportPaging.SummaryRefusedRows);
        Assert.Equal(200, RecordImportPaging.RowsPerPage);
        var response = ReadGolden("dir-dry-run");
        var rows = response["dir"]!["firstRefused"]!.AsArray();
        for (var index = 0; index < 5; index++) rows.Add(rows[0]!.DeepClone());
        Assert.False(Schema.Evaluate(JsonSerializer.SerializeToElement(response)).IsValid);

        response = ReadGolden("dir-draft");
        var files = response["files"]!.AsArray();
        for (var index = 0; index < 200; index++) files.Add(files[0]!.DeepClone());
        Assert.False(Schema.Evaluate(JsonSerializer.SerializeToElement(response)).IsValid);
    }

    [Fact]
    public void AFolderReviewListsAtMostThreeRowsOfEachKind()
    {
        Assert.Equal(3, RecordImportPaging.SummaryReviewRows);
        var response = ReadGolden("dir-review");
        var homographs = response["dir"]!["review"]!["firstHomographs"]!.AsArray();
        Assert.Equal(RecordImportPaging.SummaryReviewRows, homographs.Count);
        homographs.Add(homographs[0]!.DeepClone());
        Assert.False(Schema.Evaluate(JsonSerializer.SerializeToElement(response)).IsValid);

        response = ReadGolden("dir-review");
        response["dir"]!["review"]!["extra"] = 1;
        Assert.False(Schema.Evaluate(JsonSerializer.SerializeToElement(response)).IsValid);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(ReadGolden("dir-review").ToJsonString()) < RecordImportPaging.SummaryBytes * 2);
    }

    [Fact]
    public void ARequestNamesAFileOrAFolderButNotBoth()
    {
        var request = JsonNode.Parse("""{"projectPath":"P.fwdata","dirPath":"folder","draftName":"d","source":"s"}""")!;
        Assert.True(Schema.Evaluate(JsonSerializer.SerializeToElement(request)).IsValid);
        request["filePath"] = "a.md";
        Assert.False(Schema.Evaluate(JsonSerializer.SerializeToElement(request)).IsValid);
        request.AsObject().Remove("filePath");
        request.AsObject().Remove("dirPath");
        Assert.False(Schema.Evaluate(JsonSerializer.SerializeToElement(request)).IsValid);
    }

    [Fact]
    public void ThePagesAreBoundedSoTheSummaryFitsTwoKilobytes()
    {
        Assert.Equal(2048, RecordImportPaging.SummaryBytes);
        var response = ReadGolden("dry-run");
        var rows = response["refusals"]!.AsArray();
        for (var index = 0; index < RecordImportPaging.PageSize + 1; index++)
            rows.Add(rows[0]!.DeepClone());
        Assert.False(Schema.Evaluate(JsonSerializer.SerializeToElement(response)).IsValid);
    }

    private static JsonNode ReadGolden(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(
        RepoPaths.FindRepoRoot(), "tests", "SIL.Motif.Tests.Contract", "Golden", "RecordImport", name + ".json")))!;

    private static JsonSchema CreateValidator()
    {
        using var stream = typeof(RecordImportResponse).Assembly.GetManifestResourceStream("SIL.Motif.Contract.Schemas.record-import.schema.json")!;
        var schema = JsonNode.Parse(stream)!;
        schema["$id"] = "https://example.invalid/motif/record-import.schema.json";
        return JsonSchema.FromText(schema.ToJsonString());
    }
}
