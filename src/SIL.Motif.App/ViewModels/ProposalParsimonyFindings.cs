using System.Globalization;
using System.Text.Json;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Parsimony;

namespace SIL.Motif.App.ViewModels;

/// <summary>What a Parsimony group's stored Reports say, ready to show: the new Findings and the decision counts.</summary>
/// <param name="Findings">The Findings the Proposal adds or changes, measure by measure.</param>
/// <param name="Note">Sentences, a line each, for what could not be read or compared, or when there is nothing to list.</param>
/// <param name="Judgments">One line for each decision count of each measure that has one.</param>
/// <param name="More">How many Findings the group has beyond the ones listed.</param>
internal sealed record ProposalParsimonyGroupFindings(
    IReadOnlyList<ProposalParsimonyFindingViewModel> Findings, string Note, IReadOnlyList<string> Judgments, int More = 0)
{
    /// <summary>One sentence for each measure that has Findings beyond the ones listed.</summary>
    public IReadOnlyList<string> MoreLines { get; init; } = [];

    /// <summary>What each measure's new Findings came to once checked against decisions.</summary>
    public IReadOnlyList<ProposalParsimonyMeasureReading> Readings { get; init; } = [];
}

/// <summary>
/// What one measure's new or changed Findings came to: how many its Report counts, and how many are still Active
/// once earlier decisions are applied.
/// </summary>
/// <param name="MeasureId">The measure.</param>
/// <param name="ReportNew">The new and evidence-changed Findings the Report counts.</param>
/// <param name="Active">How many of those the Active view lists.</param>
/// <param name="Complete">Whether the Active view could be read to its end.</param>
internal sealed record ProposalParsimonyMeasureReading(string MeasureId, int ReportNew, int Active, bool Complete)
{
    /// <summary>Whether a new Finding is still waiting for a look.</summary>
    public bool Found => Active > 0;

    /// <summary>Whether the Report counts new Findings that no one could check against decisions.</summary>
    public bool Unsettled => ReportNew > 0 && Active == 0 && !Complete;

    /// <summary>Whether every new Finding the Report counts is one an earlier decision sets aside.</summary>
    public bool SetAside => ReportNew > 0 && Active == 0 && Complete;
}

