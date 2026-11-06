using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>Checks release captures and explicitly derived navigation fixtures.</summary>
public sealed class GrammarHealthReleaseTests
{
    [Fact]
    public void ReleasedSchemaFiveReportsThatOptionalFstInspectionWasNotRequested()
    {
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "GrammarHealth",
            "pangloss-v0.16.0-producer.json"));
        var response = GrammarHealthReportReader.ReadResponse(raw);
        Assert.False(response.FstChecked);
        Assert.Null(response.FstCheckError);
        var finding = Assert.Single(response.Findings);
        Assert.Equal("provisional.letter", finding.Code);
        Assert.False(finding.BlocksFst);
        Assert.Null(finding.FstReason);
        Assert.Single(response.Summary);
    }

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
        Assert.Contains("/v0.16.1/", unknown.HelpUrl);
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

    [Fact]
    public void AWholeToolPlaceWithAnEmptyFieldIsRead()
    {
        var report = JsonNode.Parse(CapturedReport())!;
        report["diagnostics"]![0]!["fieldworks_places"] = new JsonArray(
            new JsonObject { ["tool"] = "AdhocCoprohibEdit", ["field"] = "" });

        var response = GrammarHealthReportReader.ReadResponse(report.ToJsonString());

        var place = Assert.Single(response.Findings[0].FieldWorksPlaces);
        Assert.Equal("AdhocCoprohibEdit", place.Tool);
        Assert.Equal(string.Empty, place.Field);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void APlaceWithABlankToolIsStillRefused(string tool)
    {
        var report = JsonNode.Parse(CapturedReport())!;
        report["diagnostics"]![0]!["fieldworks_places"] = new JsonArray(
            new JsonObject { ["tool"] = tool, ["field"] = "" });

        Assert.Throws<JsonException>(() => GrammarHealthReportReader.ReadResponse(report.ToJsonString()));
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

    [Fact]
    public void AReportWhoseFindingHasNoOriginIsRefusedSoNoFindingDefaultsToACheckFinding()
    {
        var report = JsonNode.Parse(CapturedReport())!.AsObject();
        Assert.True(report["diagnostics"]![0]!.AsObject().Remove("origin"));

        Assert.Throws<JsonException>(() => GrammarHealthReportReader.ReadResponse(report.ToJsonString()));
    }

    [Fact]
    public void ARealImportFindingReadsAsImport()
    {
        var response = GrammarHealthReportReader.ReadResponse(CapturedReport());

        var unknown = Assert.Single(response.Findings, finding => finding.Code == "fwdata.unknown-morph-type-guid");
        Assert.Equal(GrammarFindingOrigin.Import, unknown.Origin);
        Assert.All(response.Findings, finding => Assert.Equal(GrammarFindingOrigin.Import, finding.Origin));
    }

    [Theory]
    [InlineData("import", false)]
    [InlineData("check", true)]
    public void TheOriginReadFromTheReportDecidesWhetherAnUnlistedWarningIsDroppedGrammar(string origin, bool clean)
    {
        var report = JsonNode.Parse(CapturedReport())!.AsObject();
        var finding = report["diagnostics"]![0]!.AsObject();
        finding["code"] = "grammar.brand-new-code";
        report["summary"]![0]!["code"] = "grammar.brand-new-code";
        finding["help_path"] = "docs/diagnostics/grammar.brand-new-code.md";
        finding["origin"] = origin;
        var candidate = GrammarHealthReportReader.ReadResponse(report.ToJsonString());

        var result = DroppedGrammarClassifier.Classify(candidate, new GrammarCheckResponse([], HasBaseline: true),
            new HashSet<Guid>());

        Assert.Equal(clean, result.IsClean);
        if (!clean) Assert.Equal("grammar.brand-new-code", Assert.Single(result.Introduced).Code);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(4)]
    public void AnUnsupportedReportNamesItsSchemaVersion(int version)
    {
        var report = JsonNode.Parse(CapturedReport())!;
        report["schema_version"] = version;

        var exception = Assert.Throws<GrammarHealthReportReader.UnsupportedGrammarHealthSchemaException>(
            () => GrammarHealthReportReader.ReadResponse(report.ToJsonString()));

        Assert.Equal(version, exception.Version);
        Assert.Contains($"version {version}", exception.Message);
    }

    [Fact]
    public void FstBlockingLabelsAndIncompleteInspectionRemainVisible()
    {
        var report = JsonNode.Parse(CapturedReport())!;
        report["fst_check_error"] = "The optional FST inspection did not complete.";
        report["diagnostics"]![0]!["blocks_fst"] = true;
        report["diagnostics"]![0]!["fst_reason"] = "retained construct cannot be emitted";
        report["summary"]![0]!["blocks_fst"] = true;
        report["summary"]![0]!["fst_reason"] = "retained construct cannot be emitted";

        var response = GrammarHealthReportReader.ReadResponse(report.ToJsonString());

        Assert.False(response.FstChecked);
        Assert.Equal(report["fst_check_error"]!.GetValue<string>(), response.FstCheckError);
        Assert.True(response.Findings[0].BlocksFst);
        Assert.Equal("retained construct cannot be emitted", response.Findings[0].FstReason);
        Assert.True(response.Summary[0].BlocksFst);
        Assert.Equal(response.Findings[0].FstReason, response.Summary[0].FstReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProducerCaptureWithContradictoryFstLabelsIsRefused(bool differentReasons)
    {
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "GrammarHealth",
            "pangloss-v0.16.0-producer.json"));
        Assert.Single(GrammarHealthReportReader.ReadResponse(raw).Findings);
        var report = JsonNode.Parse(raw)!;
        report["diagnostics"]![0]!["blocks_fst"] = true;
        report["diagnostics"]![0]!["fst_reason"] = "diagnostic reason";
        if (differentReasons)
        {
            report["summary"]![0]!["blocks_fst"] = true;
            report["summary"]![0]!["fst_reason"] = "summary reason";
        }

        Assert.Throws<JsonException>(() => GrammarHealthReportReader.ReadResponse(report.ToJsonString()));
    }

    [Fact]
    public void SummaryFstLabelMustAgreeWithEveryDiagnosticOfItsCode()
    {
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "GrammarHealth",
            "pangloss-v0.16.0-producer.json"));
        var report = JsonNode.Parse(raw)!;
        var second = report["diagnostics"]![0]!.DeepClone();
        second["blocks_fst"] = true;
        second["fst_reason"] = "another reason";
        report["diagnostics"]!.AsArray().Add(second);
        report["summary"]![0]!["count"] = 2;

        Assert.Throws<JsonException>(() => GrammarHealthReportReader.ReadResponse(report.ToJsonString()));
    }

    [Theory]
    [InlineData("diagnostics", "blocks_fst")]
    [InlineData("diagnostics", "fst_reason")]
    [InlineData("summary", "blocks_fst")]
    [InlineData("summary", "fst_reason")]
    public void SchemaFiveRequiresEveryRowsFstLabel(string rows, string field)
    {
        var report = JsonNode.Parse(CapturedReport())!;
        report[rows]![0]!.AsObject().Remove(field);

        Assert.Throws<JsonException>(() => GrammarHealthReportReader.ReadResponse(report.ToJsonString()));
    }

    [Fact]
    public void ACompletedFstInspectionCannotAlsoHaveACheckError()
    {
        var report = JsonNode.Parse(CapturedReport())!;
        report["fst_checked"] = true;
        report["fst_check_error"] = "incomplete";
        Assert.Throws<JsonException>(() => GrammarHealthReportReader.ReadResponse(report.ToJsonString()));
    }

    private static string CapturedReport() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "TestFixtures", "GrammarHealth", "schema-v5-navigation.json"));
}
