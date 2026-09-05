using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Assess;

/// <summary>
/// Which of the four agreed sources (design decision 4) a Selection should draw from, and how. Every
/// source is optional and they combine: a caller wanting the union of two sources sets both.
/// </summary>
/// <param name="AllWordforms">Every wordform currently in the project.</param>
/// <param name="TextIds">The wordforms of these chosen Texts, by their own <c>IText</c> identity.</param>
/// <param name="Words">
/// A pasted or typed list, one word per entry. Entries that are empty or all whitespace are dropped
/// rather than treated as a blank word.
/// </param>
/// <param name="RetryFailed">The words the previous Baseline run found no analysis for.</param>
/// <param name="RetrySlowerThan">
/// Non-null to also include the words the previous Baseline run timed out on. The value is accepted for
/// forward compatibility with a per-word elapsed time that is not yet stored anywhere: today, whether a
/// word timed out under its own run's per-word limit is the only "how slow" signal on record, so presence
/// of a value is what gates this source, not a comparison against the value itself.
/// </param>
public sealed record SelectionRequest(
    bool AllWordforms,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> Words,
    bool RetryFailed,
    TimeSpan? RetrySlowerThan);

/// <summary>A composed Selection together with the Contract-shaped projection <c>selection.txt</c> is written from.</summary>
public sealed record SelectionComposition(Selection Selection, SelectionProjection Projection);

/// <summary>
/// Builds one <see cref="Selection"/> from any combination of the four sources design decision 4 settled
/// on, and the <see cref="SelectionProjection"/> that records what was asked for and how many words each
/// source actually contributed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Match only by exact identity (AGENTS.md rule 12).</b> A chosen Text is resolved by its own GUID and
/// nothing else; there is no fallback that guesses a Text from a similar name or title.
/// </para>
/// <para>
/// <b>NFD, consistently (AGENTS.md rule 14).</b> Every source's words pass through the same
/// trim-blank-normalize pipeline before they ever reach <see cref="Selection.Create"/>, including the
/// ones already read out of LibLCM — which are already NFD, so normalizing them again is a no-op — so a
/// pasted or typed word can never fail to match a project wordform over a normalization difference alone.
/// </para>
/// </remarks>
public static class SelectionComposer
{
    private static readonly string RetryKind = AssessmentKind.ParseTime.ToStoredKind();

    /// <summary>
    /// Composes a Selection from <paramref name="request"/>'s sources, reading the project through
    /// <paramref name="cache"/> and the previous Baseline run through <paramref name="assessmentRepository"/>.
    /// Returns a <c>selection.empty</c> Refusal when no requested source contributed a single word, and a
    /// <c>selection.text-not-found</c> Refusal when a chosen Text's GUID does not resolve in this project.
    /// </summary>
    public static CommandOutcome<SelectionComposition> Compose(
        LcmCache cache, SelectionRequest request, IAssessmentRepository assessmentRepository)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assessmentRepository);

        var provenance = new List<SelectionProvenanceEntry>();
        var words = new List<string>();

        if (request.AllWordforms)
            Contribute(provenance, words, "all-wordforms", LcmWordformCorpus.ExtractForms(cache));

        if (request.TextIds.Count > 0)
        {
            var fromTexts = ReadChosenTextWordforms(cache, request.TextIds, out var missingTextId);
            if (missingTextId is { } guid)
            {
                return CommandOutcome<SelectionComposition>.Refused(new Refusal(
                    "selection.text-not-found", FailureReason.InvalidArgument,
                    $"No Text with GUID '{guid:D}' exists in this project.",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["textId"] = guid.ToString("D") }));
            }
            Contribute(provenance, words, "texts", fromTexts);
        }

        var pasted = request.Words.Where(word => !string.IsNullOrWhiteSpace(word));
        if (request.Words.Count > 0) Contribute(provenance, words, "pasted-words", pasted);

        if (request.RetryFailed || request.RetrySlowerThan is not null)
        {
            var previousRun = LatestBaselineRun(assessmentRepository);

            if (request.RetryFailed)
            {
                var failed = WordsWithOutcome(previousRun, WordOutcome.NoAnalysis, WordOutcome.Skipped);
                Contribute(provenance, words, "retry-failed", failed);
            }

            if (request.RetrySlowerThan is not null)
            {
                var slow = WordsWithOutcome(previousRun, WordOutcome.TimedOut);
                Contribute(provenance, words, "retry-slower-than", slow);
            }
        }

        if (words.Count == 0)
        {
            return CommandOutcome<SelectionComposition>.Refused(new Refusal(
                "selection.empty", FailureReason.Refused,
                "No requested source contributed any words, so there is nothing to compose a Selection from."));
        }

        var selection = Selection.Create(cache.ProjectId.Name, words);
        var projection = new SelectionProjection(selection.Words, provenance);
        return CommandOutcome<SelectionComposition>.Success(new SelectionComposition(selection, projection));
    }

    // Normalizes one source's raw words, appends them to the running total, and records its provenance count.
    private static void Contribute(
        List<SelectionProvenanceEntry> provenance, List<string> words, string source, IEnumerable<string> raw)
    {
        var normalized = raw
            .Select(word => word.Trim())
            .Where(word => word.Length > 0)
            .Select(word => word.Normalize(NormalizationForm.FormD))
            .ToList();
        words.AddRange(normalized);
        provenance.Add(new SelectionProvenanceEntry(source, normalized.Count));
    }

    private static IEnumerable<string> ReadChosenTextWordforms(
        LcmCache cache, IReadOnlyList<Guid> textIds, out Guid? missingTextId)
    {
        var repository = cache.ServiceLocator.GetInstance<ITextRepository>();
        var forms = new List<string>();

        foreach (var textId in textIds)
        {
            if (!repository.TryGetObject(textId, out var text))
            {
                missingTextId = textId;
                return forms;
            }

            var projection = InterlinearTextReader.Read(cache, text);
            forms.AddRange(
                from paragraph in projection.Paragraphs
                from phrase in paragraph.Phrases
                from word in phrase.Words
                from item in word.Items
                where item.Type == "txt" && !string.IsNullOrEmpty(item.Value)
                select item.Value);
        }

        missingTextId = null;
        return forms;
    }

    // The newest Baseline (Proposal-free) ParseTime Assessment, or null when none has ever been recorded.
    private static AssessmentRecord? LatestBaselineRun(IAssessmentRepository repository)
    {
        var runs = repository.ListBaselineAssessments(RetryKind);
        return runs.Count > 0 ? runs[^1] : null;
    }

    private static IEnumerable<string> WordsWithOutcome(AssessmentRecord? run, params WordOutcome[] outcomes)
    {
        if (run?.Words is null) yield break;

        foreach (var word in run.Words)
        {
            if (word.Outcome.TryParseStoredOutcome(out var outcome) && outcomes.Contains(outcome))
                yield return word.Word;
        }
    }
}
