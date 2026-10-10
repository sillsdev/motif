using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;

namespace SIL.Motif.App.ViewModels;

/// <summary>Shows the sound changes, affected readings, and remaining questions for retiring an alternate form.</summary>
public sealed class RetirementProposalReviewViewModel
{
    /// <summary>Creates an honest status view when some bound review evidence has not been recorded yet.</summary>
    public RetirementProposalReviewViewModel(RetirementReviewQueryResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.Applicable)
            throw new ArgumentException("The response does not describe a retirement Draft.", nameof(response));
        HasCompleteReview = response.Review is not null;
        PendingEvidenceText = response.Unavailable.Count == 0 ? string.Empty : string.Join(" ", response.Unavailable);
        if (response.Review is { } review) Initialize(review);
        else
        {
            InitializeEmpty();
            if (response.DryRun is { } dryRun) InitializePendingDryRun(dryRun);
        }
    }

    /// <summary>Creates the window's linguist-facing view of a complete retirement review.</summary>
    public RetirementProposalReviewViewModel(RetirementProposalReviewProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        HasCompleteReview = true;
        PendingEvidenceText = string.Empty;
        Initialize(projection);
    }

    private void Initialize(RetirementProposalReviewProjection projection)
    {
        AffectedWordsText = CountText(projection.Statistics.WordformsRepointed.Total(), "word affected", "words affected");
        AffectedFormCasesText = CountText(projection.Statistics.AssessedFormCases,
            "form and writing system combination checked", "form and writing system combinations checked");
        var operations = projection.DryRun.Operations.ToDictionary(operation => operation.OperationId,
            StringComparer.Ordinal);
        Parts = projection.Parts.Select(part => new RetirementReviewPartViewModel(part, operations,
            projection.Parts, projection.AffectedReadings, projection.Statistics)).ToArray();
        StatisticsRows = OtherUseSummary(projection.Statistics);
        AffectedReadings = projection.AffectedReadings.Select(reading =>
            new RetirementAffectedReadingViewModel(reading)).ToArray();
        var readingCount = projection.AffectedReadings.Count;
        var preserved = projection.AffectedReadings.Count(item => item.VerificationStatus == "preserved");
        var recovered = projection.AffectedReadings.Count(item => item.VerificationStatus == "recovered");
        var missingBefore = projection.AffectedReadings.Count(item => item.VerificationStatus == "missingBefore");
        var lost = projection.AffectedReadings.Count(item => item.VerificationStatus == "lost");
        var incomplete = projection.AffectedReadings.Count(item => item.VerificationStatus is "incomplete" or
            "foundAfterBeforeIncomplete");
        ReadingResultsText = $"{readingCount} Approved readings: {preserved} preserved, {recovered} recovered, " +
            $"{missingBefore} missing before, {lost} lost, {incomplete} incomplete";
        FindingState = projection.Finding.Resolved ? "Resolved" : "Still present";
        FindingCountsText = $"Problem examples: {projection.Finding.BeforeNumerator} of " +
            $"{projection.Finding.BeforeDenominator} before; {projection.Finding.AfterNumerator} of " +
            $"{projection.Finding.AfterDenominator} after";
        UnresolvedText = UnresolvedSentence(projection.UnresolvedReferences);
        HasUnavailable = projection.Unavailable.Count > 0;
        HasUnlinkedEffects = projection.UnlinkedEffects.Count > 0;
        HasUnlinkedOperations = projection.UnlinkedOperationIds.Count > 0;
    }

    private void InitializeEmpty()
    {
        HasDryRun = false;
        DryRunSummaryText = string.Empty;
        AffectedWordsText = string.Empty;
        AffectedFormCasesText = string.Empty;
        StatisticsRows = [];
        Parts = [];
        AffectedReadings = [];
        ReadingResultsText = string.Empty;
        FindingState = string.Empty;
        FindingCountsText = string.Empty;
        UnresolvedText = string.Empty;
        HasUnavailable = true;
        HasUnlinkedEffects = false;
        HasUnlinkedOperations = false;
    }

    private void InitializePendingDryRun(DryRunProjection dryRun)
    {
        HasDryRun = true;
        var operations = dryRun.Operations;
        var rule = operations.Count(item => item.Kind.StartsWith("grammar/ph", StringComparison.Ordinal));
        var bundle = operations.Count(item => item.Kind is "analysis/wfiMorphBundle/setMorph" or
            "analysis/wfiMorphBundle/setForm");
        var references = operations.Count(item => item.Kind.StartsWith("grammar/mo", StringComparison.Ordinal));
        var deleted = operations.Count(item => item.Kind == "lexical/lexEntry/deleteAlternateForm");
        var lines = new List<string>
        {
            $"The latest Dry Run contains {CountText(operations.Count, "operation", "operations")} and " +
            $"{CountText(dryRun.Effects.Count, "recorded effect", "recorded effects")}.",
        };
        if (rule > 0) lines.Add($"Sound class and rule changes: {rule}.");
        if (bundle > 0) lines.Add($"Affected analysis changes: {bundle}.");
        if (references > 0) lines.Add($"Other reference changes: {references}.");
        if (deleted > 0) lines.Add($"Alternate forms marked for deletion: {deleted}.");
        DryRunSummaryText = string.Join(" ", lines);
    }

    /// <summary>How many words have an analysis that now uses the remaining form.</summary>
    public string AffectedWordsText { get; private set; } = string.Empty;

    /// <summary>How many written forms were checked in the affected language varieties.</summary>
    public string AffectedFormCasesText { get; private set; } = string.Empty;

    /// <summary>Additional changed uses, shown only when at least one exists.</summary>
    public IReadOnlyList<string> StatisticsRows { get; private set; } = [];

    /// <summary>The sound changes and form changes in this retirement.</summary>
    public IReadOnlyList<RetirementReviewPartViewModel> Parts { get; private set; } = [];

    /// <summary>The Approved readings checked before and after the sound changes.</summary>
    public IReadOnlyList<RetirementAffectedReadingViewModel> AffectedReadings { get; private set; } = [];

    /// <summary>The number and outcome of the Approved readings shown below.</summary>
    public string ReadingResultsText { get; private set; } = string.Empty;

    /// <summary>Whether the issue the sound change addressed remains present.</summary>
    public string FindingState { get; private set; } = string.Empty;

    /// <summary>The number of problem examples before and after the sound changes.</summary>
    public string FindingCountsText { get; private set; } = string.Empty;

    /// <summary>The plain-language explanation of any references without a replacement destination.</summary>
    public string UnresolvedText { get; private set; } = string.Empty;

    /// <summary>Whether the complete projection has paired Reports and affected-reading results.</summary>
    public bool HasCompleteReview { get; }

    /// <summary>Whether the panel is waiting for evidence before it can compare each reading.</summary>
    public bool HasPendingEvidence => !HasCompleteReview;

    /// <summary>What evidence is waiting when a complete reading comparison is not yet available.</summary>
    public string PendingEvidenceText { get; }

    /// <summary>Whether this pending review has the staged Proposal's latest Dry Run.</summary>
    public bool HasDryRun { get; private set; }

    /// <summary>A plain-language summary of the Proposal's changes while parser evidence is pending.</summary>
    public string DryRunSummaryText { get; private set; } = string.Empty;

    /// <summary>Whether a reference still needs a replacement destination.</summary>
    public bool HasUnresolved => UnresolvedText.Length > 0;

    /// <summary>Whether some requested evidence could not be checked.</summary>
    public bool HasUnavailable { get; private set; }

    /// <summary>Whether a changed value could not be connected to one of the named parts.</summary>
    public bool HasUnlinkedEffects { get; private set; }

    /// <summary>Whether the review contains another change that is not part of the sound-rule retirement.</summary>
    public bool HasUnlinkedOperations { get; private set; }

    private static IReadOnlyList<string> OtherUseSummary(RetirementReviewStatistics statistics)
    {
        if (statistics.OtherReferences == 0) return [];
        return [CountText(statistics.OtherReferences, "other reference affected", "other references affected")];
    }

    private static string UnresolvedSentence(RetirementUnresolvedReferenceReview unresolved)
    {
        var needs = new List<string>();
        AddNeed(needs, unresolved.UnresolvedApprovedAnalyses, "Approved analysis", "Approved analyses");
        AddNeed(needs, unresolved.UnresolvedAdhocRules, "restriction", "restrictions");
        AddNeed(needs, unresolved.OtherReferences + unresolved.CustomReferences, "other use", "other uses");
        if (needs.Count == 0) return string.Empty;
        var references = unresolved.UnresolvedApprovedAnalyses + unresolved.UnresolvedAdhocRules +
            unresolved.OtherReferences + unresolved.CustomReferences;
        return $"This form cannot be retired yet: {Join(needs)} {(references == 1 ? "needs" : "need")} a destination.";
    }

    private static void AddNeed(ICollection<string> needs, int count, string singular, string plural)
    {
        if (count > 0) needs.Add(CountText(count, singular, plural));
    }

    private static string Join(IReadOnlyList<string> items) => items.Count switch
    {
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}",
    };

    private static string CountText(long count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";
}

