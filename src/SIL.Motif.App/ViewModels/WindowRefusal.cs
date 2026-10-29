using System.Collections.Immutable;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using C = SIL.Motif.Contract.Commands.RefusalCodes;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// A refusal as the window shows it: a plain summary, fatal grammar issues when available, and the command's
/// readable account folded away under Details.
/// </summary>
/// <remarks>
/// <para>
/// A command writes its message for the CLI's reader, an agent, so it may name a Draft or suggest a
/// <c>motif …</c> command line. The window's reader is a linguist, and ADR 0046 decision 3 keeps those words
/// off the screen. <see cref="Sentence"/> uses the compile diagnostic's plain summary when available;
/// otherwise it is looked up by <see cref="Refusal.Code"/>.
/// </para>
/// <para>
/// <see cref="Details"/> holds the command's message and facts, folded away but still on screen once
/// expanded, so it keeps to the window's words too: it leaves out a sentence or fact that names a Proposal,
/// a Draft, a Preflight or a Dry Run, a sentence that names an Assessment or the Assessor (its facts stay, since
/// a problem report needs an id such as <c>assessmentId</c>), and a sentence that tells the reader to run a
/// command line, which a person using the window cannot act on. An unmapped code gets <see cref="GenericSentence"/> and keeps the
/// rest of its message in Details. Compile diagnostics show the parser's issue text, source identity and
/// advice without its serialization; original streams remain available in command and stored evidence.
/// </para>
/// </remarks>
public sealed partial record WindowRefusal
{
    /// <summary>What the window says for a code it has no sentence for.</summary>
    public static string GenericSentence => MotifAppStrings.RefusalGeneric;

    /// <summary>The code of a project open that failed with an escaped exception rather than a refusal.</summary>
    public const string OpenFailedCode = "window.open-failed";

    /// <summary>The code of a saved diagnostic file that could not be read at all.</summary>
    public const string DiagnosticUnreadableCode = "window.diagnostic-unreadable";

    /// <summary>The code of a diagnostic that could not be saved or copied.</summary>
    public const string DiagnosticNotWrittenCode = "window.diagnostic-not-written";

    /// <summary>The code of an action that needs an open project when none is open.</summary>
    public const string NoProjectCode = "window.no-project";

    private const string WindowCheckCode = "window.check";

    private static string ParserMissing => MotifAppStrings.RefusalParserMissing;

    private static string ParserUnusable => MotifAppStrings.RefusalParserUnusable;

    private static string ParserWrong => MotifAppStrings.RefusalParserWrong;

    private static string StoreFromOtherVersion => MotifAppStrings.RefusalStoreFromOtherVersion;

    private static string StoreDamaged => MotifAppStrings.RefusalStoreDamaged;

    /// <summary>Codes whose sentence says PanGloss could not run; a missing parser gets its own.</summary>
    private static readonly HashSet<string> ParserUnusableCodes = new(StringComparer.Ordinal)
    {
        C.AssessParserUnavailable,
        C.GrammarCheckParserUnavailable,
        C.HandoffParserUnavailable,
        C.StatsParserUnavailable,
        C.WordTraceParserUnavailable,
    };

