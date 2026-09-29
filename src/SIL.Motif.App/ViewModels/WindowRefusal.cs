using System.Collections.Immutable;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Commands;
using C = SIL.Motif.Contract.Commands.RefusalCodes;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// A refusal as the window shows it: one sentence in the window's words, and the command's own account
/// folded away under Details.
/// </summary>
/// <remarks>
/// <para>
/// A command writes its message for the CLI's reader, an agent, so it may name a Draft or suggest a
/// <c>motif …</c> command line. The window's reader is a linguist, and ADR 0046 decision 3 keeps those words
/// off the screen. So <see cref="Sentence"/> is looked up by <see cref="Refusal.Code"/>, never taken from
/// the message.
/// </para>
/// <para>
/// <see cref="Details"/> holds the command's message and facts, folded away but still on screen once
/// expanded, so it keeps to the window's words too: it leaves out a sentence or fact that names a Proposal,
/// a Draft, a Preflight or a Dry Run, and a sentence that tells the reader to run a command line, which a
/// person using the window cannot act on. An unmapped code gets <see cref="GenericSentence"/> and keeps the
/// rest of its message in Details.
/// </para>
/// </remarks>
public sealed partial record WindowRefusal
{
    /// <summary>What the window says for a code it has no sentence for.</summary>
    public const string GenericSentence = "Motif could not complete this request. Review the project and try again.";

    /// <summary>The code of a project open that failed with an escaped exception rather than a refusal.</summary>
    public const string OpenFailedCode = "window.open-failed";

    /// <summary>The code of a saved diagnostic file that could not be read at all.</summary>
    public const string DiagnosticUnreadableCode = "window.diagnostic-unreadable";

    /// <summary>The code of a diagnostic that could not be saved or copied.</summary>
    public const string DiagnosticNotWrittenCode = "window.diagnostic-not-written";

    private const string WindowCheckCode = "window.check";

    private const string ParserMissing =
        "Motif could not find PanGloss, so it cannot measure words. Install PanGloss beside Motif, then try again.";

    private const string ParserUnusable =
        "Motif could not run PanGloss, so it cannot measure words. The details say why.";

    private const string ParserWrong =
        "PanGloss gave an answer Motif could not read. Install the PanGloss that matches this Motif, then try again.";

    private const string StoreFromOtherVersion =
        "Motif's file for this project{0} was made by a different version of Motif. Close Motif, delete that " +
        "file, and open the project again. Changes not applied yet are lost; your FieldWorks project is not touched.";

    private static readonly IReadOnlyDictionary<string, string> Sentences = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [C.ApplyAppliedContentMismatch] = "Applying may have completed, but the project could not be verified. Check it before retrying.",
        [C.ApplyChangeNoLongerFits] = "One or more changes no longer fit. Remove those changes first.",
        [C.ApplyChangeUncertain] = "This change needs another check before it can be applied.",
        [C.ApplyChangesChanged] = "The changes changed. Refresh them before applying.",
        [C.ApplyDrift] = "Applying may have completed, but the project could not be verified. Check it before retrying.",
        [C.ApplyDryRunMissing] = "The check did not give enough evidence to apply these changes. Check them again.",
        [C.ApplyNotReady] = "The check did not give enough evidence to apply these changes. Check them again.",
        [C.ApplyProjectInUse] = "FieldWorks has this project open. Close it before applying changes.",
        [C.ApplyReconciliationNeeded] =
            "Applying may have completed, but its result could not be confirmed. Check the project before retrying.",
        [C.ApplyRegression] = "The check found worse results for words already measured. Review them before applying.",
        [C.ApplyReopenFailed] =
            "The changes could not be applied or reopened. Inspect the FieldWorks project before trying again.",

        [C.AssessBaselineChanged] = "The project was refreshed while words were being measured. Measure them again.",
        [C.AssessInvalidLimit] = "Enter a per-word time limit greater than zero.",
        [C.AssessInvocationInconsistent] = "Motif could not record these measurements together. Measure the words again.",
        [C.AssessMeasurementsIncomplete] = "Measuring did not finish. Measure the words again.",
        [C.AssessParserUnavailable] = ParserUnusable,
        [C.AssessUnsupportedKind] =
            "The PanGloss Motif found cannot take this measurement. Install the PanGloss that matches this Motif, then try again.",
        [C.AssessmentCancelled] = "Measuring was cancelled. Nothing was recorded.",

