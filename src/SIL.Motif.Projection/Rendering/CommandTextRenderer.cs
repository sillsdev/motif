using SIL.Motif.Contract.Responses;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace SIL.Motif.Projection.Rendering;

/// <summary>
/// Turns each read-surface projection into the same text a reviewer reads at a terminal — a pure
/// function of the projection, never of anything the projection itself does not carry (ADR 0021
/// decision 2). <see cref="ProjectionJson.Serialize{T}"/> is the other renderer over the same
/// object, so every figure below also appears in that JSON by construction.
/// </summary>
public static class CommandTextRenderer
{
    public static string Render(ProjectSummaryProjection projection)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Project: {projection.ProjectName}");
        sb.AppendLine($"Lexical entries: {projection.LexicalEntryCount}");
        return sb.ToString();
    }

    public static string Render(ProposalListProjection projection)
    {
        var sb = new StringBuilder();
        if (projection.Proposals.Count == 0)
        {
            sb.AppendLine("No proposals in store.");
            return sb.ToString();
        }

        foreach (var item in projection.Proposals)
            sb.AppendLine($"{item.ProposalId}  {item.Status,-8}  {item.Label}");
        return sb.ToString();
    }

    public static string Render(ProposalDetailProjection projection)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Proposal {projection.ProposalId}");
        sb.AppendLine($"  status:              {projection.Status}");
        sb.AppendLine($"  label:               {projection.Label}");
        sb.AppendLine($"  comment:             {projection.Comment}");
        if (projection.CurrentIntentDigest is not null)
            sb.AppendLine($"  currentIntentDigest: {projection.CurrentIntentDigest}");
        if (projection.SupersededBy is not null)
            sb.AppendLine($"  supersededBy:        {projection.SupersededBy}");
        if (projection.ExtensionsJson is not null)
        {
            using var extensions = JsonDocument.Parse(projection.ExtensionsJson);
            sb.AppendLine($"  extensions:          {JsonSerializer.Serialize(extensions.RootElement)}");
        }
        sb.AppendLine($"  operations ({projection.Operations.Count}):");
        foreach (var op in projection.Operations)
        {
            sb.AppendLine($"    {op.OperationId}  ({op.Kind})");
            if (op.Target is not null)
                sb.AppendLine($"      target:    {op.Target}");
            if (op.EntityId is not null)
                sb.AppendLine($"      entityId:  {op.EntityId}");
            if (op.DependsOn.Count > 0)
                sb.AppendLine($"      dependsOn: {string.Join(", ", op.DependsOn)}");
            if (op.AfterJson is not null)
                sb.AppendLine($"      after:     {op.AfterJson}");
        }
        return sb.ToString();
    }

    public static string Render(CorpusListProjection projection)
    {
        var sb = new StringBuilder();
        if (projection.Corpora.Count == 0)
        {
            sb.AppendLine("No corpora in store.");
            return sb.ToString();
        }

        foreach (var corpus in projection.Corpora)
        {
            sb.AppendLine(corpus.CorpusId);
            sb.AppendLine($"  {corpus.Description}");
            sb.AppendLine(
                $"  {corpus.DocumentCount} document(s); {corpus.DerivableDocumentCount} permit derived works");
            sb.AppendLine(
                $"  accuracy figures: {(corpus.SupportsAccuracyClaims ? "permitted" : "not computable — no attestation")}");
        }

        return sb.ToString();
    }

    public static string Render(CorpusDetailProjection projection)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Corpus:       {projection.CorpusId}");
        sb.AppendLine($"Origin:       {projection.Description}");
        if (projection.Uri is not null) sb.AppendLine($"Location:     {projection.Uri}");
        sb.AppendLine($"Retrieved:    {projection.RetrievedUtc}");
        sb.AppendLine($"Licence:      {projection.Licence ?? "(none recorded)"}");
        sb.AppendLine($"Tokenisation: {projection.Tokeniser} {projection.TokeniserVersion}");
        if (projection.TokeniserNotes is not null)
            sb.AppendLine($"              {projection.TokeniserNotes}");

        sb.AppendLine();
        sb.AppendLine(projection.AccuracyStatement);
        sb.AppendLine();
        sb.AppendLine($"Documents ({projection.Documents.Count}):");
        foreach (var document in projection.Documents)
        {
            sb.AppendLine($"  {document.DocumentId}  {document.Title}");
            sb.AppendLine($"    {document.CharacterCount:N0} characters, sha256 {document.ContentSha256[..12]}...");
            sb.AppendLine(
                $"    licence: {document.Licence ?? "(none recorded)"}; derived works: " +
                (document.PermitsDerivedArtefacts ? "permitted" : "not permitted"));
        }

        sb.AppendLine();
        sb.AppendLine(projection.DerivationStatement);
        return sb.ToString();
    }

    public static string Render(AnalysisAggregateProjection projection)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Analysis aggregate");
        sb.AppendLine($"Word forms: {projection.WordFormCount}");
        sb.AppendLine(projection.ProjectContext?.Source switch
        {
            "baseline" => "Manually approved analyses from the captured Baseline:",
            "saved-project" => "Manually approved analyses from the saved FieldWorks project:",
            _ => "Manual analysis source not recorded:",
        });
        if (projection.ProjectContext is { } context)
        {
            sb.AppendLine($"Source saved: {context.SourceLastWriteUtc:O}");
            if (context.Baseline is { } baseline)
                sb.AppendLine($"Baseline: {baseline.BundleDigest}, captured {baseline.CapturedUtc}");
        }

        foreach (var wordForm in projection.WordForms)
        {
            sb.AppendLine($"  {wordForm.Form}  {wordForm.WordformGuid}");
            sb.AppendLine($"    manually approved analyses: {wordForm.ManualAnalysisCount}");
            foreach (var analysis in wordForm.ManualAnalyses)
            {
                sb.AppendLine($"      {analysis.ContentDigest}");
                sb.AppendLine($"        {analysis.MorphBreakdown}");
                sb.AppendLine($"        occurrences: {analysis.OccurrenceCount}");
                foreach (var occurrence in analysis.Occurrences)
                    sb.AppendLine($"          {occurrence.SegmentGuid}[{occurrence.AnalysisIndex}]");
            }

            if (projection.AssessmentCases is not null)
            {
                continue;
            }
            if (wordForm.AutomaticAnalyses is null)
            {
                sb.AppendLine("    automatic analyses: not covered");
            }
            else
            {
                sb.AppendLine($"    automatic analyses: {wordForm.AutomaticAnalysisCount}");
                foreach (var analysis in wordForm.AutomaticAnalyses)
                {
                    sb.AppendLine($"      {analysis.ContentDigest}");
                    sb.AppendLine($"        {analysis.MorphBreakdown}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine(projection.AssessmentState);
        if (projection.AssessmentCases is { } cases)
        {
            sb.AppendLine($"Recorded Assessment cases: {cases.Count}");
            if (cases.Any(item => item.Correctness is not null))
                sb.AppendLine("Approved matches use expectations frozen when the Assessment was produced.");
            foreach (var item in cases)
            {
                var evidence = item.Morphology;
                var limits = string.Join(" and ", new[]
                    { evidence.Capped ? "step limit" : null, evidence.TimedOut ? "time limit" : null }.OfType<string>());
                var completion = evidence.Capped || evidence.TimedOut
                    ? $"INCOMPLETE — parsing did not finish ({limits})"
                    : evidence.InvalidShape ? "Not attempted: invalid shape" : "Search completed";
                sb.AppendLine($"  Case {evidence.Index}: {evidence.Word}: {completion}");
                sb.AppendLine($"    Elapsed: {evidence.ElapsedMs} ms");
                if (item.Correctness is { } correctness)
                    sb.AppendLine($"    {correctness.Matched}/{correctness.Expected} approved readings matched; {correctness.Status}");
                else
                    sb.AppendLine("    Approved expectations were not collected.");
                sb.AppendLine($"    Parser readings: {evidence.Analyses.Count}");
                foreach (var analysis in evidence.Analyses)
                    sb.AppendLine("      " + string.Join(" -> ", analysis.Morphs.Select(morph =>
                        $"Form={morph.Form}; MSA={morph.Msa}; InflType={morph.InflType ?? "absent"}; guessed={morph.GuessedString ?? "absent"}")));
                foreach (var reason in item.Correctness?.Unavailable ?? evidence.Unavailable)
                    sb.AppendLine($"    Unavailable: {reason}");
            }
        }
        if (projection.GrammarWarnings is { Count: > 0 } warnings)
        {
            sb.AppendLine();
            sb.AppendLine($"PanGloss messages: {warnings.Count}");
            foreach (var warning in warnings)
                sb.AppendLine($"  {warning}");
        }
        if (projection.UnanalysedReach is { } reach)
        {
            sb.AppendLine();
            sb.AppendLine($"Unanalysed word forms: {reach.UnanalysedCount}");
            sb.AppendLine($"Parsed unanalysed word forms: {reach.ParsedCount}");
            sb.AppendLine(reach.Statement);
        }
        return sb.ToString();
    }

    public static string Render(AppliedLogProjection projection)
    {
        var sb = new StringBuilder();
        var noun = projection.EntryCount == 1 ? "entry" : "entries";
        sb.AppendLine($"Applied-change log for '{projection.ProjectPath}' ({projection.EntryCount} Motif {noun}):");

        foreach (var entry in projection.Entries)
        {
            sb.AppendLine(
                $"  {entry.ProposalId}  ts={entry.TimestampUtc}  user='{entry.User}'  " +
                $"intentDigest={entry.IntentDigest}  description=\"{entry.Description}\"");
        }

        foreach (var diagnostic in projection.Diagnostics)
            sb.AppendLine(diagnostic);

        return sb.ToString();
    }

    public static string Render(DryRunProjection projection)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DryRun of Proposal {projection.ProposalId}");
        sb.AppendLine($"  intentDigest: {projection.IntentDigest}");
        sb.AppendLine($"  baseline:     {projection.BaselineNote}");
        if (projection.Operations.Count > 0)
        {
            sb.AppendLine($"  operations ({projection.Operations.Count}):");
            foreach (var operation in projection.Operations)
            {
                sb.AppendLine($"    {operation.OperationId}  ({operation.Kind})");
                if (operation.DependsOn.Count > 0)
                    sb.AppendLine($"      dependsOn: {string.Join(", ", operation.DependsOn)}");
            }
        }
        sb.AppendLine($"  effects ({projection.Effects.Count}):");
        AppendEffects(sb, projection.Effects);
        sb.AppendLine($"  effectDigest: {projection.EffectDigest}");
        sb.AppendLine($"  footprintDigest: {projection.FootprintDigest}");
        sb.AppendLine("  (bound-DryRun anchor recorded on the manifest; 'apply' will require it)");
        return sb.ToString();
    }

    public static string Render(RetirementProposalReviewProjection projection)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"One Proposal: {projection.ProposalId}");
        sb.AppendLine($"  intentDigest: {projection.IntentDigest}");
        sb.AppendLine("  This is one connected change; the displayed parts cannot be applied separately.");
        var statistics = projection.Statistics;
        sb.AppendLine($"  bundles repointed: {Counts(statistics.BundlesRepointed)}");
        sb.AppendLine($"  analyses repointed: {Counts(statistics.AnalysesRepointed)}");
        sb.AppendLine($"  words affected: {Counts(statistics.WordformsRepointed)}");
        sb.AppendLine($"  affected form cases checked: {statistics.AssessedFormCases}");
        sb.AppendLine($"  other references moved: {statistics.OtherReferences}");
        sb.AppendLine($"  objects deleted: {statistics.OwnedObjectsDeleted}");
        sb.AppendLine($"  bundle Form alternatives changed: {statistics.BundleTextAlternativesChanged}; " +
            $"cleared: {statistics.BundleTextAlternativesCleared}");
        foreach (var counts in statistics.Adhoc.OrderBy(item => item.Grouped).ThenBy(item => item.Enabled))
            sb.AppendLine($"  ad hoc ({(counts.Grouped ? "grouped" : "single")}, " +
                $"{(counts.Enabled ? "enabled" : "disabled")}): {counts.Rules} rules, " +
                $"{counts.TargetOccurrences} occurrences");

        foreach (var part in projection.Parts)
        {
            sb.AppendLine($"  {part.Title}:");
            foreach (var id in part.OperationIds)
            {
                var operation = projection.DryRun.Operations.Single(item => item.OperationId == id);
                sb.AppendLine($"    {id} ({operation.Kind})");
                if (operation.DependsOn.Count > 0)
                    sb.AppendLine($"      dependsOn: {string.Join(", ", operation.DependsOn)}");
            }
            AppendEffects(sb, part.Effects);
            foreach (var detail in part.Details)
            {
                sb.AppendLine($"    {detail.Kind}: {detail.Id}  {detail.Label}");
                if (detail.Opinion is not null) sb.AppendLine($"      Opinion: {detail.Opinion}");
                if (detail.BeforeText is not null || detail.AfterText is not null)
                    sb.AppendLine($"      bundle Form: {detail.BeforeText ?? "(absent)"} → {detail.AfterText ?? "(absent)"}");
            }
        }

        sb.AppendLine($"  tested readings ({projection.AffectedReadings.Count}):");
        foreach (var reading in projection.AffectedReadings)
        {
            sb.AppendLine($"    {reading.OpinionGlyph} {reading.Opinion}  {reading.Word}: " +
                $"{reading.VerificationStatus}; surface {reading.SurfaceBefore} → " +
                $"{reading.SurfaceAfter ?? "unavailable"}");
            sb.AppendLine($"      rule attribution: {reading.RuleAttributionStatus}" +
                (reading.RuleId is null ? string.Empty : $" ({reading.RuleId})") +
                (reading.AttributionDigest is null ? string.Empty : $" · {reading.AttributionDigest}"));
        }
        sb.AppendLine($"  finding: {(projection.Finding.Resolved ? "resolved" : "still present")}; " +
            $"{projection.Finding.BeforeNumerator}/{projection.Finding.BeforeDenominator} before, " +
            $"{projection.Finding.AfterNumerator}/{projection.Finding.AfterDenominator} after");
        sb.AppendLine($"  disposition: {projection.Finding.Suppressed switch
        {
            true => "Suppressed",
            false => "Not suppressed",
            null => "Unavailable",
        }}");
        if (projection.UnresolvedReferences.UnresolvedApprovedAnalyses > 0 ||
            projection.UnresolvedReferences.UnresolvedAdhocRules > 0 ||
            projection.UnresolvedReferences.OtherReferences > 0 ||
            projection.UnresolvedReferences.CustomReferences > 0)
        {
            sb.AppendLine("  destination needed:");
            sb.AppendLine($"    {projection.UnresolvedReferences.UnresolvedApprovedAnalyses} distinct unresolved Approved analyses; " +
                $"{projection.UnresolvedReferences.UnresolvedAdhocRules} unresolved ad hoc rules; " +
                $"{projection.UnresolvedReferences.OtherReferences + projection.UnresolvedReferences.CustomReferences} other references");
            if (projection.UnresolvedReferences.Message.Length > 0)
                sb.AppendLine($"    {projection.UnresolvedReferences.Message}");
        }
        foreach (var unavailable in projection.Unavailable)
            sb.AppendLine($"  unavailable: {unavailable}");
        sb.AppendLine("  observed Dry Run effects:");
        AppendEffects(sb, projection.DryRun.Effects);
        return sb.ToString();
    }

    private static string Counts(SIL.Motif.Contract.Retirement.RetirementOpinionCounts counts) =>
        $"{counts.Approved} Approved, {counts.Disapproved} Disapproved, {counts.Unknown} Unknown, {counts.Mixed} Mixed";

    public static string Render(ApplyProjection projection)
    {
        var sb = new StringBuilder();
        if (projection.AlreadyApplied)
        {
            sb.AppendLine($"Proposal {projection.ProposalId} was already applied (idempotent; no mutation performed).");
            sb.AppendLine($"  {projection.ResultNote}");
        }
        else
        {
            sb.AppendLine($"Applied Proposal {projection.ProposalId}.");
            sb.AppendLine($"  {projection.ResultNote}");
            sb.AppendLine($"  effects ({projection.Effects.Count}):");
            AppendEffects(sb, projection.Effects);
            sb.AppendLine($"  effectDigest: {projection.EffectDigest}");
        }

        if (projection.Readiness is { } readiness)
        {
            sb.AppendLine($"  Readiness: {(readiness.IsReady ? "ready" : readiness.Forced ? "not ready; --force used" : "not ready")}");
            if (readiness.CorrectnessAssessmentExempt)
            {
                sb.AppendLine("    Correctness Assessment not required.");
                if (readiness.CorrectnessAssessmentExemptionReason is { } reason)
                    sb.AppendLine($"    reason: {reason}");
            }
            else
            {
                sb.AppendLine($"    Correctness Assessment required: {(readiness.CorrectnessAssessmentRequired ? "yes" : "no")}");
                foreach (var reason in readiness.Reasons)
                    sb.AppendLine($"    reason: {reason}");
            }
        }

        var logEntry = projection.AppliedLogEntry;
        sb.AppendLine(
            $"  applied-log entry: proposalId={logEntry.ProposalId} timestamp={logEntry.TimestampUtc} " +
            $"user='{logEntry.User}' intentDigest={logEntry.IntentDigest}");
        return sb.ToString();
    }

    public static string Render(ProjectConfigurationProjection projection)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Regression gate: {(projection.GateOnRegression ? "on" : "off")}");
        sb.AppendLine($"Purge on apply:  {(projection.PurgeOnApply ? "on" : "off")}");
        sb.AppendLine($"Scopes ({projection.Scopes.Count}):");
        foreach (var scope in projection.Scopes)
        {
            sb.AppendLine($"  {scope.Name}");
            sb.AppendLine($"    query:          {scope.Query}");
            sb.AppendLine($"    assessor:       {scope.Assessor}");
            sb.AppendLine(
                $"    collect:        {(scope.Collect.Count == 0 ? "(assessor default)" : string.Join(", ", scope.Collect))}");
            sb.AppendLine($"    per-word limit: {scope.PerWordLimitMs} ms");
            sb.AppendLine($"    per-word analysis attempts: {scope.PerWordStepLimit}");
        }
        return sb.ToString();
    }

    private static void AppendEffects(StringBuilder sb, IReadOnlyList<EffectView> effects)
    {
        foreach (var effect in effects)
        {
            sb.AppendLine($"    {effect.CanonicalId}  field={effect.Field}");
            if (effect.OperationIds.Count > 0)
                sb.AppendLine($"      operations: {string.Join(", ", effect.OperationIds)}");
            else
                sb.AppendLine("      operations: (no direct operation link)");

            if (effect.Changes.Count == 0)
            {
                sb.AppendLine("      (no observable before/after change)");
                continue;
            }

            foreach (var change in effect.Changes)
                sb.AppendLine($"      [{change.Ws}] \"{change.Before ?? "(absent)"}\" -> \"{change.After ?? "(absent)"}\"");
        }
    }
}