    /// <summary>Sentence per code. To add: resx RefusalXxx, its accessor, then an entry here.</summary>
    private static readonly IReadOnlyDictionary<string, Func<string>> Sentences = new Dictionary<string, Func<string>>(StringComparer.Ordinal)
    {
        [C.ApplyAppliedContentMismatch] = () => MotifAppStrings.RefusalApplyAppliedContentMismatch,
        [C.ApplyChangeNoLongerFits] = () => MotifAppStrings.RefusalApplyChangeNoLongerFits,
        [C.ApplyChangeUncertain] = () => MotifAppStrings.RefusalApplyChangeUncertain,
        [C.ApplyChangesChanged] = () => MotifAppStrings.RefusalApplyChangesChanged,
        [C.ApplyDrift] = () => MotifAppStrings.RefusalApplyDrift,
        [C.ApplyDryRunMissing] = () => MotifAppStrings.RefusalApplyDryRunMissing,
        [C.ProposalClosed] = () => MotifAppStrings.RefusalProposalClosed,
        [C.ProposalNotFound] = () => MotifAppStrings.RefusalProposalNotFound,
        [C.ProposalRationaleEmpty] = () => MotifAppStrings.RefusalProposalRationaleEmpty,
        [C.ApplyPreconditionFailed] = () => MotifAppStrings.RefusalApplyPreconditionFailed,
        [C.ApplyNotReady] = () => MotifAppStrings.RefusalApplyNotReady,
        [C.ApplyProjectInUse] = () => MotifAppStrings.RefusalApplyProjectInUse,
        [C.ApplyReconciliationNeeded] = () => MotifAppStrings.RefusalApplyReconciliationNeeded,
        [C.OperationCascadingDelete] = () => MotifAppStrings.RefusalOperationCascadingDelete,
        [C.OperationInvalidDependency] = () => MotifAppStrings.RefusalOperationInvalidDependency,
        [C.ApplyRegression] = () => MotifAppStrings.RefusalApplyRegression,
        [C.ApplyReopenFailed] = () => MotifAppStrings.RefusalApplyReopenFailed,

        [C.ParseAlreadyRunningHere] = () => MotifAppStrings.RefusalParseAlreadyRunningHere,
        ["parse.already-running"] = () => MotifAppStrings.RefusalParseAlreadyRunningElsewhere,
        [C.AssessBaselineChanged] = () => MotifAppStrings.RefusalAssessBaselineChanged,
        [C.AssessInvalidLimit] = () => MotifAppStrings.RefusalAssessInvalidLimit,
        [C.AssessInvocationInconsistent] = () => MotifAppStrings.RefusalAssessInvocationInconsistent,
        [C.AssessMeasurementsIncomplete] = () => MotifAppStrings.RefusalAssessMeasurementsIncomplete,
        [C.AssessParserUnavailable] = () => ParserUnusable,
        [C.AssessUnsupportedKind] = () => MotifAppStrings.RefusalAssessUnsupportedKind,
        [C.AssessWordsAmbiguous] = () => MotifAppStrings.RefusalAssessWordsAmbiguous,
        [C.AssessWordsFileEmpty] = () => MotifAppStrings.RefusalAssessWordsFileEmpty,
        [C.AssessWordsFileUnreadable] = () => MotifAppStrings.RefusalAssessWordsFileUnreadable,
        [C.AssessmentCancelled] = () => MotifAppStrings.RefusalAssessmentCancelled,

        [C.BaselineBusy] = () => MotifAppStrings.RefusalBaselineBusy,
        [C.BaselineCopyUnloadable] = () => MotifAppStrings.RefusalBaselineCopyUnloadable,
        [C.BaselineOwnedRootViolation] = () => MotifAppStrings.RefusalBaselineOwnedRootViolation,
        [C.BaselineSourceIncomplete] = () => MotifAppStrings.RefusalBaselineSourceIncomplete,
        [C.BaselineTextWordsUnreadable] = () => MotifAppStrings.RefusalBaselineTextWordsUnreadable,

        [C.ChangeAnalysisIdentityRequired] = () => MotifAppStrings.RefusalChangeAnalysisIdentityRequired,
        [C.ChangeAnalysisOwnerInvalid] = () => MotifAppStrings.RefusalChangeAnalysisOwnerInvalid,
        [C.ChangeAssessmentIncomplete] = () => MotifAppStrings.RefusalChangeAssessmentIncomplete,
        [C.ChangeAssessmentKind] = () => MotifAppStrings.RefusalChangeAssessmentKind,
        [C.ChangeAssessmentMissing] = () => MotifAppStrings.RefusalChangeAssessmentMissing,
        [C.ChangeAssessmentRequired] = () => MotifAppStrings.RefusalChangeAssessmentRequired,
        [C.ChangeAssessmentStale] = () => MotifAppStrings.RefusalChangeAssessmentStale,
        [C.ChangeAssessmentWordMissing] = () => MotifAppStrings.RefusalChangeAssessmentWordMissing,
        [C.ChangeBaselineMissing] = () => MotifAppStrings.RefusalChangeBaselineMissing,
        [C.ChangeCannotCompose] = () => MotifAppStrings.RefusalChangeCannotCompose,
        [C.ChangeInvalidIdentity] = () => MotifAppStrings.RefusalChangeInvalidIdentity,
        [C.ChangeNoEffect] = () => MotifAppStrings.RefusalChangeNoEffect,
        [C.ChangeNotFound] = () => MotifAppStrings.RefusalChangeNotFound,
        [C.ChangeOccurrenceUnavailable] = () => MotifAppStrings.RefusalChangeOccurrenceUnavailable,
        [C.ChangeOccurrenceWordformMismatch] = () => MotifAppStrings.RefusalChangeOccurrenceWordformMismatch,
        [C.ChangeProjectSaving] = () => MotifAppStrings.RefusalChangeProjectSaving,
        [C.ChangeReadingMissing] = () => MotifAppStrings.RefusalChangeReadingMissing,
        [C.ChangeReconfirmNotAllowed] = () => MotifAppStrings.RefusalChangeReconfirmNotAllowed,
        [C.ChangeReconfirmUnneeded] = () => MotifAppStrings.RefusalChangeReconfirmUnneeded,
        [C.ChangeRefreshRequired] = () => MotifAppStrings.RefusalChangeRefreshRequired,
        [C.ChangeRevisionConflict] = () => MotifAppStrings.RefusalChangeRevisionConflict,
        [C.ChangeScopeInvalid] = () => MotifAppStrings.RefusalChangeScopeInvalid,
        [C.ChangeSlotOccupied] = () => MotifAppStrings.RefusalChangeSlotOccupied,
        [C.ChangeStoredAnalysisMissing] = () => MotifAppStrings.RefusalChangeStoredAnalysisMissing,
        [C.ChangeTextUnavailable] = () => MotifAppStrings.RefusalChangeTextUnavailable,
        [C.ChangeWordformAmbiguous] = () => MotifAppStrings.RefusalChangeWordformAmbiguous,
        [C.ChangeWordformChanged] = () => MotifAppStrings.RefusalChangeWordformChanged,
        [C.ChangeWordformMissing] = () => MotifAppStrings.RefusalChangeWordformMissing,

        [C.ConfigInvalid] = () => MotifAppStrings.RefusalConfigInvalid,
        [C.DraftRevisionConflict] = () => MotifAppStrings.RefusalDraftRevisionConflict,

        [C.GrammarCheckCancelled] = () => MotifAppStrings.RefusalGrammarCheckCancelled,
        [C.GrammarCheckMalformedFindings] = () => ParserWrong,
        [C.GrammarCheckParserRefused] = () => MotifAppStrings.RefusalGrammarCheckParserRefused,
        [C.GrammarCheckParserUnavailable] = () => ParserUnusable,
        [C.GrammarCheckTimedOut] = () => MotifAppStrings.RefusalGrammarCheckTimedOut,
        [C.GrammarCheckUnsupportedSchema] = () => ParserWrong,

        [C.HandoffCancelled] = () => MotifAppStrings.RefusalHandoffCancelled,
        [C.HandoffDestinationExists] = () => MotifAppStrings.RefusalHandoffDestinationExists,
        [C.HandoffInvocationMismatch] = () => MotifAppStrings.RefusalHandoffInvocationMismatch,
        [C.HandoffInvocationNotFound] = () => MotifAppStrings.RefusalHandoffInvocationNotFound,
        [C.HandoffInvocationRequired] = () => MotifAppStrings.RefusalHandoffInvocationRequired,
        [C.HandoffParserUnavailable] = () => ParserUnusable,
        [C.HandoffSourceUnavailable] = () => MotifAppStrings.RefusalHandoffSourceUnavailable,
        [C.HandoffStatisticsUnavailable] = () => MotifAppStrings.RefusalHandoffStatisticsUnavailable,
        [C.HandoffTextNotFound] = () => MotifAppStrings.RefusalHandoffTextNotFound,

        [C.JobParserUnavailable] = () => MotifAppStrings.RefusalJobParserUnavailable,
        [C.JobWaitCancelled] = () => MotifAppStrings.RefusalJobWaitCancelled,
        [C.JobWaitTimeout] = () => MotifAppStrings.RefusalJobWaitTimeout,
        [C.PreflightUnavailable] = () => MotifAppStrings.RefusalPreflightUnavailable,

        [C.ProjectBackupUnreadable] = () => MotifAppStrings.RefusalProjectBackupUnreadable,
        [C.ProjectBusy] = () => MotifAppStrings.RefusalProjectBusy,
        [C.ProjectInUse] = () => MotifAppStrings.RefusalProjectInUse,
        [C.ProjectInvalid] = () => MotifAppStrings.RefusalProjectInvalid,
        [C.ProjectNotFound] = () => MotifAppStrings.RefusalProjectNotFound,
        [C.ProjectOperationIo] = () => MotifAppStrings.RefusalProjectOperationIo,
        [C.ProjectStoreIo] = () => MotifAppStrings.RefusalProjectStoreIo,
        [C.ProjectUnloadable] = () => MotifAppStrings.RefusalProjectUnloadable,
        [C.ProjectWaitCancelled] = () => MotifAppStrings.RefusalProjectWaitCancelled,

        [C.ReviewAssessmentNotFound] = () => MotifAppStrings.RefusalReviewAssessmentNotFound,
        [C.ReviewNumbersUnavailable] = () => MotifAppStrings.RefusalReviewNumbersUnavailable,
        [C.ReviewWrongAssessmentKind] = () => MotifAppStrings.RefusalReviewWrongAssessmentKind,

        [C.SelectionDefaultMissing] = () => MotifAppStrings.RefusalSelectionDefaultMissing,
        [C.SelectionEmpty] = () => MotifAppStrings.RefusalSelectionEmpty,
        [C.SelectionInvalid] = () => MotifAppStrings.RefusalSelectionInvalid,
        [C.SelectionTextNotFound] = () => MotifAppStrings.RefusalSelectionTextNotFound,

        [RefusalCodes.SelectionReaderBusy] = () => MotifAppStrings.RefusalSelectionReaderBusy,
        [RefusalCodes.SelectionReaderCancelled] = () => MotifAppStrings.RefusalSelectionReaderCancelled,
        [RefusalCodes.SelectionReaderDisposed] = () => MotifAppStrings.RefusalSelectionReaderDisposed,
        [RefusalCodes.SelectionReaderInvalid] = () => MotifAppStrings.RefusalSelectionReaderInvalid,
        ["trial.scratch-gone"] = () => MotifAppStrings.RefusalTrialScratchGone,
        ["trial.none"] = () => MotifAppStrings.RefusalTrialNone,
        [RefusalCodes.SelectionReaderNoBaseline] = () => MotifAppStrings.RefusalSelectionReaderNoBaseline,
        [RefusalCodes.SelectionReaderRangeInvalid] = () => MotifAppStrings.RefusalSelectionReaderRangeInvalid,
        [RefusalCodes.SelectionReaderOwnershipLimit] = () => MotifAppStrings.RefusalSelectionReaderOwnershipLimit,
        [RefusalCodes.SelectionReaderProjectPathRequired] = () => MotifAppStrings.RefusalSelectionReaderProjectPathRequired,
        [C.TextsEvidenceChanged] = () => MotifAppStrings.RefusalTextsEvidenceChanged,

        [C.StatsCancelled] = () => MotifAppStrings.RefusalStatsCancelled,
        [C.StatsInvalidEvidence] = () => MotifAppStrings.RefusalStatsInvalidEvidence,
        [C.StatsNoAssessment] = () => MotifAppStrings.RefusalStatsNoAssessment,
        [C.StatsNoCache] = () => MotifAppStrings.RefusalStatsNoCache,
        [C.StatsNoEvidence] = () => MotifAppStrings.RefusalStatsNoEvidence,
        [C.StatsParserRefused] = () => MotifAppStrings.RefusalStatsParserRefused,
        [C.StatsParserUnavailable] = () => ParserUnusable,
        [C.StatsTimedOut] = () => MotifAppStrings.RefusalStatsTimedOut,

        [C.StoreInconsistent] = () => MotifAppStrings.RefusalStoreInconsistent,
        [C.StoreUnsupported] = () => MotifAppStrings.RefusalStoreUnsupported,

        [C.TextsWordsCancelled] = () => MotifAppStrings.RefusalTextsWordsCancelled,

        [C.TimingInvalidOverride] = () => MotifAppStrings.RefusalTimingInvalidOverride,
        [C.TimingNoAssessment] = () => MotifAppStrings.RefusalTimingNoAssessment,
        [C.TimingNoBaseline] = () => MotifAppStrings.RefusalTimingNoBaseline,
        [C.TimingOverrideNotFound] = () => MotifAppStrings.RefusalTimingOverrideNotFound,
        [C.TimingWordSetNotFound] = () => MotifAppStrings.RefusalTimingWordSetNotFound,
        [C.TimingWrongKind] = () => MotifAppStrings.RefusalTimingWrongKind,

        [C.TrialChangesChanged] = () => MotifAppStrings.RefusalTrialChangesChanged,
        [C.TrialMeasurementIncomplete] = () => MotifAppStrings.RefusalTrialMeasurementIncomplete,
        [NoProjectCode] = () => MotifAppStrings.RefusalNoProjectCode,
        [C.TrialNothingPending] = () => MotifAppStrings.RefusalTrialNothingPending,
        [C.TrialUpdateInProgress] = () => MotifAppStrings.RefusalTrialUpdateInProgress,

        [C.WordTraceCancelled] = () => MotifAppStrings.RefusalWordTraceCancelled,
        [C.WordTraceMalformedDiagnostic] = () => MotifAppStrings.RefusalWordTraceMalformedDiagnostic,
        [C.WordTraceMalformedOutput] = () => ParserWrong,
        [C.WordTraceNoBaseline] = () => MotifAppStrings.RefusalWordTraceNoBaseline,
        [C.WordTraceParserRefused] = () => MotifAppStrings.RefusalWordTraceParserRefused,
        [C.WordTraceParserUnavailable] = () => ParserUnusable,
    };