        [C.BaselineBusy] = "Another Motif task is reading this project. Try again in a moment.",
        [C.BaselineCopyUnloadable] =
            "Motif could not read the saved copy of this project. Save it in FieldWorks, then refresh again.",
        [C.BaselineOwnedRootViolation] =
            "Motif's working files for this project are not where they should be. Close Motif and open the project again.",
        [C.BaselineSourceIncomplete] =
            "The project's files are incomplete. Save the project in FieldWorks, then refresh again.",
        [C.BaselineTextWordsUnreadable] =
            "Motif could not read the project's texts. Save the project in FieldWorks, then refresh again.",

        [C.ChangeAnalysisIdentityRequired] = "Choose an analysis before adding this change.",
        [C.ChangeAnalysisOwnerInvalid] = "Motif could not find the word that owns this analysis. Refresh and choose it again.",
        [C.ChangeAssessmentIncomplete] = "This Assessment did not finish for every selected word. Run it again before accepting the new set.",
        [C.ChangeAssessmentKind] = "Accept the new set requires an Assessment that parses every word. Run a complete Assessment first.",
        [C.ChangeAssessmentMissing] = "This Assessment is no longer available. Run it again before adding the change.",
        [C.ChangeAssessmentRequired] = "Run an Assessment before adding this change.",
        [C.ChangeAssessmentStale] = "This Assessment is from an older Baseline. Run a new Assessment first.",
        [C.ChangeAssessmentWordMissing] = "This Assessment does not include the selected word. Run it for that selection again.",
        [C.ChangeBaselineMissing] = "Capture a Baseline before collecting changes.",
        [C.ChangeCannotCompose] = "This change could not be added. Refresh the changes and try again.",
        [C.ChangeInvalidIdentity] = "This change could not be added. Select the word again and retry.",
        [C.ChangeNoEffect] = "This change would not alter the project.",
        [C.ChangeNotFound] = "That change is no longer in the list. Refresh the changes and try again.",
        [C.ChangeOccurrenceUnavailable] = "The selected word occurrence is unavailable. Refresh and select it again.",
        [C.ChangeOccurrenceWordformMismatch] = "The selected occurrence belongs to a different word.",
        [C.ChangeProjectSaving] = "FieldWorks is saving the project. Try again in a moment.",
        [C.ChangeReadingMissing] = "That parser reading is no longer available. Run the Assessment again.",
        [C.ChangeReconfirmNotAllowed] = "This decision no longer fits the project and cannot be checked again.",
        [C.ChangeReconfirmUnneeded] = "This change does not need another check.",
        [C.ChangeRefreshRequired] = "Refresh the project before checking the changes again.",
        [C.ChangeRevisionConflict] = "The changes changed elsewhere. Refresh them and try again.",
        [C.ChangeScopeInvalid] = "Choose one wordform, one Selection, or one Text.",
        [C.ChangeSlotOccupied] = "Another pending change already addresses this word and analysis.",
        [C.ChangeStoredAnalysisMissing] = "That analysis is no longer stored under this word. Refresh and choose another.",
        [C.ChangeTextUnavailable] = "That Text is no longer in the current project view. Refresh and try again.",
        [C.ChangeWordformAmbiguous] = "The project has more than one word with this spelling. Choose one in FieldWorks.",
        [C.ChangeWordformChanged] = "That word changed in the project. Refresh and try again.",
        [C.ChangeWordformMissing] = "That word isn't in the FieldWorks project. Refresh and try again.",

        [C.ConfigInvalid] = "Motif's settings for this project could not be read. Open the setup again and save them.",
        [C.DraftRevisionConflict] = "The changes changed elsewhere. Refresh them and try again.",

        [C.GrammarCheckCancelled] = "The grammar check was cancelled.",
        [C.GrammarCheckMalformedFindings] = ParserWrong,
        [C.GrammarCheckParserRefused] = "PanGloss could not check this grammar. The details say why.",
        [C.GrammarCheckParserUnavailable] = ParserUnusable,
        [C.GrammarCheckTimedOut] = "The grammar check took too long and was stopped. Try again.",
        [C.GrammarCheckUnsupportedSchema] = ParserWrong,

