using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;

namespace SIL.Motif.App.ViewModels;

/// <summary>The comparison between FieldWorks' stored readings and PanGloss's readings for one word.</summary>
public enum AnalysisMarkingClass
{
    /// <summary>The parser builds the approved reading and no other decision is needed.</summary>
    Same,

    /// <summary>The parser's result conflicts with an opinion or spelling mark in FieldWorks.</summary>
    Conflict,

    /// <summary>The parser proposes a reading that differs from the stored readings.</summary>
    Different,

    /// <summary>The parser builds an approved reading and proposes at least one additional reading.</summary>
    Extra,

    /// <summary>The parser completed without returning a reading.</summary>
    None,

    /// <summary>The parser stopped before completing its search.</summary>
    Capped,

    /// <summary>No Assessment result is available for the word.</summary>
    NotAssessed,
}

/// <summary>The action presented for one comparison or one explicit Fix choice.</summary>
public enum AnalysisMarkingActionKind
{
    /// <summary>Approve a stored analysis.</summary>
    Approve,

    /// <summary>Add PanGloss's reading as approved while retaining a rejected stored reading.</summary>
    Accept,

    /// <summary>Add a parser reading without changing its opinion from Unknown.</summary>
    Add,

    /// <summary>Keep FieldWorks unchanged and mark the row as read.</summary>
    KeepFieldWorks,

    /// <summary>Disapprove an analysis.</summary>
    Disapprove,

    /// <summary>Return an analysis to Unknown.</summary>
    MakeUnknown,
}

/// <summary>A stored analysis and the human opinion that FieldWorks records for it.</summary>
/// <param name="StoredAnalysisId">The stable identity used to target an opinion change.</param>
/// <param name="Opinion">The value from <see cref="ReadingGrade"/> for this stored analysis.</param>
/// <param name="Morphs">The stored reading as parser-comparable morphs.</param>
public sealed record FieldWorksAnalysisMarking(
    string StoredAnalysisId, string Opinion, IReadOnlyList<ParserReadingMorph> Morphs);

/// <summary>A PanGloss reading together with the FieldWorks analyses it matches.</summary>
/// <param name="Analysis">The semantic parser reading.</param>
/// <param name="Display">The reading formatted for the window, when available.</param>
/// <param name="MatchingAnalysisIds">Stored analysis identities with the same morphology.</param>
/// <param name="MatchingOpinions">Distinct stored opinions for the matching analyses.</param>
public sealed record PanGlossReadingMarking(
    ParseAnalysis Analysis, ParserReading? Display, IReadOnlyList<string> MatchingAnalysisIds,
    string? MatchingOpinions)
{
    /// <summary>Whether a FieldWorks analysis has the same morphology.</summary>
    public bool MatchesStored => MatchingAnalysisIds.Count > 0;

    /// <summary>Whether PanGloss produced a reading absent from FieldWorks.</summary>
    public bool IsParserOnly => !MatchesStored;
}

/// <summary>A named Fix choice for changing one stored opinion or adding one parser reading.</summary>
/// <param name="Kind">The action's role in the window.</param>
/// <param name="Label">The visible choice name.</param>
/// <param name="Subtitle">The opinion transition explained to the reader.</param>
/// <param name="StoredAnalysisId">The stored reading to change, when this choice changes one.</param>
/// <param name="Reading">The parser reading to add or approve, when this choice uses one.</param>
/// <param name="ReadingIndex">The parser reading's position in the Assessment.</param>
/// <param name="Now">The current state shown in the staged transition.</param>
/// <param name="AfterApply">The state after the pending change is applied.</param>
/// <param name="ChangeKind">The pending change kind to send to the command client.</param>
public sealed record AnalysisMarkingChoice(
    AnalysisMarkingActionKind Kind, string Label, string Subtitle, string? StoredAnalysisId,
    ParseAnalysis? Reading, int? ReadingIndex, string Now, string AfterApply, string? ChangeKind);

/// <summary>An action selected from the primary row control or its Fix menu.</summary>
/// <param name="Kind">The action's role in the window.</param>
/// <param name="Label">The visible action name.</param>
/// <param name="StoredAnalysisId">The stored reading to change, when this action changes one.</param>
/// <param name="Reading">The parser reading to add or approve, when this action uses one.</param>
/// <param name="ReadingIndex">The parser reading's position in the Assessment.</param>
/// <param name="Now">The current state shown in the staged transition.</param>
/// <param name="AfterApply">The state after the pending change is applied.</param>
/// <param name="ChangeKind">The pending change kind to send to the command client.</param>
public sealed record AnalysisMarkingAction(
    AnalysisMarkingActionKind Kind, string Label, string? StoredAnalysisId,
    ParseAnalysis? Reading, int? ReadingIndex, string Now, string AfterApply, string? ChangeKind);

