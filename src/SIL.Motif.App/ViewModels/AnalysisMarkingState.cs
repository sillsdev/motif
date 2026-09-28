using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

public enum AnalysisMarkingClass
{
    Same,
    Different,
    Extra,
    None,
    Capped,
    NotAssessed,
}

public enum AnalysisMarkingActionKind
{
    Approve,
    Accept,
    Add,
    KeepFieldWorks,
    Disapprove,
    MakeUnknown,
}

public sealed record FieldWorksAnalysisMarking(
    string StoredAnalysisId, string Opinion, IReadOnlyList<ParserReadingMorph> Morphs);

public sealed record PanGlossReadingMarking(
    ParseAnalysis Analysis, ParserReading? Display, IReadOnlyList<string> MatchingAnalysisIds,
    string? MatchingOpinions)
{
    public bool MatchesStored => MatchingAnalysisIds.Count > 0;
    public bool IsParserOnly => !MatchesStored;
}

public sealed record AnalysisMarkingChoice(
    AnalysisMarkingActionKind Kind, string Label, string Subtitle, string? StoredAnalysisId,
    ParseAnalysis? Reading, int? ReadingIndex, string Now, string AfterApply);

public sealed record AnalysisMarkingAction(
    AnalysisMarkingActionKind Kind, string Label, string? StoredAnalysisId,
    ParseAnalysis? Reading, int? ReadingIndex, string Now, string AfterApply);

public sealed record StagedMarkingTransition(string Now, string AfterApply)
{
    public string Text => $"{Now} → {AfterApply}";
}

