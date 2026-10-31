using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>Checks the warning report captured from the released PanGloss executable.</summary>
public sealed class GrammarHealthReleaseTests
{
    [Fact]
    public void ReleasedReportPreservesFindingTitlesAndSubjectIdentities()
    {
        var response = GrammarHealthReportReader.ReadResponse(CapturedReport());

        Assert.True(response.HasBaseline);
        Assert.Equal("en", response.Locale);
        Assert.Equal(3, response.Findings.Count);
        var unknown = Assert.Single(response.Findings, finding => finding.Code == "fwdata.unknown-morph-type-guid");
        Assert.Equal("Unknown morph type reference", unknown.Title);
        Assert.Equal("docs/diagnostics/fwdata.unknown-morph-type-guid.md", unknown.HelpPath);
        Assert.Contains("/v0.12.2/", unknown.HelpUrl);
        var authored = Assert.Single(unknown.Subject, subject => subject.Status == GrammarSubjectStatus.Object);
        Assert.Equal("xxx", authored.Title);
        Assert.Equal("00000000-0000-0000-0000-000000000044", authored.SubjectGuid);
        Assert.Equal("MoStemAllomorph", authored.SourceClass);
        Assert.Equal("MorphType", authored.Field);
        Assert.Equal(FieldWorksLinkStatus.Available, authored.LinkStatus);
        var missing = Assert.Single(unknown.Subject, subject => subject.Status == GrammarSubjectStatus.UnresolvedReference);
        Assert.Equal("00000000-0000-0000-0000-00000000abcd", missing.SubjectGuid);
        Assert.Equal(FieldWorksLinkReason.UnresolvedReference, missing.LinkReason);
        Assert.Null(missing.FieldWorksLink);
        Assert.Equal(2, response.Findings.Count(finding => finding.Code == "provisional.letter"));
        Assert.Equal(response.Findings.Count, response.Summary.Sum(row => row.Count));
    }

    [Fact]
    public void ACheckedReportWithoutFindingsRemainsASuccess()
    {
        var report = JsonNode.Parse(CapturedReport())!;
        report["diagnostics"] = new JsonArray();
        report["summary"] = new JsonArray();

        var response = GrammarHealthReportReader.ReadResponse(report.ToJsonString());

        Assert.True(response.HasBaseline);
        Assert.Empty(response.Findings);
        Assert.Empty(response.Summary);
        Assert.Equal("en", response.Locale);
    }

    [Theory]
    [InlineData("locale")]
    [InlineData("title")]
    [InlineData("subjects")]
    [InlineData("negative-count")]
    [InlineData("false-link")]
    public void AnIncompleteOrContradictoryReleaseReportIsRefused(string defect)
    {
        var report = JsonNode.Parse(CapturedReport())!.AsObject();
        if (defect == "locale") report.Remove("locale");
        else if (defect == "title") report["diagnostics"]![0]!["title"] = "";
        else if (defect == "subjects") report["diagnostics"]![0]!["subjects"] = null;
        else if (defect == "negative-count") report["summary"]![0]!["count"] = -1;
        else report["diagnostics"]![0]!["subjects"]![1]!["fieldworks"] =
            report["diagnostics"]![0]!["subjects"]![0]!["fieldworks"]!.DeepClone();

        Assert.Throws<JsonException>(() => GrammarHealthReportReader.ReadResponse(report.ToJsonString()));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void AnUnsupportedReportNamesItsSchemaVersion(int version)
    {
        var report = JsonNode.Parse(CapturedReport())!;
        report["schema_version"] = version;

        var exception = Assert.Throws<GrammarHealthReportReader.UnsupportedGrammarHealthSchemaException>(
            () => GrammarHealthReportReader.ReadResponse(report.ToJsonString()));

        Assert.Equal(version, exception.Version);
        Assert.Contains($"version {version}", exception.Message);
    }

    private static string CapturedReport() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "TestFixtures", "GrammarHealth", "pangloss-v0.10.0-producer.json"));
}