    private WindowRefusal(string code, string sentence, string? details, string? reportDetails,
        IReadOnlyDictionary<string, string> facts, FailureReason? reason, Exception? failureException = null,
        ParserCompileDiagnostic? parserDiagnostic = null)
    {
        Code = code;
        Sentence = sentence;
        Details = details;
        ReportDetails = reportDetails;
        Facts = facts;
        Reason = reason;
        FailureException = failureException;
        ParserDiagnostic = parserDiagnostic;
    }

    /// <summary>The command's stable refusal code, for a control that acts on one kind of refusal.</summary>
    public string Code { get; }

    /// <summary>The bounded window operation inferred from this refusal's stable code.</summary>
    public string Operation => OperationFor(Code);

    /// <summary>The command refusal class, or <see langword="null"/> for a window-only check.</summary>
    public FailureReason? Reason { get; }

    /// <summary>The local exception used to make a path-free stack for the report preview.</summary>
    internal Exception? FailureException { get; }

    /// <summary>The window's plain summary, shown before any fatal issues and folded details.</summary>
    public string Sentence { get; }

    /// <summary>
    /// The command's own message and facts, or <see langword="null"/> when there is nothing to add. A fact whose
    /// value is a path shows only its file name.
    /// </summary>
    public string? Details { get; }