/// <summary>One connected part of the retirement, described with forms and sound changes.</summary>
public sealed class RetirementReviewPartViewModel
{
    /// <summary>Creates a readable summary from the part and its linked evidence.</summary>
    public RetirementReviewPartViewModel(RetirementReviewPart part,
        IReadOnlyDictionary<string, ProposalOperationView> operations,
        IReadOnlyList<RetirementReviewPart> allParts,
        IReadOnlyList<RetirementAffectedReading> readings,
        RetirementReviewStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(operations);
        Title = PartTitle(part.Id);
        Summaries = BuildSummaries(part, operations, allParts, readings, statistics);
    }

    /// <summary>The plain-language name of this part.</summary>
    public string Title { get; }

    /// <summary>Readable summaries of the change represented by this part.</summary>
    public IReadOnlyList<string> Summaries { get; }

    /// <summary>Whether this part has any change to describe.</summary>
    public bool HasSummaries => Summaries.Count > 0;

    private static IReadOnlyList<string> BuildSummaries(RetirementReviewPart part,
        IReadOnlyDictionary<string, ProposalOperationView> operations,
        IReadOnlyList<RetirementReviewPart> allParts,
        IReadOnlyList<RetirementAffectedReading> readings,
        RetirementReviewStatistics statistics)
    {
        var linked = part.OperationIds.Where(operations.ContainsKey).Select(id => operations[id]).ToArray();
        var summaries = new List<string>();
        switch (part.Id)
        {
            case "rule":
                AddSoundClassNames(summaries, linked);
                AddSoundRule(summaries, part, linked);
                break;
            case "bundle":
                summaries.AddRange(FormChanges(part, readings));
                break;
            case "other-references":
                var restrictions = statistics.Adhoc.Sum(item => item.TargetOccurrences);
                if (restrictions > 0 && linked.Length > 0)
                    summaries.Add(restrictions == 1
                        ? "Restriction moved to the remaining form"
                        : "Restrictions moved to the remaining form");
                if (statistics.OtherReferences > 0 && summaries.Count == 0)
                    summaries.Add("Other uses moved to the remaining form");
                break;
            case "retired-form":
                AddRetiredForms(summaries, part, allParts);
                break;
            default:
                break;
        }

        return Array.AsReadOnly(summaries.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static void AddSoundClassNames(ICollection<string> summaries,
        IEnumerable<ProposalOperationView> operations)
    {
        foreach (var operation in operations.Where(item => item.Kind == "grammar/phNaturalClass/setName"))
        {
            var name = PayloadText(operation.AfterJson, "text");
            if (IsPlainLanguage(name)) summaries.Add($"Sound class: {name}");
        }
    }

    private static void AddSoundRule(ICollection<string> summaries, RetirementReviewPart part,
        IReadOnlyList<ProposalOperationView> operations)
    {
        var description = (part.Details ?? []).Select(item => item.Label)
            .FirstOrDefault(label => label.StartsWith("New sound rule:", StringComparison.Ordinal) &&
                IsPlainLanguage(label));
        var enabled = SoundRuleEnabled(operations);
        if (description is not null)
        {
            summaries.Add(enabled is null || description.EndsWith("(enabled)", StringComparison.Ordinal) ||
                    description.EndsWith("(disabled)", StringComparison.Ordinal)
                ? description
                : $"{description} ({(enabled.Value ? "enabled" : "disabled")})");
        }
        else if (enabled is { } state)
        {
            summaries.Add($"Sound rule {(state ? "enabled" : "disabled")}");
        }
        else if (operations.Any(item => item.Kind.StartsWith("grammar/ph", StringComparison.Ordinal)))
        {
            summaries.Add("Sound rule updated");
        }
    }

    private static bool? SoundRuleEnabled(IEnumerable<ProposalOperationView> operations)
    {
        foreach (var operation in operations)
        {
            if (operation.Kind == "grammar/phSegmentRule/clearDisabled") return true;
            if (operation.Kind != "grammar/phSegmentRule/setDisabled") continue;
            if (PayloadBoolean(operation.AfterJson, "value") is { } disabled) return !disabled;
        }

        return null;
    }

    private static IEnumerable<string> FormChanges(RetirementReviewPart part,
        IReadOnlyList<RetirementAffectedReading> readings)
    {
        foreach (var effect in part.Effects.Where(effect => effect.Field.EndsWith("/form", StringComparison.Ordinal)))
        foreach (var change in effect.Changes)
        {
            if (!IsPlainLanguage(change.Before) || !IsPlainLanguage(change.After)) continue;
            var wordformIds = (part.Details ?? []).Where(detail => detail.BundleId == effect.CanonicalId)
                .Select(detail => detail.WordformId).Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);
            var reading = readings.FirstOrDefault(item => item.WordformId is not null &&
                wordformIds.Contains(item.WordformId));
            yield return reading is null
                ? $"Form {change.Before} is replaced by {change.After}"
                : $"Analysis of {reading.Word} now uses form {change.After} instead of {change.Before}";
        }
    }

    private static void AddRetiredForms(ICollection<string> summaries, RetirementReviewPart part,
        IReadOnlyList<RetirementReviewPart> allParts)
    {
        foreach (var label in (part.Details ?? []).Select(item => item.Label)
                     .Where(label => label.StartsWith("Deleted form:", StringComparison.Ordinal) &&
                         IsPlainLanguage(label)))
            summaries.Add(label);

        if (summaries.Count > 0) return;
        foreach (var change in allParts.Where(item => item.Id == "bundle").SelectMany(item => item.Effects)
                     .Where(effect => effect.Field.EndsWith("/form", StringComparison.Ordinal))
                     .SelectMany(effect => effect.Changes))
        {
            if (IsPlainLanguage(change.Before) && IsPlainLanguage(change.After))
                summaries.Add($"Deleted form: {change.Before} (alternate of {change.After})");
        }
    }

    private static string PartTitle(string id) => id switch
    {
        "rule" => "Sound changes",
        "bundle" => "Affected analyses",
        "other-references" => "Restrictions and other uses",
        "retired-form" => "Retired forms",
        _ => "Other changes",
    };

    private static string? PayloadText(string? json, string property)
    {
        if (json is null) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool? PayloadBoolean(string? json, string property)
    {
        if (json is null) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(property, out var value) &&
                value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsPlainLanguage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.Trim();
        if (value.Contains('/') || value.Contains('.') || value.Contains("sha256:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("Wfi", StringComparison.OrdinalIgnoreCase) || value.Contains('[') || value.Contains(']'))
            return false;
        return !(value.Length >= 4 && char.IsAsciiLetterLower(value[0]) &&
            char.IsAsciiLetterLower(value[1]) && char.IsAsciiLetterLower(value[2]) && value[3] == ':');
    }
}

/// <summary>One checked reading, with its human opinion and outcome but no internal attribution identifiers.</summary>
public sealed class RetirementAffectedReadingViewModel(RetirementAffectedReading reading)
{
    /// <summary>The wordform whose Approved reading was checked.</summary>
    public string Word { get; } = reading.Word;

    /// <summary>The reading's original Opinion.</summary>
    public string Opinion { get; } = OpinionText(reading.Opinion);

    /// <summary>The sign shown beside the Opinion.</summary>
    public string OpinionGlyph { get; } = reading.OpinionGlyph;

    /// <summary>The parser result for this reading before and after the sound changes.</summary>
    public string Verification { get; } = VerificationText(reading.VerificationStatus);

    /// <summary>The written form read back after the sound changes, when available.</summary>
    public string SurfaceText { get; } = SurfaceSummary(reading);

    /// <summary>Whether the sound rule was traced for this reading.</summary>
    public string RuleAttribution { get; } = reading.RuleAttributionStatus == "traced"
        ? "The sound rule was traced for this reading"
        : "The sound rule could not be traced for this reading";

    private static string OpinionText(string opinion) => opinion switch
    {
        "approved" => "Approved",
        "disapproved" => "Disapproved",
        "unknown" => "Unknown",
        "mixed" => "Mixed",
        _ => "Opinion unavailable",
    };

    private static string VerificationText(string status) => status switch
    {
        "preserved" => "Preserved",
        "recovered" => "Recovered",
        "missingBefore" => "Missing before",
        "lost" => "Lost",
        "foundAfterBeforeIncomplete" => "Found after; before incomplete",
        _ => "Unavailable",
    };

    private static string SurfaceSummary(RetirementAffectedReading value) => value.SurfaceAfter is { } after
        ? $"Written form: {after}"
        : "Written form after the change is unavailable";
}