        [C.HandoffCancelled] = "The AI Handoff was cancelled.",
        [C.HandoffDestinationExists] = "That folder already has files in it. Choose an empty folder.",
        [C.HandoffInvocationMismatch] =
            "Those measurements belong to another project. Measure this project's words, then try again.",
        [C.HandoffInvocationNotFound] =
            "Those measurements are no longer stored. Measure the words again, then make the AI Handoff.",
        [C.HandoffInvocationRequired] = "Measure words before making an AI Handoff.",
        [C.HandoffParserUnavailable] = ParserUnusable,
        [C.HandoffSourceUnavailable] =
            "The grammar those measurements used is no longer stored. Measure the words again, then make the AI Handoff.",
        [C.HandoffStatisticsUnavailable] =
            "The statistics for those measurements are no longer stored. Measure the words again, then make the AI Handoff.",
        [C.HandoffTextNotFound] =
            "A chosen text is not in the project as it was measured. Refresh, measure again, then try again.",

        [C.JobWaitCancelled] = "The check was cancelled.",
        [C.JobWaitTimeout] = "The check took too long and was stopped. Your changes are unchanged; try applying again.",
        [C.PreflightUnavailable] = "Motif could not check these changes against the project. Try again.",

        [C.ProjectBusy] = "Another Motif task is using this project. Try again in a moment.",
        [C.ProjectInUse] = "FieldWorks has this project open. Close it in FieldWorks, then try again.",
        [C.ProjectInvalid] = "Motif cannot open that file as a FieldWorks project. Choose the project's .fwdata file.",
        [C.ProjectNotFound] = "Motif cannot find that project file. It may have been moved or deleted.",
        [C.ProjectOperationIo] = "Motif could not read or write a file it needed. Check the disk, then try again.",
        [C.ProjectStoreIo] =
            "Motif could not read or write its file for this project. Check that the folder can be written to, then try again.",
        [C.ProjectUnloadable] =
            "Motif could not open this FieldWorks project. Open it in FieldWorks to check it, then try again.",
        [C.ProjectWaitCancelled] = "Waiting to use the project was cancelled.",

        [C.ReviewAssessmentNotFound] = "Those measurements are no longer stored. Check the changes again.",
        [C.ReviewNumbersUnavailable] = "Motif could not work out the numbers for these changes. Check them again.",
        [C.ReviewWrongAssessmentKind] = "These numbers need a correctness check. Check the changes again.",

        [C.SelectionDefaultMissing] =
            "Motif does not know which words to measure yet. Choose texts or words in the setup first.",
        [C.SelectionEmpty] = "The chosen texts have no words to measure. Choose other texts or add words.",
        [C.SelectionInvalid] = "Choose at least one text or add a word before continuing.",
        [C.SelectionInvalidTimeLimit] = "Enter a per-word time limit greater than zero.",
        [C.SelectionTextNotFound] = "A chosen text is no longer in the project. Choose the texts again.",

        [C.StatsCancelled] = "Reading the statistics was cancelled.",
        [C.StatsInvalidEvidence] = "The stored statistics are damaged. Measure the words again.",
        [C.StatsNoAssessment] = "No measurements with statistics are stored yet. Measure the words first.",
        [C.StatsNoCache] = "These measurements were stored without statistics. Measure the words again.",
        [C.StatsNoEvidence] = "The grammar these measurements used is no longer stored. Measure the words again.",
        [C.StatsParserRefused] = "PanGloss could not read these statistics. The details say why.",
        [C.StatsParserUnavailable] = ParserUnusable,
        [C.StatsTimedOut] = "Reading the statistics took too long and was stopped. Try again.",

        [C.StoreInconsistent] = "Motif's file for this project is damaged and cannot be used.",
        [C.StoreUnsupported] = "Motif's file for this project cannot be used by this version of Motif.",

        [C.TextsWordsCancelled] = "Reading the texts' words was cancelled.",
        [C.TextsWordsQueryFailed] = "Motif could not read the words of the chosen texts. Try again.",

        [C.TimingInvalidOverride] = "A re-run does not belong to these measurements. Re-run the words again.",
        [C.TimingNoAssessment] = "No timings are stored yet. Measure the words first.",
        [C.TimingNoBaseline] = "Refresh the project before looking at timings.",
        [C.TimingOverrideNotFound] = "A re-run is no longer stored. Re-run the words again.",
        [C.TimingWordSetNotFound] = "That saved set of words no longer exists. Choose another.",
        [C.TimingWrongKind] = "Those measurements have no timings. Measure the words again.",