/// <summary>The visible transition for one pending analysis change.</summary>
/// <param name="Now">The current opinion or presence.</param>
/// <param name="AfterApply">The opinion or presence after Apply.</param>
/// <param name="StoredAnalysisId">The stored analysis addressed by this change.</param>
/// <param name="ReadingIndex">The Assessment reading addressed by this change.</param>
/// <param name="FitStatus">The change's current fit with the project.</param>
public sealed record StagedMarkingTransition(string Now, string AfterApply,
    string? StoredAnalysisId = null, int? ReadingIndex = null, string? FitStatus = null)
{
    /// <summary>The transition as a short line of text.</summary>
    public string Text => $"{Now} → {AfterApply}";
}

/// <summary>The complete FieldWorks and PanGloss evidence for one text occurrence.</summary>
/// <param name="FieldWorksAnalyses">Every stored analysis and its current opinion.</param>
/// <param name="PanGlossClass">The comparison class after stored opinions are considered.</param>
/// <param name="PanGlossReadings">Every parser reading and any matching stored analyses.</param>
/// <param name="PrimaryAction">The table-selected action, or <see langword="null"/> when no one-click action applies.</param>
/// <param name="FixChoices">Explicit choices for changing an opinion or adding a reading.</param>
/// <param name="StagedTransitions">The pending changes displayed on this occurrence.</param>
/// <param name="IsUncertain">Whether the pending change's fit with the current project is uncertain.</param>
/// <param name="NoLongerFits">Whether the pending change no longer fits the current project.</param>
/// <param name="IsUnread">Whether the occurrence has no persisted Read marker.</param>
/// <remarks>Reading opinions and staged changes follow the Approved, Disapproved, and Unknown model in ADR 0049.</remarks>
public sealed record AnalysisMarkingState(
    IReadOnlyList<FieldWorksAnalysisMarking> FieldWorksAnalyses,
    AnalysisMarkingClass PanGlossClass,
    IReadOnlyList<PanGlossReadingMarking> PanGlossReadings,
    AnalysisMarkingAction? PrimaryAction,
    IReadOnlyList<AnalysisMarkingChoice> FixChoices,
    IReadOnlyList<StagedMarkingTransition> StagedTransitions,
    bool IsUncertain,
    bool NoLongerFits,
    bool IsUnread)
{
    /// <summary>Whether an available action or Fix choice remains unstaged.</summary>
    public bool NeedsALook => StagedTransitions.Count == 0 && (PrimaryAction is not null || FixChoices.Count > 0);

    private static readonly IReadOnlyDictionary<(AnalysisMarkingClass Class, string Opinion), PrimaryActionRule?>
        PrimaryActionTable = new Dictionary<(AnalysisMarkingClass, string), PrimaryActionRule?>
        {
            [(AnalysisMarkingClass.Same, ReadingGrade.Approved)] = null,
            [(AnalysisMarkingClass.Same, ReadingGrade.Candidate)] =
                new(AnalysisMarkingActionKind.Approve, "Approve", ChangeKinds.Approve, "Unknown", "Approved",
                    PrimaryActionTarget.MatchedReading),
            [(AnalysisMarkingClass.Different, ReadingGrade.NoOpinion)] =
                new(AnalysisMarkingActionKind.Add, "Add", ChangeKinds.AddCandidate, "Not in FieldWorks", "Unknown",
                    PrimaryActionTarget.ParserOnlyReading),
            [(AnalysisMarkingClass.Different, ReadingGrade.Candidate)] =
                new(AnalysisMarkingActionKind.Add, "Add", ChangeKinds.AddCandidate, "Not in FieldWorks", "Unknown",
                    PrimaryActionTarget.ParserOnlyReading),
            [(AnalysisMarkingClass.Different, ReadingGrade.Disapproved)] =
                new(AnalysisMarkingActionKind.Accept, "Accept", ChangeKinds.Approve, "Not in FieldWorks", "Approved",
                    PrimaryActionTarget.ParserOnlyReading),
            [(AnalysisMarkingClass.Extra, ReadingGrade.Approved)] =
                new(AnalysisMarkingActionKind.KeepFieldWorks, "Keep", null, string.Empty, string.Empty,
                    PrimaryActionTarget.None),
            [(AnalysisMarkingClass.None, ReadingGrade.Approved)] = null,
            [(AnalysisMarkingClass.Capped, ReadingGrade.Approved)] = null,
        };

    /// <summary>Builds a word's comparison from its occurrence and Assessment evidence.</summary>
    /// <param name="token">The occurrence and its stored FieldWorks analyses.</param>
    /// <param name="result">The word's Assessment result, or <see langword="null"/> when it was not assessed.</param>
    /// <returns>The comparison and the actions supported by the available evidence.</returns>
    public static AnalysisMarkingState Create(TextToken token, AssessmentWordResult? result, bool isUnread = true)
    {
        ArgumentNullException.ThrowIfNull(token);
        var stored = token.StoredAnalyses.Select(analysis => new FieldWorksAnalysisMarking(
            analysis.StoredAnalysisId ?? string.Empty,
            analysis.StoredAnalysisOpinion ?? ReadingGrade.Candidate,
            analysis.Morphs)).ToArray();
        var parses = result?.Morphology?.Analyses ?? [];
        var renderings = result?.Readings;
        var readings = parses.Select((analysis, index) =>
        {
            var display = renderings is not null && index < renderings.Count ? renderings[index] : null;
            var matches = token.StoredAnalyses.Where(storedAnalysis =>
                storedAnalysis.Identity is { } identity && AnalysisMorphologyMatcher.Matches(analysis, identity))
                .ToArray();
            var opinions = matches.Select(match => match.StoredAnalysisOpinion ?? ReadingGrade.Candidate)
                .Distinct(StringComparer.Ordinal).ToArray();
            return new PanGlossReadingMarking(analysis,
                display,
                matches.Select(match => match.StoredAnalysisId ?? string.Empty).ToArray(),
                opinions.Length == 0 ? null : string.Join(", ", opinions));
        }).ToArray();

        var markingClass = Classify(token.IncorrectSpelling, result, stored, readings);
        var primary = BuildPrimaryAction(markingClass, stored, readings);
        var fixes = BuildFixChoices(markingClass, stored, readings);
        return new AnalysisMarkingState(stored, markingClass, readings, primary, fixes, [],
            false, false, isUnread);
    }

    /// <summary>Builds the shared marking state from the resolved evidence for one Assessment word.</summary>
    public static AnalysisMarkingState Create(AssessmentWordResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var stored = result.StoredAnalyses
            .Where(reading => reading.StoredAnalysisId is not null)
            .GroupBy(reading => reading.StoredAnalysisId!, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(reading => new ProjectAnalysis(string.Empty, reading.Morphs)
            {
                StoredAnalysisId = reading.StoredAnalysisId,
                StoredAnalysisOpinion = reading.StoredAnalysisOpinion,
                Identity = reading.Identity,
            })
            .ToArray();
        var token = new TextToken(result.Word, result.Word, null, null)
        {
            IncorrectSpelling = result.ProjectStanding == ProjectStanding.IncorrectSpelling,
            StoredAnalyses = stored,
        };
        return Create(token, result);
    }

    /// <summary>Attaches pending transitions and their current fit statuses.</summary>
    /// <param name="transitions">The changes attached to this occurrence.</param>
    /// <returns>A copy with staged transitions and an already-read marker state.</returns>
    public AnalysisMarkingState WithStagedTransitions(IReadOnlyList<StagedMarkingTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        return this with
        {
            StagedTransitions = transitions,
            IsUncertain = transitions.Any(transition => transition.FitStatus == ChangeFitStatus.Uncertain),
            NoLongerFits = transitions.Any(transition => transition.FitStatus == ChangeFitStatus.NoLongerFits),
        };
    }

    private static AnalysisMarkingClass Classify(bool incorrectSpelling, AssessmentWordResult? result,
        IReadOnlyList<FieldWorksAnalysisMarking> stored, IReadOnlyList<PanGlossReadingMarking> readings)
    {
        if (result is null || result.Outcome == "skipped") return AnalysisMarkingClass.NotAssessed;
        if (incorrectSpelling) return AnalysisMarkingClass.Conflict;
        if (readings.Any(reading => reading.MatchingAnalysisIds.Any(id => stored.Any(analysis =>
                analysis.StoredAnalysisId == id && analysis.Opinion == ReadingGrade.Disapproved))))
            return AnalysisMarkingClass.Conflict;
        if (result.IsIncomplete) return AnalysisMarkingClass.Capped;
        if (readings.Count == 0) return AnalysisMarkingClass.None;
        if (stored.Any(analysis => analysis.Opinion == ReadingGrade.Approved &&
                !readings.Any(reading => reading.MatchingAnalysisIds.Contains(analysis.StoredAnalysisId,
                    StringComparer.Ordinal)))) return AnalysisMarkingClass.Conflict;
        if (readings.All(reading => reading.MatchesStored))
            return AnalysisMarkingClass.Same;
        if (readings.Any(reading => reading.IsParserOnly) && readings.Any(reading =>
                reading.MatchingAnalysisIds.Any(id => stored.Any(analysis => analysis.StoredAnalysisId == id &&
                    analysis.Opinion == ReadingGrade.Approved)))) return AnalysisMarkingClass.Extra;
        return AnalysisMarkingClass.Different;
    }

    private static AnalysisMarkingAction? BuildPrimaryAction(AnalysisMarkingClass markingClass,
        IReadOnlyList<FieldWorksAnalysisMarking> stored, IReadOnlyList<PanGlossReadingMarking> readings)
    {
        var opinion = ResolveOpinion(stored, readings);
        if (!PrimaryActionTable.TryGetValue((markingClass, opinion), out var rule) || rule is null) return null;
        if (rule.Target == PrimaryActionTarget.None)
            return new AnalysisMarkingAction(rule.Kind, rule.Label, null, null, null,
                rule.Now, rule.AfterApply, rule.ChangeKind);

        for (var index = 0; index < readings.Count; index++)
        {
            var reading = readings[index];
            if (rule.Target == PrimaryActionTarget.ParserOnlyReading && !reading.IsParserOnly) continue;
            if (rule.Target == PrimaryActionTarget.MatchedReading && !reading.MatchingAnalysisIds.Any(id =>
                    stored.Any(analysis => analysis.StoredAnalysisId == id && analysis.Opinion == opinion))) continue;
            var matched = rule.Target == PrimaryActionTarget.MatchedReading
                ? stored.First(analysis => reading.MatchingAnalysisIds.Contains(analysis.StoredAnalysisId,
                    StringComparer.Ordinal) && analysis.Opinion == opinion)
                : null;
            return new AnalysisMarkingAction(rule.Kind, rule.Label, matched?.StoredAnalysisId,
                reading.Analysis, index, rule.Now, rule.AfterApply, rule.ChangeKind);
        }
        return null;
    }

    private static IReadOnlyList<AnalysisMarkingChoice> BuildFixChoices(
        AnalysisMarkingClass markingClass, IReadOnlyList<FieldWorksAnalysisMarking> stored,
        IReadOnlyList<PanGlossReadingMarking> readings)
    {
        if (markingClass is AnalysisMarkingClass.NotAssessed or AnalysisMarkingClass.Capped or AnalysisMarkingClass.Same)
            return [];

        var choices = new List<AnalysisMarkingChoice>();
        if (markingClass == AnalysisMarkingClass.None && stored.Any(analysis =>
                analysis.Opinion == ReadingGrade.Approved))
        {
            choices.Add(Choice(AnalysisMarkingActionKind.KeepFieldWorks, "Keep FieldWorks", "Nothing staged",
                null, null, null, string.Empty, string.Empty, null));
            return choices;
        }

        var parserOnlyReadings = readings.Select((reading, index) => (reading, index))
            .Where(item => item.reading.IsParserOnly).ToArray();
        if (markingClass == AnalysisMarkingClass.Different && parserOnlyReadings.Length > 0 &&
            stored.Any(analysis => analysis.Opinion == ReadingGrade.Disapproved))
        {
            foreach (var (reading, index) in parserOnlyReadings)
            {
                choices.Add(Choice(AnalysisMarkingActionKind.Add, "Accept PanGloss's reading",
                    "Not in FieldWorks → Approved", null, reading.Analysis, index,
                    "Not in FieldWorks", "Approved", ChangeKinds.Approve));
                choices.Add(Choice(AnalysisMarkingActionKind.Add, "Add as Unknown",
                    "Not in FieldWorks → Unknown", null, reading.Analysis, index,
                    "Not in FieldWorks", "Unknown", ChangeKinds.AddCandidate));
            }
            choices.Add(Choice(AnalysisMarkingActionKind.KeepFieldWorks, "Keep FieldWorks", "Nothing staged",
                null, null, null, string.Empty, string.Empty, null));
            return choices;
        }

        foreach (var analysis in stored)
        {
            if (analysis.Opinion == ReadingGrade.Approved)
                choices.Add(Choice(AnalysisMarkingActionKind.Disapprove, "Disapprove",
                    "Approved → Disapproved", analysis.StoredAnalysisId, null, null,
                    "Approved", "Disapproved", ChangeKinds.Reject));
            else if (analysis.Opinion == ReadingGrade.Disapproved)
            {
                choices.Add(Choice(AnalysisMarkingActionKind.Approve, "Approve", "Disapproved → Approved",
                    analysis.StoredAnalysisId, null, null, "Disapproved", "Approved", ChangeKinds.Approve));
                choices.Add(Choice(AnalysisMarkingActionKind.MakeUnknown, "Make Unknown",
                    "Disapproved → Unknown", analysis.StoredAnalysisId, null, null,
                    "Disapproved", "Unknown", ChangeKinds.Candidate));
            }
            else
            {
                choices.Add(Choice(AnalysisMarkingActionKind.Approve, "Approve", "Unknown → Approved",
                    analysis.StoredAnalysisId, null, null, "Unknown", "Approved", ChangeKinds.Approve));
                choices.Add(Choice(AnalysisMarkingActionKind.Disapprove, "Disapprove",
                    "Unknown → Disapproved", analysis.StoredAnalysisId, null, null,
                    "Unknown", "Disapproved", ChangeKinds.Reject));
            }
        }

        for (var index = 0; index < readings.Count; index++)
        {
            var reading = readings[index];
            if (!reading.IsParserOnly) continue;
            choices.Add(Choice(AnalysisMarkingActionKind.Add, "Add as Unknown",
                "Not in FieldWorks → Unknown", null, reading.Analysis, index,
                "Not in FieldWorks", "Unknown", ChangeKinds.AddCandidate));
            var label = markingClass == AnalysisMarkingClass.Different &&
                stored.Any(analysis => analysis.Opinion == ReadingGrade.Disapproved)
                ? "Accept PanGloss's reading" : "Add as Approved";
            choices.Add(Choice(AnalysisMarkingActionKind.Add, label,
                "Not in FieldWorks → Approved", null, reading.Analysis, index,
                "Not in FieldWorks", "Approved", ChangeKinds.Approve));
            if (markingClass == AnalysisMarkingClass.Extra && stored.Any(analysis =>
                    analysis.Opinion == ReadingGrade.Approved))
                choices.Add(Choice(AnalysisMarkingActionKind.Disapprove, "Disapprove the extra reading",
                    "Not in FieldWorks → Disapproved", null, reading.Analysis, index,
                    "Not in FieldWorks", "Disapproved", ChangeKinds.Reject));
        }

        if (stored.Count > 0 && markingClass is AnalysisMarkingClass.Conflict or AnalysisMarkingClass.Different or
            AnalysisMarkingClass.Extra)
            choices.Add(Choice(AnalysisMarkingActionKind.KeepFieldWorks, "Keep FieldWorks", "Nothing staged",
                null, null, null, string.Empty, string.Empty, null));
        return choices;
    }

    private static AnalysisMarkingChoice Choice(AnalysisMarkingActionKind kind, string label, string subtitle,
        string? storedAnalysisId, ParseAnalysis? reading, int? readingIndex, string now, string afterApply,
        string? changeKind) => new(kind, label, subtitle, storedAnalysisId, reading, readingIndex,
        now, afterApply, changeKind);

    private static string ResolveOpinion(IReadOnlyList<FieldWorksAnalysisMarking> stored,
        IReadOnlyList<PanGlossReadingMarking> readings)
    {
        foreach (var reading in readings)
        {
            var matched = stored.FirstOrDefault(analysis => reading.MatchingAnalysisIds.Contains(
                analysis.StoredAnalysisId, StringComparer.Ordinal));
            if (matched is not null) return matched.Opinion;
        }
        return stored.FirstOrDefault()?.Opinion ?? ReadingGrade.NoOpinion;
    }

    private enum PrimaryActionTarget
    {
        None,
        MatchedReading,
        ParserOnlyReading,
    }

    private sealed record PrimaryActionRule(AnalysisMarkingActionKind Kind, string Label, string? ChangeKind,
        string Now, string AfterApply, PrimaryActionTarget Target);
}
