using System.Collections.Generic;
using System.Text.Json;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Effects;
using SIL.Motif.Projection;
using SIL.Motif.Projection.Rendering;
using DryRunResult = SIL.Motif.Model.DryRun.DryRun;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class DryRunOperationRenderingTests
{
    [Fact]
    public void DryRunTextShowsOperationCountsAndOmitsTheFullEffectsByDefault()
    {
        var projection = new DryRunProjection("proposal/one", "intent/one", "Baseline",
            [new("bundle/one", "analysis/wfiMorphBundle/form", [], null)], "effects/one", "footprint/one")
        {
            Operations =
            [
                new("operation/retarget", "analysis/wfiMorphBundle/setMorph", null, null, [], null),
                new("operation/delete", "lexical/lexEntry/deleteAlternateForm", null, null, [], null),
            ],
        };

        var text = CommandTextRenderer.Render(projection);

        Assert.Contains("analysis/wfiMorphBundle/setMorph: 1", text, StringComparison.Ordinal);
        Assert.Contains("lexical/lexEntry/deleteAlternateForm: 1", text, StringComparison.Ordinal);
        Assert.Contains("effects: 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("bundle/one", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DryRunTextKeepsTheDependencyAndEffectOperationLinkVisible()
    {
        var projection = new DryRunProjection("proposal/one", "intent/one", "Baseline",
            [new("bundle/one", "analysis/wfiMorphBundle/form", [], null)
            {
                OperationIds = ["operation/retarget"],
            }], "effects/one", "footprint/one")
        {
            Operations =
            [
                new("operation/retarget", "analysis/wfiMorphBundle/setMorph", null, null, [], null),
                new("operation/delete", "lexical/lexEntry/deleteAlternateForm", null, null,
                    ["operation/retarget"], null),
            ],
        };

        var text = CommandTextRenderer.Render(projection, includeEffects: true);

        Assert.Contains("operations (2)", text, StringComparison.Ordinal);
        Assert.Contains("dependsOn: operation/retarget", text, StringComparison.Ordinal);
        Assert.Contains("operations: operation/retarget", text, StringComparison.Ordinal);
        Assert.Contains("field=analysis/wfiMorphBundle/form", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DryRunJsonKeepsFullListsAndAddsAnEffectsSummary()
    {
        var digest = "sha256:" + new string('a', 64);
        var projection = new DryRunProjection("proposal/one", "intent/one", "Baseline",
            [new("bundle/one", "analysis/wfiMorphBundle/form", [], null)], "effects/one", "footprint/one")
        {
            Operations = [new("operation/one", "analysis/wfiMorphBundle/setMorph", null, null, [], null)],
            Baseline = new BaselineToken("project/one", digest, "projection/one", "2024-01-01T00:00:00Z",
                digest, "session/one", 1),
        };

        using var json = System.Text.Json.JsonDocument.Parse(ProjectionJson.Serialize(projection));
        var root = json.RootElement;

        Assert.Equal(1, root.GetProperty("operations").GetArrayLength());
        Assert.Equal(1, root.GetProperty("effects").GetArrayLength());
        Assert.Equal(1, root.GetProperty("effectsSummary").GetProperty("operationCount").GetInt32());
        Assert.Equal(digest, root.GetProperty("effectsSummary").GetProperty("projectHashBefore").GetString());
    }

    [Fact]
    public void DryRunProjectionCarriesWarningsFromWarningEffects()
    {
        const string warning = "This affix has no feeding stem.";
        var effect = new ExpectedEffect(CanonicalId.FromGuid(Guid.NewGuid()), "dryRunWarning",
            new Dictionary<string, string>(), new Dictionary<string, string> { ["warning"] = warning },
            JsonSerializer.SerializeToElement(new { warning }));
        var dryRun = new DryRunResult("intent/one", "Baseline", [effect], "effects/one",
            new BoundDryRunAnchor("intent/one", "footprint/one", "effects/one", "runner", "lcm",
                "projection", "2026-01-01T00:00:00Z"));

        var projection = DryRunProjectionBuilder.Build("proposal/one", dryRun);

        Assert.Equal(warning, Assert.Single(projection.Warnings));
        Assert.Equal(warning, Assert.Single(projection.EffectsSummary.Warnings));
    }

    [Fact]
    public void ApplyTextShowsCountsAndHidesTheEffectsUntilRequested()
    {
        var projection = new ApplyProjection("proposal/one", false, "Applied.",
            [new("bundle/one", "analysis/wfiMorphBundle/form", [], null)], "effects/one",
            new AppliedLogEntrySummary("proposal/one", "now", "person", "intent/one"))
        {
            OperationKinds = ["analysis/wfiMorphBundle/setMorph"],
        };

        var summary = CommandTextRenderer.Render(projection);
        var detailed = CommandTextRenderer.Render(projection, includeEffects: true);

        Assert.Contains("analysis/wfiMorphBundle/setMorph: 1", summary, StringComparison.Ordinal);
        Assert.Contains("effects: 1", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("bundle/one", summary, StringComparison.Ordinal);
        Assert.Contains("bundle/one", detailed, StringComparison.Ordinal);
        using var json = System.Text.Json.JsonDocument.Parse(ProjectionJson.Serialize(projection));
        Assert.Equal(1, json.RootElement.GetProperty("effects").GetArrayLength());
        Assert.Equal(1, json.RootElement.GetProperty("effectsSummary").GetProperty("effectCount").GetInt32());
    }

    [Fact]
    public void RetirementReviewTextShowsOneProposalCountsReadingEvidenceAndDestinationRefusal()
    {
        const string digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var dryRun = new DryRunProjection("proposal/one", "intent/one", "Baseline",
            [new("bundle/one", "analysis/wfiMorphBundle/form", [new("qaa", "ta", "ra")], null)
            {
                OperationIds = ["operation/retarget"],
            }], "effects/one", "footprint/one")
        {
            Operations =
            [
                new("operation/rule", "grammar/phSegmentRule/setDisabled", null, null, [], null),
                new("operation/retarget", "analysis/wfiMorphBundle/setMorph", null, null,
                    ["operation/rule"], null),
                new("operation/delete", "lexical/lexEntry/deleteAlternateForm", null, null,
                    ["operation/retarget"], null),
            ],
        };
        var statistics = new SIL.Motif.Contract.Retirement.RetirementReviewStatistics(
            new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), 1,
            [new(true, true, 1, 1), new(true, false, 0, 0),
                new(false, true, 0, 0), new(false, false, 0, 0)],
            0, 1, 1, 0, new("finding/one", 1, 1, 0, 1, digest, digest, true), digest);
        var projection = new RetirementProposalReviewProjection("proposal/one", "intent/one", dryRun,
            statistics,
            [new("rule", "New rule and sound class", ["operation/rule"], []),
                new("bundle", "Analyses and bundle text", ["operation/retarget"], [dryRun.Effects[0]]),
                new("other-references", "Other references", [], []),
                new("retired-form", "Deleted forms", ["operation/delete"], [])],
            [new("case/one", "reading/one", "wordform/one", "ata", "qaa", "ata", "ata",
                "approved", "A", "preserved", true, true, true, "traced", "rule/one", digest, null)],
            new("finding/one", 1, 1, 0, 1, digest, digest, true, false),
            new(1, 1, 0, 0, []) { Message = "Declare every replacement destination." }, []);

        var text = CommandTextRenderer.Render(projection);

        Assert.Contains("One Proposal: proposal/one", text, StringComparison.Ordinal);
        Assert.Contains("New rule and sound class", text, StringComparison.Ordinal);
        Assert.Contains("dependsOn: operation/rule", text, StringComparison.Ordinal);
        Assert.Contains("[qaa] \"ta\" -> \"ra\"", text, StringComparison.Ordinal);
        Assert.Contains("A approved  ata: preserved", text, StringComparison.Ordinal);
        Assert.Contains("rule attribution: traced (rule/one)", text, StringComparison.Ordinal);
        Assert.Contains("finding: resolved", text, StringComparison.Ordinal);
        Assert.Contains("disposition: Not suppressed", text, StringComparison.Ordinal);
        Assert.Contains("1 distinct unresolved Approved analyses; 1 unresolved ad hoc rules; 0 other references", text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewTextShowsTheReadingIdentityWithItsDifferencesAndIncompleteWords()
    {
        var digest = "sha256:" + new string('b', 64);
        var dryRun = new DryRunProjection("proposal/one", "intent/one", "Baseline", [], "effects/one", "footprint/one");
        var projection = new RetirementProposalReviewProjection("proposal/one", "intent/one", dryRun,
            new SIL.Motif.Contract.Retirement.RetirementReviewStatistics(new(0, 0, 0, 0), new(0, 0, 0, 0),
                new(0, 0, 0, 0), 0,
                [new(true, true, 0, 0), new(true, false, 0, 0), new(false, true, 0, 0), new(false, false, 0, 0)],
                0, 0, 0, 0, new("finding/one", 0, 0, 0, 0, digest, digest, true), digest),
            [], [], new("finding/one", 0, 0, 0, 0, digest, digest, true, false), new(0, 0, 0, 0, []), [])
        {
            ReadingIdentity = new ReadingIdentityResult(ReadingIdentityStatus.Differs, 3, 1, 4, 1, 0,
                [new("kan", ["a"], [])], ["kaun"], ["1 affected words have no completed search on both sides."]),
        };

        var text = CommandTextRenderer.Render(projection);

        Assert.Contains("readings of affected words: Differs; 1 of 3 words identical; 1 lost, 0 new of 4 before", text);
        Assert.Contains("kan: lost [a] new []", text);
        Assert.Contains("incomplete: kaun", text);
    }
}