    /// <summary>Details with full local paths, for the problem report.</summary>
    internal string? ReportDetails { get; }

    public bool HasDetails => Details is not null;

    /// <summary>The parser's fatal grammar issues, shown ahead of its folded details.</summary>
    public ParserCompileDiagnostic? ParserDiagnostic { get; }

    public IReadOnlyList<ParserCompileIssue> CompileIssues => ParserDiagnostic?.Issues ?? [];

    public bool HasCompileIssues => CompileIssues.Count > 0;

    public string DetailsTitle => ParserDiagnostic is null ? MotifAppStrings.Details : MotifAppStrings.RefusalDetailsGrammarIssuesTitle;

    /// <summary>The command's facts, such as the store path a delete button would need.</summary>
    public IReadOnlyDictionary<string, string> Facts { get; }

    /// <summary>Whether this refusal can be reported as a problem rather than a cancelled or invalid action.</summary>
    public bool OffersProblemReport => Code != WindowCheckCode &&
        Reason is not FailureReason.Cancelled and not FailureReason.InvalidArgument;

    /// <summary>
    /// Whether this refusal is a store made by another version of Motif that names its file, the one refusal a
    /// person can clear by deleting that file so Motif recreates it.
    /// </summary>
    public bool OffersStoreDeletion =>
        Code == C.StoreOtherVersion && Facts.ContainsKey(RefusalFactNames.StorePath);

