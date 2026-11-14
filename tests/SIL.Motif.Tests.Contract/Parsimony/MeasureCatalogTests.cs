using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract.Parsimony;

public sealed class MeasureCatalogTests
{
    [Fact]
    public void CatalogIsClosedAndMatchesTheMeasureInventory()
    {
        Assert.Equal(MeasureCatalog.All.Count, MeasureCatalog.All.Select(measure => measure.Id).Distinct().Count());
        Assert.Null(MeasureCatalog.Find("P-unknown"));
        var alternation = MeasureCatalog.Find("P-allo-alternation-family")!;
        Assert.Equal(ParsimonyTier.Static, alternation.Tier);
        Assert.Equal(ParsimonyThresholdOperator.GreaterThanOrEqual, alternation.Threshold!.Operator);
        Assert.Equal(2, alternation.Threshold.Value);
        Assert.Equal(ParsimonyTier.Static, MeasureCatalog.Find("R-allo-unconditioned")!.Tier);
        Assert.Equal(ParsimonyTier.Text, MeasureCatalog.Find("R-slot-blocking")!.Tier);
        var adhocOrder = MeasureCatalog.Find("B-adhoc-is-slot-order")!;
        Assert.Equal(ParsimonyTier.Text, adhocOrder.Tier);
        Assert.Equal(ParsimonyGroupKind.AdhocSlotOrder, adhocOrder.GroupKind);
        var unslotted = MeasureCatalog.Find("B-affix-unslotted")!;
        Assert.Equal(ParsimonyTier.Text, unslotted.Tier);
        Assert.All(MeasureCatalog.All, measure =>
        {
            Assert.NotEmpty(measure.RequiredCapabilities);
            Assert.Equal(measure.RequiredCapabilities.Count,
                measure.RequiredCapabilities.Distinct(StringComparer.Ordinal).Count());
            Assert.NotEmpty(measure.CitationKeys);
            Assert.Equal(measure.CitationKeys.Count,
                measure.CitationKeys.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal($"guide:parsimony/recipes/{measure.Id.ToLowerInvariant()}", measure.RecipeLink);
        });
    }

    [Fact]
    public void TheFourRedundancyMeasuresAreDeclaredAndEveryBlockCapableMeasureIsInTheCatalog()
    {
        foreach (var id in new[] { "P-tag-polarity-majority", "P-variant-same-shape", "P-lexeme-near-duplicate" })
            Assert.NotNull(MeasureCatalog.Find(id)!.QueryId);
        // Needs per-rule context matches PanGloss does not expose yet (ADR 0116, PanGloss ask).
        Assert.Null(MeasureCatalog.Find("P-tag-implied-by-context")!.QueryId);
        Assert.All(BlockingPolicy.BlockCapable, id => Assert.NotNull(MeasureCatalog.Find(id)));
    }

    [Fact]
    public void GrammarMeasuresRunInTheGrammarGroupAndNineInTheTextGroup()
    {
        string[] grammar =
        [
            "P-adhoc-duplicate", "P-allo-alternation-family", "P-allo-duplicate-form", "P-allo-repeated-form",
            "P-entry-duplicate", "P-env-duplicate", "P-env-literal-host", "P-feature-few-carriers",
            "P-gloss-echoes-form", "P-lexeme-near-duplicate", "P-msa-no-category", "P-statement-unused",
            "P-tag-implied-by-context", "P-tag-polarity-majority", "P-variant-same-shape", "R-allo-unconditioned",
        ];
        Assert.Equal(grammar, MeasureCatalog.All.Where(measure => measure.Group == ParsimonyMeasureGroup.Grammar)
            .Select(measure => measure.Id).Order(StringComparer.Ordinal));
        Assert.Equal(9, MeasureCatalog.All.Count(measure => measure.Group == ParsimonyMeasureGroup.Text));
        Assert.All(MeasureCatalog.All.Where(measure => measure.Tier == ParsimonyTier.Parser),
            measure => Assert.Equal(ParsimonyMeasureGroup.Text, measure.Group));
        Assert.Equal(ParsimonyTier.Text, MeasureCatalog.Find("B-affix-null-vs-optional")!.Tier);
        Assert.DoesNotContain(ParsimonyCapabilities.ApprovedSignatures,
            MeasureCatalog.Find("P-allo-alternation-family")!.RequiredCapabilities);
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
            "guide:parsimony/recipes/p-adhoc-duplicate",
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

        var json = ProjectionJson.Serialize(response);
        Assert.Contains('\n', json);
        Assert.DoesNotContain('\r', json);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var rows = document.RootElement.GetProperty("rows").EnumerateArray().ToArray();

        Assert.Equal("natural-class", rows[0].GetProperty("kind").GetString());
        Assert.Equal("segments", rows[0].GetProperty("classKind").GetString());
        Assert.Equal("alternation-family", rows[1].GetProperty("kind").GetString());
    }
}