        [C.TrialChangesChanged] = "The changes were updated while they were being checked. Check them again.",
        [C.TrialMeasurementIncomplete] = "The check did not finish. Try it again.",
        [C.TrialNothingPending] = "There are no changes to check.",

        [C.WordTraceCancelled] = "Trying the word was cancelled.",
        [C.WordTraceMalformedDiagnostic] = "That file is not a diagnostic Motif can read. Choose a diagnostic Motif saved.",
        [C.WordTraceMalformedOutput] = ParserWrong,
        [C.WordTraceNoBaseline] = "Refresh the project before trying a word.",
        [C.WordTraceParserRefused] = "PanGloss could not parse this word. The details say why.",
        [C.WordTraceParserUnavailable] = ParserUnusable,
    };

    private WindowRefusal(string code, string sentence, string? details, IReadOnlyDictionary<string, string> facts)
    {
        Code = code;
        Sentence = sentence;
        Details = details;
        Facts = facts;
    }

    /// <summary>The command's stable refusal code, for a control that acts on one kind of refusal.</summary>
    public string Code { get; }

    /// <summary>The window's sentence for this refusal; the only part shown unfolded.</summary>
    public string Sentence { get; }

    /// <summary>The command's own message and facts, or <see langword="null"/> when there is nothing to add.</summary>
    public string? Details { get; }

    public bool HasDetails => Details is not null;

    /// <summary>The command's facts, such as the store path a delete button would need.</summary>
    public IReadOnlyDictionary<string, string> Facts { get; }

    /// <summary>
    /// Whether this refusal is a store made by another version of Motif that names its file, the one refusal a
    /// person can clear by deleting that file so Motif recreates it.
    /// </summary>
    public bool OffersStoreDeletion =>
        Code == C.StoreOtherVersion && Facts.ContainsKey(RefusalFactNames.StorePath);

    /// <summary>Presents a command's refusal in the window's words.</summary>
    public static WindowRefusal From(Refusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new WindowRefusal(refusal.Code, SentenceFor(refusal), DetailsOf(refusal), refusal.Facts);
    }

    /// <summary>A check the window made itself, already in the window's words and with nothing to fold away.</summary>
    public static WindowRefusal Plain(string sentence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sentence);
        return new WindowRefusal(WindowCheckCode, sentence, null, ImmutableDictionary<string, string>.Empty);
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
        return new WindowRefusal(code, sentence, details.Length == 0 ? null : details,
            ImmutableDictionary<string, string>.Empty);
    }

    private static string SentenceFor(Refusal refusal)
    {
        if (refusal.Code == C.StoreOtherVersion)
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, StoreFromOtherVersion,
                refusal.Facts.TryGetValue(RefusalFactNames.StorePath, out var path) ? ", " + path + "," : string.Empty);
        if (refusal.Facts.ContainsKey(RefusalFactNames.ParserNotFound) &&
            Sentences.TryGetValue(refusal.Code, out var mapped) && mapped == ParserUnusable)
            return ParserMissing;
        return Sentences.TryGetValue(refusal.Code, out var sentence) ? sentence : GenericSentence;
    }

    private static string? DetailsOf(Refusal refusal)
    {
        var lines = new List<string>();
        var message = WindowSafe(refusal.Message);
        if (message.Length > 0) lines.Add(message);
        lines.AddRange(refusal.Facts
            .Where(fact => fact.Key != RefusalFactNames.ParserNotFound)
            .Where(fact => !CliTerm().IsMatch(fact.Key) && !CliTerm().IsMatch(fact.Value))
            .OrderBy(fact => fact.Key, StringComparer.Ordinal)
            .Select(fact => $"{fact.Key}: {fact.Value}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string WindowSafe(string text) => string.Join(" ", SentenceBreak().Split(text.Trim())
        .Where(sentence => sentence.Length > 0 && !CommandLine().IsMatch(sentence) && !CliTerm().IsMatch(sentence)));

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();

    // A quoted `motif …` line, or any quoted line with an option, is a remedy only the CLI can act on.
    [GeneratedRegex(@"[`'](?:motif\s|[^`']*\s--[a-z])")]
    private static partial Regex CommandLine();

    // The CLI's names for pending changes and their checks, which ADR 0046 decision 3 keeps off the screen.
    [GeneratedRegex(@"proposal|draft|preflight|dry[\s-]?run", RegexOptions.IgnoreCase)]
    private static partial Regex CliTerm();
}