    /// <summary>The heading for a refused Motif store that can be deleted and recreated.</summary>
    public string? Heading => OffersStoreDeletion ? MotifAppStrings.RefusalStoreOtherVersionHeading : null;

    /// <summary>Presents a command's refusal in the window's words.</summary>
    public static WindowRefusal From(Refusal refusal) => From(refusal, GenericSentence);

    /// <summary>Presents a refusal with a page-specific sentence when the window has no sentence for its code.</summary>
    /// <param name="refusal">The refusal being shown.</param>
    /// <param name="sentenceWhenUnmapped">The sentence used when the refusal code has no window sentence.</param>
    public static WindowRefusal From(Refusal refusal, string sentenceWhenUnmapped)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        ArgumentException.ThrowIfNullOrWhiteSpace(sentenceWhenUnmapped);
        var diagnostic = refusal.ParserDiagnostic ?? ParserCompileDiagnosticReader.Read(refusal.Message);
        return new WindowRefusal(refusal.Code, diagnostic?.Summary ?? SentenceFor(refusal, sentenceWhenUnmapped),
            DetailsFor(refusal, diagnostic, forReport: false), DetailsFor(refusal, diagnostic, forReport: true),
            refusal.Facts, refusal.Reason, parserDiagnostic: diagnostic);
    }

    /// <summary>A check the window made itself, already in the window's words and with nothing to fold away.</summary>
    public static WindowRefusal Plain(string sentence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sentence);
        return new WindowRefusal(WindowCheckCode, sentence, null, null,
            ImmutableDictionary<string, string>.Empty, null);
    }

    /// <summary>
    /// A failure that reached the window as an exception rather than a refusal: the window's sentence, with the
    /// exception's own text kept to Details under the same rules as a command's message.
    /// </summary>
    public static WindowRefusal Failure(string code, string sentence, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(sentence);
        ArgumentNullException.ThrowIfNull(exception);
        var details = WindowSafe(exception.Message);
        var text = details.Length == 0 ? null : details;
        return new WindowRefusal(code, sentence, text, text,
            ImmutableDictionary<string, string>.Empty, null, exception);
    }

    private static string OperationFor(string code) =>
        code == OpenFailedCode ? "open a project" :
        code.StartsWith("assess.", StringComparison.Ordinal) || code.StartsWith("assessment.", StringComparison.Ordinal)
            ? "measure words" :
        code.StartsWith("baseline.", StringComparison.Ordinal) ? "refresh the project" :
        code.StartsWith("apply.", StringComparison.Ordinal) ? "apply pending changes" :
        code.StartsWith("grammarcheck.", StringComparison.Ordinal) ? "check grammar" :
        code.StartsWith("wordtrace.", StringComparison.Ordinal) ? "try a word" :
        code.StartsWith("stats.", StringComparison.Ordinal) ? "read statistics" :
        code.StartsWith("project.", StringComparison.Ordinal) || code.StartsWith("store.", StringComparison.Ordinal)
            ? "open or read a project" : "window action";

    private static string SentenceFor(Refusal refusal, string sentenceWhenUnmapped)
    {
        if (refusal.Code == C.StoreInconsistent) return StoreDamaged;
        if (refusal.Code == C.StoreOtherVersion) return StoreFromOtherVersion;
        if (refusal.Facts.ContainsKey(RefusalFactNames.ParserNotFound) && ParserUnusableCodes.Contains(refusal.Code))
            return ParserMissing;
        return Sentences.TryGetValue(refusal.Code, out var sentence) ? sentence() : sentenceWhenUnmapped;
    }

    private static string CompileDetails(Refusal refusal, ParserCompileDiagnostic diagnostic, bool forReport)
    {
        var lines = new List<string> { diagnostic.Summary };
        foreach (var issue in diagnostic.Issues)
        {
            lines.Add($"{issue.Code} ({issue.Kind}): {issue.Text}");
            if (issue.ObjectKind is { } kind) lines.Add(string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.RefusalIssueObjectFormat, kind));
            if (issue.ObjectGuid is { } id) lines.Add(string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.RefusalIssueIdentityFormat, id));
            if (issue.Field is { } field) lines.Add(string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.RefusalIssueFieldFormat, field));
            lines.Add(issue.Advice);
        }
        lines.AddRange(refusal.Facts.Where(fact =>
                fact.Key is not "standardError" and not "standardOutput" and not "parserMessage")
            .OrderBy(fact => fact.Key, StringComparer.Ordinal).Select(fact => FactLine(fact, forReport)));
        return string.Join(Environment.NewLine, lines);
    }

    private static string? DetailsOf(Refusal refusal, bool forReport)
    {
        var lines = new List<string>();
        var message = WindowSafe(refusal.Message);
        if (message.Length > 0) lines.Add(message);
        lines.AddRange(refusal.Facts
            .Where(fact => fact.Key != RefusalFactNames.ParserNotFound)
            .Where(fact => !CliTerm().IsMatch(fact.Key) && !CliTerm().IsMatch(fact.Value))
            .OrderBy(fact => fact.Key, StringComparer.Ordinal)
            .Select(fact => FactLine(fact, forReport)));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? DetailsFor(Refusal refusal, ParserCompileDiagnostic? diagnostic, bool forReport) =>
        diagnostic is null ? DetailsOf(refusal, forReport) : CompileDetails(refusal, diagnostic, forReport);

    // Path-valued facts show only their file name here; ReportDetails keeps the whole path.
    private static string FactLine(KeyValuePair<string, string> fact, bool forReport)
    {
        var pathValued = fact.Key.EndsWith("Path", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(fact.Value);
        if (forReport || !pathValued) return $"{fact.Key}: {fact.Value}";
        return $"{fact.Key}: {Path.GetFileName(fact.Value)}";
    }

    private static string WindowSafe(string text) => string.Join(" ", SentenceBreak().Split(text.Trim())
        .Where(sentence => sentence.Length > 0 && !CommandLine().IsMatch(sentence) && !CliTerm().IsMatch(sentence) &&
            !AssessmentWord().IsMatch(sentence)));

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();

    // A quoted `motif …` line, or any quoted line with an option, is a remedy only the CLI can act on.
    [GeneratedRegex(@"[`'](?:motif\s|[^`']*\s--[a-z])")]
    private static partial Regex CommandLine();

    // The CLI's names for pending changes and their checks, which ADR 0046 decision 3 keeps off the screen.
    [GeneratedRegex(@"proposal|draft|preflight|dry[\s-]?run", RegexOptions.IgnoreCase)]
    private static partial Regex CliTerm();

    // The CLI's words for a parse run, kept out of prose only: a fact such as assessmentId is what a report needs.
    [GeneratedRegex(@"\bassess\w*", RegexOptions.IgnoreCase)]
    private static partial Regex AssessmentWord();
}