public sealed record AnalysisMarkingState(
    IReadOnlyList<FieldWorksAnalysisMarking> FieldWorksAnalyses,
    AnalysisMarkingClass PanGlossClass,
    IReadOnlyList<PanGlossReadingMarking> PanGlossReadings,
    AnalysisMarkingAction? PrimaryAction,
    IReadOnlyList<AnalysisMarkingChoice> FixChoices,
    StagedMarkingTransition? StagedTransition,
    bool IsUncertain,
    bool NoLongerFits,
    bool NeedsALook)
{
    public static AnalysisMarkingState Create(TextToken token, AssessmentWordResult? result)
    {
        ArgumentNullException.ThrowIfNull(token);
        var stored = token.StoredAnalyses.Select(analysis => new FieldWorksAnalysisMarking(
            analysis.StoredAnalysisId ?? string.Empty,
            analysis.StoredAnalysisOpinion ?? "unknown",
            analysis.Morphs)).ToArray();
        var parses = result?.Morphology?.Analyses ?? [];
        var renderings = result?.Readings;
        var readings = parses.Select((analysis, index) =>
        {
            var matches = token.StoredAnalyses.Where(storedAnalysis => storedAnalysis.Identity is { } identity &&
                AnalysisMorphologyMatcher.Matches(analysis, identity)).ToArray();
            var opinions = matches.Select(match => match.StoredAnalysisOpinion ?? "unknown")
                .Distinct(StringComparer.Ordinal).ToArray();
            return new PanGlossReadingMarking(analysis,
                renderings is not null && index < renderings.Count ? renderings[index] : null,
                matches.Select(match => match.StoredAnalysisId ?? string.Empty).ToArray(),
                opinions.Length == 0 ? null : string.Join(", ", opinions));
        }).ToArray();

        var markingClass = Classify(result, stored, readings);
        var primary = BuildPrimaryAction(markingClass, stored, readings);
        var fixes = BuildFixChoices(markingClass, stored, readings);
        var notAssessed = markingClass == AnalysisMarkingClass.NotAssessed;
        return new AnalysisMarkingState(stored, markingClass, readings, primary, fixes, null,
            false, false, markingClass != AnalysisMarkingClass.Same || notAssessed);
    }

    public AnalysisMarkingState WithStagedTransition(string now, string afterApply, string? fitStatus = null)
    {
        ArgumentNullException.ThrowIfNull(now);
        ArgumentNullException.ThrowIfNull(afterApply);
        var uncertain = fitStatus == ChangeFitStatus.Uncertain;
        var noLongerFits = fitStatus == ChangeFitStatus.NoLongerFits;
        return this with
        {
            StagedTransition = new StagedMarkingTransition(now, afterApply),
            IsUncertain = uncertain,
            NoLongerFits = noLongerFits,
            NeedsALook = NeedsALook || uncertain || noLongerFits,
        };
    }

    public AnalysisMarkingState KeepFieldWorks() => this;

    private static AnalysisMarkingClass Classify(AssessmentWordResult? result,
        IReadOnlyList<FieldWorksAnalysisMarking> stored, IReadOnlyList<PanGlossReadingMarking> readings)
    {
        if (result is null || result.Outcome == "skipped") return AnalysisMarkingClass.NotAssessed;
        if (result.IsIncomplete) return AnalysisMarkingClass.Capped;
        if (readings.Count == 0) return AnalysisMarkingClass.None;
        if (readings.All(reading => reading.MatchesStored) && stored.All(analysis =>
                readings.Any(reading => reading.MatchingAnalysisIds.Contains(analysis.StoredAnalysisId,
                    StringComparer.Ordinal)))) return AnalysisMarkingClass.Same;
        if (readings.Any(reading => reading.IsParserOnly) && readings.Any(reading =>
                reading.MatchingAnalysisIds.Any(id => stored.Any(analysis => analysis.StoredAnalysisId == id &&
                    analysis.Opinion == "approved")))) return AnalysisMarkingClass.Extra;
        return AnalysisMarkingClass.Different;
    }

    private static AnalysisMarkingAction? BuildPrimaryAction(AnalysisMarkingClass markingClass,
        IReadOnlyList<FieldWorksAnalysisMarking> stored, IReadOnlyList<PanGlossReadingMarking> readings)
    {
        if (markingClass == AnalysisMarkingClass.None || markingClass == AnalysisMarkingClass.NotAssessed)
            return null;
        for (var index = 0; index < readings.Count; index++)
        {
            var reading = readings[index];
            var matched = stored.FirstOrDefault(analysis => reading.MatchingAnalysisIds.Contains(
                analysis.StoredAnalysisId, StringComparer.Ordinal));
            if (matched is null) continue;
            if (matched.Opinion == "approved") continue;
            var kind = matched.Opinion == "disapproved"
                ? AnalysisMarkingActionKind.Accept : AnalysisMarkingActionKind.Approve;
            var label = matched.Opinion == "disapproved" ? "Accept this analysis" : "Approve this analysis";
            return new AnalysisMarkingAction(kind, label, matched.StoredAnalysisId,
                reading.Analysis, index, OpinionLabel(matched.Opinion), "Approved");
        }
        if (markingClass == AnalysisMarkingClass.Different &&
            stored.Any(analysis => analysis.Opinion == "disapproved"))
        {
            for (var index = 0; index < readings.Count; index++)
                if (readings[index].IsParserOnly)
                    return new AnalysisMarkingAction(AnalysisMarkingActionKind.Accept,
                        "Accept PanGloss's reading", null, readings[index].Analysis, index,
                        "Not in FieldWorks", "Approved");
        }
        if (markingClass is AnalysisMarkingClass.Same or AnalysisMarkingClass.Extra)
            return new AnalysisMarkingAction(AnalysisMarkingActionKind.KeepFieldWorks, "Keep FieldWorks",
                null, null, null, string.Empty, string.Empty);
        for (var index = 0; index < readings.Count; index++)
            if (readings[index].IsParserOnly)
                return new AnalysisMarkingAction(AnalysisMarkingActionKind.Add, "Add", null,
                    readings[index].Analysis, index, "Not in FieldWorks", "Approved");
        return null;
    }

    private static IReadOnlyList<AnalysisMarkingChoice> BuildFixChoices(
        AnalysisMarkingClass markingClass, IReadOnlyList<FieldWorksAnalysisMarking> stored,
        IReadOnlyList<PanGlossReadingMarking> readings)
    {
        var choices = new List<AnalysisMarkingChoice>();
        foreach (var analysis in stored)
        {
            if (analysis.Opinion == "approved")
                choices.Add(new AnalysisMarkingChoice(AnalysisMarkingActionKind.Disapprove, "Disapprove",
                    "Approved → Disapproved", analysis.StoredAnalysisId, null, null,
                    "Approved", "Disapproved"));
            else if (analysis.Opinion == "disapproved")
            {
                choices.Add(new AnalysisMarkingChoice(AnalysisMarkingActionKind.Accept, "Accept",
                    "Disapproved → Approved", analysis.StoredAnalysisId, null, null,
                    "Disapproved", "Approved"));
                choices.Add(new AnalysisMarkingChoice(AnalysisMarkingActionKind.MakeUnknown, "Make Unknown",
                    "Disapproved → Unknown", analysis.StoredAnalysisId, null, null,
                    "Disapproved", "Unknown"));
            }
            else
            {
                choices.Add(new AnalysisMarkingChoice(AnalysisMarkingActionKind.Approve, "Approve",
                    "Unknown → Approved", analysis.StoredAnalysisId, null, null,
                    "Unknown", "Approved"));
                choices.Add(new AnalysisMarkingChoice(AnalysisMarkingActionKind.Disapprove, "Disapprove",
                    "Unknown → Disapproved", analysis.StoredAnalysisId, null, null,
                    "Unknown", "Disapproved"));
            }
        }
        for (var index = 0; index < readings.Count; index++)
        {
            var reading = readings[index];
            if (!reading.IsParserOnly) continue;
            choices.Add(new AnalysisMarkingChoice(AnalysisMarkingActionKind.Add, "Add as Unknown",
                "Not in FieldWorks → Unknown", null, reading.Analysis, index,
                "Not in FieldWorks", "Unknown"));
            var label = markingClass == AnalysisMarkingClass.Different &&
                stored.Any(analysis => analysis.Opinion == "disapproved")
                ? "Accept PanGloss's reading" : "Add as Approved";
            choices.Add(new AnalysisMarkingChoice(AnalysisMarkingActionKind.Approve, label,
                "Not in FieldWorks → Approved", null, reading.Analysis, index,
                "Not in FieldWorks", "Approved"));
            if (markingClass == AnalysisMarkingClass.Extra &&
                stored.Any(analysis => analysis.Opinion == "approved"))
                choices.Add(new AnalysisMarkingChoice(AnalysisMarkingActionKind.Disapprove,
                    "Disapprove the extra reading", "Not in FieldWorks → Disapproved", null,
                    reading.Analysis, index, "Not in FieldWorks", "Disapproved"));
        }
        if (stored.Count > 0 && markingClass is AnalysisMarkingClass.Different or AnalysisMarkingClass.Extra)
            choices.Add(new AnalysisMarkingChoice(AnalysisMarkingActionKind.KeepFieldWorks, "Keep FieldWorks",
                "Nothing staged", null, null, null, string.Empty, string.Empty));
        return choices;
    }

    private static string OpinionLabel(string opinion) => opinion switch
    {
        "approved" => "Approved",
        "disapproved" => "Disapproved",
        _ => "Unknown",
    };
}