/// <summary>
/// Reads the stored Reports and kept views behind a measured group, only to present them. It names a Finding new
/// when the candidate's Active view has it and the Baseline's Report does not, and changed when both have it with
/// different evidence. It reads nothing but stored material and starts no job.
/// </summary>
internal sealed class ProposalParsimonyFindings(
    Func<ShowParsimonyReportRequest, CancellationToken, Task<CommandOutcome<ParsimonyReportResponse>>> readReport,
    Func<ReadParsimonyViewRequest, CancellationToken, Task<CommandOutcome<ParsimonyNamedViewResponse>>> readView)
{
    /// <summary>The most Findings one group lists; the rest are counted, so a large grammar never fills the section.</summary>
    internal const int MaxListed = 25;

    private const int PageSize = 200;

    // A group has a handful of measures, so a page cap only guards a store that keeps returning cursors.
    private const int MaxPages = 20;

    public async Task<ProposalParsimonyGroupFindings> ReadAsync(string projectPath, ProposalParsimonyGroup group,
        CancellationToken cancellationToken)
    {
        var findings = new List<ProposalParsimonyFindingViewModel>();
        var judgments = new List<string>();
        var readings = new List<ProposalParsimonyMeasureReading>();
        var hidden = new List<(string Name, int Count)>();
        var notes = new List<string>();
        foreach (var change in group.Changes)
        {
            var name = ProposalParsimonyText.MeasureName(change.MeasureId);
            var reported = change.NewFindings + change.EvidenceChangedFindings;
            if (group.MeasureRuns.FirstOrDefault(run => run.MeasureId == change.MeasureId) is { Status: not ParsimonyMeasureStatus.Computed })
            {
                readings.Add(new ProposalParsimonyMeasureReading(change.MeasureId, reported, 0, reported == 0));
                continue;
            }
            var active = await ReadActiveAsync(projectPath, group.Candidate!.AfterBundleId, change, reported > 0,
                cancellationToken).ConfigureAwait(true);
            if (active is null)
            {
                AddOnce(notes, MotifAppStrings.ProposalParsimonyFindingsUnreadable);
                readings.Add(new ProposalParsimonyMeasureReading(change.MeasureId, reported, 0, reported == 0));
                continue;
            }
            judgments.AddRange(JudgmentLines(name, active.First));
            if (reported == 0)
            {
                readings.Add(new ProposalParsimonyMeasureReading(change.MeasureId, 0, 0, true));
                continue;
            }
            if (!active.Complete) AddOnce(notes, MotifAppStrings.ProposalParsimonyFindingsPartial);
            var before = await ReadBeforeAsync(projectPath, change.BeforeReportId, cancellationToken).ConfigureAwait(true);
            if (before is null)
            {
                AddOnce(notes, MotifAppStrings.ProposalParsimonyFindingsNotComparable);
                readings.Add(new ProposalParsimonyMeasureReading(change.MeasureId, reported, 0, false));
                continue;
            }
            var counted = 0;
            var left = 0;
            foreach (var row in active.Rows)
            {
                var finding = row.Finding;
                var kind = !before.TryGetValue(finding.FindingId, out var digest) ? MotifAppStrings.ProposalParsimonyFindingKindNew
                    : digest != finding.EvidenceDigest ? MotifAppStrings.ProposalParsimonyFindingKindChanged : null;
                if (kind is null) continue;
                counted++;
                if (findings.Count >= MaxListed)
                {
                    left++;
                    continue;
                }
                var text = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalParsimonyFindingLineFormat,
                    name, Describe(finding));
                findings.Add(new ProposalParsimonyFindingViewModel(finding.FindingId, kind, text,
                    token => ReadEvidenceAsync(projectPath, finding, token)));
            }
            if (left > 0) hidden.Add((name, left));
            readings.Add(new ProposalParsimonyMeasureReading(change.MeasureId, reported, counted, active.Complete));
        }
        if (notes.Count == 0 && findings.Count == 0)
            notes.Add(readings.Any(reading => reading.SetAside) ? MotifAppStrings.ProposalParsimonyFindingsAllDecided
                : group.MeasureRuns.All(run => run.Status == ParsimonyMeasureStatus.Computed) ? MotifAppStrings.ProposalParsimonyFindingsNone
                : MotifAppStrings.ProposalParsimonyFindingsNoneComputed);
        return new ProposalParsimonyGroupFindings(findings, string.Join("\n", notes), judgments, hidden.Sum(item => item.Count))
        {
            MoreLines = [.. hidden.Select(item => string.Format(CultureInfo.CurrentCulture,
                MotifAppStrings.ProposalParsimonyFindingsMoreLineFormat, item.Name,
                PluralText.Format(MotifAppStrings.ProposalParsimonyFindingsMorePattern, item.Count)))],
            Readings = readings,
        };
    }

    private static void AddOnce(List<string> notes, string note)
    {
        if (!notes.Contains(note)) notes.Add(note);
    }

    // An older Report has no stored description, so its counted numbers stand in.
    internal static string Describe(ParsimonyFinding finding) => !string.IsNullOrWhiteSpace(finding.Description)
        ? finding.Description
        : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ParsimonyDescribeFormat,
            finding.Number.Numerator, finding.Number.Denominator, finding.Number.Unit);

    private async Task<ActivePages?> ReadActiveAsync(string projectPath, string bundleId,
        ProposalParsimonyMeasureChange change, bool wantRows, CancellationToken cancellationToken)
    {
        var rows = new List<ParsimonyFindingDispositionViewRow>();
        ParsimonyNamedViewResponse? first = null;
        string? cursor = null;
        var complete = false;
        for (var page = 0; page < MaxPages; page++)
        {
            var outcome = await Safe(() => readView(new ReadParsimonyViewRequest(projectPath,
                MotifProductVersion.CurrentText, new ParsimonyNamedViewRequest(bundleId,
                    ParsimonyViews.ParsimonyActiveFindings,
                    new ParsimonyViewFilters(ReportId: change.AfterReportId, MeasureId: change.MeasureId), PageSize,
                    cursor)), cancellationToken)).ConfigureAwait(true);
            if (outcome is not { Succeeded: true } || outcome.Value!.Status != ParsimonyMeasureStatus.Computed) return null;
            first ??= outcome.Value!;
            rows.AddRange(outcome.Value!.Rows.OfType<ParsimonyFindingDispositionViewRow>());
            cursor = outcome.Value.NextCursor;
            complete = !outcome.Value.Truncated || cursor is null;
            if (complete || !wantRows) break;
        }
        return new ActivePages(first!, rows, complete || !wantRows);
    }

    private async Task<Dictionary<string, string>?> ReadBeforeAsync(string projectPath, string reportId,
        CancellationToken cancellationToken)
    {
        var outcome = await Safe(() => readReport(new ShowParsimonyReportRequest(projectPath,
            MotifProductVersion.CurrentText, reportId), cancellationToken)).ConfigureAwait(true);
        return outcome is { Succeeded: true }
            ? outcome.Value!.Findings.GroupBy(finding => finding.FindingId, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.First().EvidenceDigest, StringComparer.Ordinal)
            : null;
    }

    // The decision counts say what the person already decided; a measure with none to report adds no line.
    private static IEnumerable<string> JudgmentLines(string name, ParsimonyNamedViewResponse page)
    {
        if (page.SuppressedCount is > 0 and var suppressed)
            yield return JudgmentLine(name, PluralText.Format(MotifAppStrings.ProposalParsimonyJudgmentsSuppressedPattern, suppressed));
        if (page.ResurfacedCount is > 0 and var resurfaced)
            yield return JudgmentLine(name, PluralText.Format(MotifAppStrings.ProposalParsimonyJudgmentsResurfacedPattern, resurfaced));
        if (page.UnresolvedJudgmentCount is > 0 and var unresolved)
            yield return JudgmentLine(name, PluralText.Format(MotifAppStrings.ProposalParsimonyJudgmentsUnresolvedPattern, unresolved));
    }

    private static string JudgmentLine(string name, string sentence) =>
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalParsimonyJudgmentsLineFormat, name, sentence);

    private async Task<IReadOnlyList<string>> ReadEvidenceAsync(string projectPath, ParsimonyFinding finding,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        foreach (var reference in finding.EvidenceRefs)
        {
            var outcome = await Safe(() => readView(new ReadParsimonyViewRequest(projectPath,
                MotifProductVersion.CurrentText, new ParsimonyNamedViewRequest(reference.BundleId, reference.View,
                    FiltersOf(reference.Arguments), 1)), cancellationToken)).ConfigureAwait(true);
            lines.Add(string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalParsimonyEvidenceLineFormat,
                ViewName(reference.View), EvidenceState(outcome)));
        }
        return lines;
    }

    // A view that is not kept is an answer to show plainly, not a failure.
    private static string EvidenceState(CommandOutcome<ParsimonyNamedViewResponse>? outcome)
    {
        if (outcome is not { Succeeded: true }) return MotifAppStrings.ProposalParsimonyEvidenceUnreadable;
        var view = outcome.Value!;
        if (view.Status == ParsimonyMeasureStatus.Computed)
            return PluralText.Format(MotifAppStrings.ProposalParsimonyEvidenceRowsPattern, view.Total ?? view.Returned);
        return view.Detail == ParsimonyViewsQuery.NotKeptDetail
            ? MotifAppStrings.ProposalParsimonyEvidenceNotKept : MotifAppStrings.ProposalParsimonyEvidenceNotAvailable;
    }

    // A reference's arguments are an object of string filters; a missing or non-string value is no filter.
    private static ParsimonyViewFilters FiltersOf(JsonElement arguments)
    {
        string? Read(string name) => arguments.ValueKind == JsonValueKind.Object &&
            arguments.EnumerateObject().FirstOrDefault(item => string.Equals(item.Name, name,
                StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: JsonValueKind.String } property
            ? property.Value.GetString() : null;
        return new ParsimonyViewFilters(ObjectGuid: Read("objectGuid"), CategoryGuid: Read("categoryGuid"),
            StatementKind: Read("statementKind"), CaseKey: Read("caseKey"));
    }

    private static string ViewName(string view) => view switch
    {
        ParsimonyViews.AffixContext => MotifAppStrings.ProposalParsimonyViewAffixContext,
        ParsimonyViews.AdhocContext => MotifAppStrings.ProposalParsimonyViewAdhocContext,
        ParsimonyViews.AllomorphContext => MotifAppStrings.ProposalParsimonyViewAllomorphContext,
        ParsimonyViews.AlternationFamilies => MotifAppStrings.ProposalParsimonyViewAlternationFamilies,
        ParsimonyViews.ApprovedMorphSequences => MotifAppStrings.ProposalParsimonyViewApprovedMorphSequences,
        ParsimonyViews.EnvironmentExcess => MotifAppStrings.ProposalParsimonyViewEnvironmentExcess,
        ParsimonyViews.JoinQuality => MotifAppStrings.ProposalParsimonyViewJoinQuality,
        ParsimonyViews.NaturalClassExcess => MotifAppStrings.ProposalParsimonyViewNaturalClassExcess,
        ParsimonyViews.NaturalClassContext => MotifAppStrings.ProposalParsimonyViewNaturalClassContext,
        ParsimonyViews.ParserCases => MotifAppStrings.ProposalParsimonyViewParserCases,
        ParsimonyViews.StatementUsage => MotifAppStrings.ProposalParsimonyViewStatementUsage,
        ParsimonyViews.SlotContext => MotifAppStrings.ProposalParsimonyViewSlotContext,
        ParsimonyViews.TemplateOrder => MotifAppStrings.ProposalParsimonyViewTemplateOrder,
        ParsimonyViews.UnslottedAffixes => MotifAppStrings.ProposalParsimonyViewUnslottedAffixes,
        ParsimonyViews.NullOptional => MotifAppStrings.ProposalParsimonyViewNullOptional,
        _ => MotifAppStrings.ProposalParsimonyViewOther,
    };

    // A throwing read reads as a refusal; only a cancel passes through.
    private static async Task<CommandOutcome<T>?> Safe<T>(Func<Task<CommandOutcome<T>>> read) where T : class?
    {
        try { return await read().ConfigureAwait(true); }
        catch (Exception exception) when (exception is not OperationCanceledException) { return null; }
    }

    private sealed record ActivePages(ParsimonyNamedViewResponse First, List<ParsimonyFindingDispositionViewRow> Rows, bool Complete);
}
