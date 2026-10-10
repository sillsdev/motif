using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract.Parsimony;

public sealed class MeasureCatalogTests
{
    [Fact]
    public void CatalogIsClosedAndMatchesTheMeasureInventory()
    {
        var expectedIds = new[]
        {
            "P-adhoc-duplicate",
            "P-allo-duplicate-form",
            "P-statement-unused",
            "R-word-negative-accepted",
            "R-word-disapproved-produced",
            "B-affix-unslotted",
            "R-tmpl-precedence",
            "R-slot-blocking",
            "R-allo-unconditioned",
            "R-env-broad",
            "R-nc-excess",
            "P-allo-alternation-family",
            "B-adhoc-is-slot-order",
            "B-affix-null-vs-optional",
        };

        Assert.Equal(expectedIds.Order(StringComparer.Ordinal),
            MeasureCatalog.All.Select(measure => measure.Id).Order(StringComparer.Ordinal));
        Assert.Equal(MeasureCatalog.All.Count, MeasureCatalog.All.Select(measure => measure.Id).Distinct().Count());
        Assert.Null(MeasureCatalog.Find("P-unknown"));
        Assert.Equal("allomorph-duplicate-form/v1", MeasureCatalog.Find("P-allo-duplicate-form")!.QueryId);
        Assert.Equal("statement-unused/v1", MeasureCatalog.Find("P-statement-unused")!.QueryId);
        Assert.Equal("negative-accepted/v1", MeasureCatalog.Find("R-word-negative-accepted")!.QueryId);
        var alternation = MeasureCatalog.Find("P-allo-alternation-family")!;
        Assert.Equal("allo-alternation-family/v1", alternation.QueryId);
        Assert.Equal(ParsimonyTier.Static, alternation.Tier);
        Assert.Equal(ParsimonyThresholdOperator.GreaterThanOrEqual, alternation.Threshold!.Operator);
        Assert.Equal(2, alternation.Threshold.Value);
        Assert.Contains("alternation-families", alternation.EvidenceViews);
        Assert.Equal("template-precedence/v1", MeasureCatalog.Find("R-tmpl-precedence")!.QueryId);
        Assert.Equal("slot-blocking/v1", MeasureCatalog.Find("R-slot-blocking")!.QueryId);
        Assert.Equal("allo-unconditioned/v1", MeasureCatalog.Find("R-allo-unconditioned")!.QueryId);
        Assert.Equal(ParsimonyTier.Static, MeasureCatalog.Find("R-allo-unconditioned")!.Tier);
        Assert.Equal(ParsimonyTier.Text, MeasureCatalog.Find("R-slot-blocking")!.Tier);
        var adhocOrder = MeasureCatalog.Find("B-adhoc-is-slot-order")!;
        Assert.Equal("adhoc-slot-order/v1", adhocOrder.QueryId);
        Assert.Equal(ParsimonyTier.Text, adhocOrder.Tier);
        Assert.Equal(ParsimonyGroupKind.AdhocSlotOrder, adhocOrder.GroupKind);
        Assert.Contains("adhoc-context", adhocOrder.EvidenceViews);
        Assert.Contains("template-order", adhocOrder.EvidenceViews);
        Assert.Contains("approved-morph-sequences", adhocOrder.EvidenceViews);
        Assert.Contains("black-2018", adhocOrder.CitationKeys);
        var unslotted = MeasureCatalog.Find("B-affix-unslotted")!;
        Assert.Equal("unslotted-affix/v1", unslotted.QueryId);
        Assert.Equal(ParsimonyTier.Text, unslotted.Tier);
        Assert.Contains("unslotted-affixes", unslotted.EvidenceViews);
        Assert.Contains("approved-morph-sequences", unslotted.EvidenceViews);
        var nullOptional = MeasureCatalog.Find("B-affix-null-vs-optional")!;
        Assert.Equal("null-optional/v1", nullOptional.QueryId);
        Assert.Contains("null-optional", nullOptional.EvidenceViews);
        Assert.Contains("approved-morph-sequences", nullOptional.EvidenceViews);
        Assert.Contains("unslotted-affixes", ParsimonyViewCatalog.All.Select(view => view.Code));
        Assert.Contains("null-optional", ParsimonyViewCatalog.All.Select(view => view.Code));
        Assert.Contains("bender-poulson-drellishak-evans-2007",
            MeasureCatalog.Find("P-statement-unused")!.CitationKeys);
        Assert.Contains("black-2018", MeasureCatalog.Find("P-allo-duplicate-form")!.CitationKeys);
        Assert.All(MeasureCatalog.All, measure =>
        {
            Assert.NotEmpty(measure.RequiredCapabilities);
            Assert.Equal(measure.RequiredCapabilities.Count,
                measure.RequiredCapabilities.Distinct(StringComparer.Ordinal).Count());
            Assert.NotEmpty(measure.CitationKeys);
            Assert.Equal(measure.CitationKeys.Count,
                measure.CitationKeys.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal($"guide:parsimony/recipes/{measure.Id}", measure.RecipeLink);
        });
    }

    [Fact]
    public void MissingCapabilitiesAreReturnedByTheirStableNames()
    {
        var missing = MeasureCatalog.MissingCapabilities("P-adhoc-duplicate", ["load-facts"]);

        Assert.Contains("grouped-adhoc-load", missing);
        Assert.Contains("ordered-adhoc-targets", missing);
    }

    [Fact]
    public void FindingAndDispositionContractsKeepTheirClosedWireValues()
    {
        using var argumentsDocument = System.Text.Json.JsonDocument.Parse("{}");
        var finding = new ParsimonyFinding(
            "finding/one",
            "P-adhoc-duplicate",
            ParsimonyAxis.Parsimony,
            ParsimonyTier.Static,
            new ParsimonyFindingAttachment(
                ParsimonyAttachmentKind.AuthoredObject,
                "18f47b40-0c5f-4d08-a9b4-9988f2e47702",
                ParsimonyAuthoredObjectKind.AdhocProhibition),
            "group/one",
            new ParsimonyMeasureNumber(2, 3, "prohibitions"),
            new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.GreaterThan, 1, "1"),
            new string('a', 64),
            [new ParsimonyEvidenceReference("bundle/one", "adhoc-context", argumentsDocument.RootElement.Clone())],
            "guide:parsimony/recipes/P-adhoc-duplicate",
            [],
            ParsimonyVerification.NotRun);
        var disposition = new ParsimonyDispositionRecord(
            finding.MeasureId,
            ParsimonyAttachmentKind.AuthoredObject,
            finding.AttachesTo.Identity,
            "group/one",
            finding.EvidenceDigest,
            ParsimonyDispositionKind.Keep,
            "This is an intentional limitation.",
            null,
            ParsimonyActorType.Human,
            "linguist/one",
            DateTimeOffset.Parse("2026-10-05T12:00:00Z"),
            1,
            "report/one",
            null);

        var findingJson = ProjectionJson.Serialize(finding);
        var dispositionJson = ProjectionJson.Serialize(disposition);
        var restoredFinding = ProjectionJson.Deserialize<ParsimonyFinding>(findingJson);
        var restoredDisposition = ProjectionJson.Deserialize<ParsimonyDispositionRecord>(dispositionJson);

        Assert.Contains("\"axis\": \"parsimony\"", findingJson, StringComparison.Ordinal);
        Assert.Contains("\"disposition\": \"keep\"", dispositionJson, StringComparison.Ordinal);
        Assert.NotNull(restoredFinding);
        Assert.Equal(finding.FindingId, restoredFinding.FindingId);
        Assert.Equal(finding.AttachesTo, restoredFinding.AttachesTo);
        Assert.Equal(finding.EvidenceRefs.Single().View, Assert.Single(restoredFinding.EvidenceRefs).View);
        Assert.Equal(disposition, restoredDisposition);
    }

    [Fact]
    public void NamedViewRowsKeepTheirTypeDiscriminatorSeparateFromNaturalClassKind()
    {
        var response = new ParsimonyNamedViewResponse("bundle/one", "alternation-families", 1,
            ParsimonyMeasureStatus.Computed, [], 2, 2, null, false,
            [
                new ParsimonyNaturalClassViewRow("class/one", "segments", "Labials", [], [], 0, 0, true, null),
                new ParsimonyAlternationFamilyViewRow("family/one", "n ~ m in 2 prefixes", "feature-weighted", false,
                    "condition:unspecified", "gate/one", "prefixes", [], [], [], [], [], []),
            ], new string('a', 64), null);

        using var document = System.Text.Json.JsonDocument.Parse(ProjectionJson.Serialize(response));
        var rows = document.RootElement.GetProperty("rows").EnumerateArray().ToArray();

        Assert.Equal("natural-class", rows[0].GetProperty("kind").GetString());
        Assert.Equal("segments", rows[0].GetProperty("classKind").GetString());
        Assert.Equal("alternation-family", rows[1].GetProperty("kind").GetString());
    }
}
