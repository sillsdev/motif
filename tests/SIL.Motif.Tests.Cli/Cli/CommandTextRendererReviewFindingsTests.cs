using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Review;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class CommandTextRendererReviewFindingsTests
{
    [Fact]
    public void FormsQueryTextShowsEvidenceScoresAndIdsWithoutRowsGroupsOrItems()
    {
        var score = new QueryScore("tolerance", "tolerance/1", "stem",
            new Dictionary<string, long> { ["hits"] = 2 },
            new Dictionary<string, double?> { ["rate"] = 0.5 }, "productive", ["two examples"]);
        var evidence = new QueryEvidence("stems", 2, 7,
            [new QueryContext("category", "verb", 2, 1, 0.5, true, true, true, "yes", "holds", "A")],
            0, [], [new QueryExample("member/one", "mala", 3)],
            [new QueryQuestion("question/one", "Does this form share the pattern?")], null, ["held-out unavailable"]);
        var result = new QueryResult([], [], ["object/one"])
        {
            Evidence = evidence,
            Scores = [score],
        };
        var response = new FormsQueryResponse("trial/one", "grammar/one", "bundle/one", false, 0, result);

        var text = CommandTextRenderer.Render(CommandOutcome<FormsQueryResponse>.Success(response), asJson: false).Output;

        Assert.Contains("2 of 7", text, StringComparison.Ordinal);
        Assert.Contains("category=verb", text, StringComparison.Ordinal);
        Assert.Contains("tolerance", text, StringComparison.Ordinal);
        Assert.Contains("Counts hits: 2", text, StringComparison.Ordinal);
        Assert.Contains("object/one", text, StringComparison.Ordinal);
        Assert.DoesNotContain("No matching forms.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormsQueryTextBoundsNestedItemKeysAndValues()
    {
        var values = Enumerable.Range(0, 12).ToDictionary(index => $"key-{index:D2}",
            index => Enumerable.Range(0, 12).Select(value => $"value-{index:D2}-{value:D2}").ToArray());
        var result = new QueryResult([], [], []) { Items = [new QueryItem("item/one", values)] };
        var response = new FormsQueryResponse("trial/one", "grammar/one", "bundle/one", false, 0, result);

        var text = CommandTextRenderer.Render(CommandOutcome<FormsQueryResponse>.Success(response), asJson: false).Output;

        Assert.Contains("key-00", text, StringComparison.Ordinal);
        Assert.Contains("value-00-00", text, StringComparison.Ordinal);
        Assert.Contains("and 2 more values; use --json for all", text, StringComparison.Ordinal);
        Assert.Contains("and 2 more; use --json for all", text, StringComparison.Ordinal);
        Assert.DoesNotContain("key-10", text, StringComparison.Ordinal);
        Assert.DoesNotContain("value-00-10", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProposalReviewTextShowsWordPayloadAndGrammarEffects()
    {
        var beforeReading = new ParserReading([new ParserReadingMorph("before-form", "before-gloss", "n", null, false, null)]);
        var afterReading = new ParserReading([new ParserReadingMorph("after-form", "after-gloss", "v", null, false, null)]);
        var change = new PendingChange("change/one", "wordform/one", "word-text", "replace-analysis", null,
            "display-reading", ["op/word"]);
        var fit = new ChangeFit("change/one", "no-longer-fits", ["fit-reason"]);
        var word = new WordChangeEntry("change/one", 0, "Word entry", ["op/word"], [], "digest/word", false,
            change, fit, new Paired<ParserReading?>(beforeReading, afterReading, true, true, true, null));
        var effect = new EffectView("object/one", "Abbreviation", [new EffectChange("en", "effect-before", "effect-after")]);
        var reading = new RetirementAffectedReading("case/one", "reading/one", "wordform/affected", "affected-word",
            "en", "surface-before", "surface-after", "Approved", "✓", "verified", true, true, false,
            "attributed", "rule/one", "digest/attribution", "attribution-detail");
        var part = new RetirementReviewPart("part/one", "part-title", ["op/grammar"], [effect]);
        var grammar = new GrammarChangeEntry("grammar/one", 1, "Grammar entry", ["op/grammar"], [], "digest/grammar",
            false, "compose-natural-class", "grammar-summary", [effect], [reading], [part]);
        var response = new ProposalReviewResponse("proposal/one", "intent/one", "revision/one", [word, grammar], null,
            null, [fit], 0);

        var text = CommandTextRenderer.Render(CommandOutcome<ProposalReviewResponse>.Success(response), asJson: false).Output;

        Assert.Contains("word-text", text, StringComparison.Ordinal);
        Assert.Contains("before-form", text, StringComparison.Ordinal);
        Assert.Contains("after-form", text, StringComparison.Ordinal);
        Assert.Contains("fit-reason", text, StringComparison.Ordinal);
        Assert.Contains("grammar-summary", text, StringComparison.Ordinal);
        Assert.Contains("effect-before", text, StringComparison.Ordinal);
        Assert.Contains("affected-word", text, StringComparison.Ordinal);
        Assert.Contains("part-title", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactInventoryTextShowsArtifactDetailsAndBoundsLongLists()
    {
        var artifacts = Enumerable.Range(0, 12).Select(index => new ArtifactInventoryEntry(
            $"artifacts/file-{index:D2}.bin", "assessment", 120 + index, 90 + index,
            ["invocation/one"], "retained", "current-record", ["record/one"], true)).ToArray();
        var response = new ArtifactInventoryResponse("/project", "stores-digest", true, true, true,
            1400, [], artifacts, []);

        var text = CommandTextRenderer.Render(CommandOutcome<ArtifactInventoryResponse>.Success(response), asJson: false).Output;

        Assert.Contains("artifacts/file-00.bin", text, StringComparison.Ordinal);
        Assert.Contains("assessment", text, StringComparison.Ordinal);
        Assert.Contains("invocation/one", text, StringComparison.Ordinal);
        Assert.Contains("retained", text, StringComparison.Ordinal);
        Assert.Contains("120", text, StringComparison.Ordinal);
        Assert.Contains("record/one", text, StringComparison.Ordinal);
        Assert.Contains("and 2 more; use --json for all", text, StringComparison.Ordinal);
        Assert.DoesNotContain("artifacts/file-10.bin", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NotebookRecordTypeTextBoundsLongLists()
    {
        var response = new NotebookRecordTypesResponse(Enumerable.Range(0, 12)
            .Select(index => new NotebookRecordType($"record/{index:D2}", $"type-{index:D2}")).ToArray());

        var text = CommandTextRenderer.Render(CommandOutcome<NotebookRecordTypesResponse>.Success(response), asJson: false).Output;

        Assert.Contains("type-00", text, StringComparison.Ordinal);
        Assert.Contains("and 2 more; use --json for all", text, StringComparison.Ordinal);
        Assert.DoesNotContain("type-10", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectStoreUpgradeTextBoundsUnreadableRevisionLists()
    {
        var report = new MotifStoreUpgrade.Report(1, 2, null, [], [], Enumerable.Range(0, 12)
            .Select(index => new MotifStoreUpgrade.UnreadableRevision($"proposal/{index:D2}", "digest", $"error-{index:D2}"))
            .ToList());
        var response = new ProjectStoreUpgradeResponse(report);

        var text = CommandTextRenderer.Render(CommandOutcome<ProjectStoreUpgradeResponse>.Success(response), asJson: false).Output;

        Assert.Contains("error-00", text, StringComparison.Ordinal);
        Assert.Contains("and 2 more; use --json for all", text, StringComparison.Ordinal);
        Assert.DoesNotContain("error-10", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WordReadStateTextBoundsReadAndSkippedOccurrences()
    {
        OccurrenceAnchor Anchor(int index) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), index);
        var response = new WordReadStateResponse(Enumerable.Range(0, 12).Select(Anchor).ToArray(), true)
        {
            SkippedOccurrences = Enumerable.Range(0, 12).Select(Anchor).ToArray(),
        };

        var text = CommandTextRenderer.Render(CommandOutcome<WordReadStateResponse>.Success(response), asJson: false).Output;

        Assert.Contains("Read occurrences: 12", text, StringComparison.Ordinal);
        Assert.Contains("Skipped Text", text, StringComparison.Ordinal);
        Assert.Contains("and 2 more; use --json for all", text, StringComparison.Ordinal);
        Assert.Equal(2, text.Split("and 2 more; use --json for all", StringSplitOptions.None).Length - 1);
        Assert.Equal(10, text.Split("  Text ", StringSplitOptions.None).Length - 1);
        Assert.Equal(10, text.Split("  Skipped Text ", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void RuleAttemptSummaryTextBoundsGroups()
    {
        var summary = new RuleAttemptSummary(5, 12,
            Enumerable.Range(0, 12).Select(index => new RuleAttemptGroupRow($"shape-{index:D2}", 5, [], false)).ToArray(),
            false, false);

        var text = TrialReviewTextRenderer.RenderRuleAttempts(summary);

        Assert.Contains("shape-00", text, StringComparison.Ordinal);
        Assert.Contains("and 2 more; use --json for all", text, StringComparison.Ordinal);
        Assert.DoesNotContain("shape-10", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CorpusViewerTextShowsHashAndBoundedBoundaryAndOccurrencePositions()
    {
        var spans = Enumerable.Range(0, 12)
            .Select(index => new CorpusWordSpan($"word-{index:D2}", index * 6, 5, index * 6, 5)).ToArray();
        var document = new CorpusViewerDocument("corpus/one", "document/one", "word sequence", "sha256:document-hash",
            "Interim boundaries", spans, [new CorpusViewerWordform("word", spans.Length, spans)]);

        var text = CommandTextRenderer.Render(CommandOutcome<CorpusViewerDocument>.Success(document), asJson: false).Output;

        Assert.Contains("sha256:document-hash", text, StringComparison.Ordinal);
        Assert.Contains("word-00", text, StringComparison.Ordinal);
        Assert.Contains("UTF-16 [0, 5)", text, StringComparison.Ordinal);
        Assert.Contains("Occurrence", text, StringComparison.Ordinal);
        Assert.Contains("and 2 more; use --json for all", text, StringComparison.Ordinal);
        Assert.DoesNotContain("word-10", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsimonyCandidateTextShowsBeforeAfterFindingsAndUnmatchedApprovedIdentities()
    {
        var before = CandidateFinding("before-description", "before-evidence", 2);
        var after = CandidateFinding("after-description", "after-evidence", 1);
        var binding = new ParsimonyCandidateBinding("identity-digest", Baseline(), "dry-run/one", "proposal/one",
            "intent-digest", "content-digest", [], "effect-digest", "anchor-digest", "baseline-source",
            "candidate-source", "judgment-digest");
        var response = new ParsimonyCandidateEvidenceResponse(1, "measure/one", binding, null!, null!,
            [new ParsimonyCandidateFindingChange("finding/one", "changed", before, after)],
            new ParsimonyApprovedAnalysisPreservation("partial", 3, 2,
                [new ParsimonyApprovedAnalysisTuple("wordform-guid/one", "analysis-guid/one", "analysis-content/one")]));

        var text = CommandTextRenderer.Render(CommandOutcome<ParsimonyCandidateEvidenceResponse>.Success(response),
            asJson: false).Output;

        Assert.Contains("before-description", text, StringComparison.Ordinal);
        Assert.Contains("after-description", text, StringComparison.Ordinal);
        Assert.Contains("before-evidence", text, StringComparison.Ordinal);
        Assert.Contains("after-evidence", text, StringComparison.Ordinal);
        Assert.Contains("wordform-guid/one", text, StringComparison.Ordinal);
        Assert.Contains("analysis-guid/one", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsimonyCatalogAndNamedViewTextShowSchemaAndEvidenceIdentity()
    {
        var measure = new MeasureDefinition("measure/one", ParsimonyAxis.Parsimony, ParsimonyTier.Static,
            ParsimonyAttachmentKind.AuthoredObject, ParsimonyAuthoredObjectKind.AdhocProhibition, null,
            ["capability/one"], "guide:recipe/one", "query/one",
            new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.GreaterThan, 3, "threshold/one"),
            ["view/one"], ["citation/one"], "prohibitions");
        var definition = new ParsimonyViewDefinition("view/one", 4, "view-description", ["capability/one"], "object-guid");
        var catalog = new ParsimonyMeasureCatalogResponse(7, [measure], [definition]);
        var catalogText = CommandTextRenderer.Render(CommandOutcome<ParsimonyMeasureCatalogResponse>.Success(catalog),
            asJson: false).Output;
        var namedView = new ParsimonyNamedViewResponse("bundle/one", "view/one", 4, ParsimonyMeasureStatus.Computed,
            ["capability/one"], 9, 1, null, false, [], "items-digest/one", null)
        {
            ActiveCount = 5,
            SuppressedCount = 2,
            ResurfacedCount = 1,
            UnresolvedJudgmentCount = 1,
            JudgmentProjectionDigest = "judgment-digest/one",
        };
        var viewText = CommandTextRenderer.Render(CommandOutcome<ParsimonyNamedViewResponse>.Success(namedView),
            asJson: false).Output;

        Assert.Contains("AuthoredObject", catalogText, StringComparison.Ordinal);
        Assert.Contains("threshold/one", catalogText, StringComparison.Ordinal);
        Assert.Contains("> 3", catalogText, StringComparison.Ordinal);
        Assert.Contains("v4", catalogText, StringComparison.Ordinal);
        Assert.Contains("object-guid", catalogText, StringComparison.Ordinal);
        Assert.Contains("bundle/one", viewText, StringComparison.Ordinal);
        Assert.Contains("capability/one", viewText, StringComparison.Ordinal);
        Assert.Contains("items-digest/one", viewText, StringComparison.Ordinal);
        Assert.Contains("5 active", viewText, StringComparison.Ordinal);
        Assert.Contains("judgment-digest/one", viewText, StringComparison.Ordinal);
    }

    [Fact]
    public void GrammarReadTextShowsAbbreviationsFormMetadataAndBaselineIdentity()
    {
        var response = new GrammarReadResponse(GrammarReadKind.lexical_entry, 3, 25,
            [
                new GrammarNaturalClassRead("class/one", "class-name", "class-abbr", "segment", [], []),
                new GrammarPartOfSpeechRead("category/one", "category-name", "category-abbr", null),
                new GrammarInflectionFeatureRead("feature/one", "feature-name", "feature-abbr", "closed", []),
                new GrammarLexicalEntryRead("entry/one", "headword", "stem", "stem-id",
                    new GrammarFormRead("form/one", "stored-form", "qaa-x-private", "lexeme", "stem", "stem-id", []), [], []),
            ])
        {
            Baseline = Baseline(),
        };

        var text = CommandTextRenderer.Render(CommandOutcome<GrammarReadResponse>.Success(response), asJson: false).Output;

        Assert.Contains("class-abbr", text, StringComparison.Ordinal);
        Assert.Contains("category-abbr", text, StringComparison.Ordinal);
        Assert.Contains("feature-abbr", text, StringComparison.Ordinal);
        Assert.Contains("qaa-x-private", text, StringComparison.Ordinal);
        Assert.Contains("lexeme", text, StringComparison.Ordinal);
        Assert.Contains("project/one", text, StringComparison.Ordinal);
        Assert.Contains("sha256:", text, StringComparison.Ordinal);
    }

    private static ParsimonyFinding CandidateFinding(string description, string digest, long count) =>
        new("finding/one", "measure/one", ParsimonyAxis.Parsimony, ParsimonyTier.Static,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.AuthoredObject, "object-guid/one",
                ParsimonyAuthoredObjectKind.AdhocProhibition), "group/one",
            new ParsimonyMeasureNumber(count, 4, "objects"),
            new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.GreaterThan, 1, "threshold/one"), digest,
            [], "guide:parsimony/recipe", ["limitation/one"], ParsimonyVerification.NotRun)
        { Description = description, ItemNames = ["item/one"] };

    private static BaselineToken Baseline() => new("project/one", "sha256:" + new string('a', 64), "1",
        "2026-11-17T00:00:00Z", "sha256:" + new string('b', 64));
}
