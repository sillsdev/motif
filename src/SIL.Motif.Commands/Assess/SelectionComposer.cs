using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Assess;

/// <summary>A composed Selection, its human-facing projection, and its complete retained descriptor.</summary>
public sealed record SelectionComposition(
    Selection Selection, SelectionProjection Projection, SelectionDescriptor Descriptor);

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
        LcmCache cache, SelectionRequest request, IAssessmentRepository assessmentRepository,
        string? baselineToken = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assessmentRepository);

        var provenance = new List<SelectionProvenanceEntry>();
        var words = new List<string>();

        if (request.AllWordforms)
            Contribute(provenance, words, "all-wordforms", LcmWordformCorpus.ExtractForms(cache));

        var textIds = request.TextIds
            .Distinct()
            .OrderBy(textId => textId.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        if (textIds.Length > 0)
        {
            var fromTexts = ReadChosenTextWordforms(cache, textIds, out var missingTextId);
            if (missingTextId is { } guid)
            {
                return CommandOutcome<SelectionComposition>.Refused(new Refusal(
                    "selection.text-not-found", FailureReason.InvalidArgument,
                    $"No Text with GUID '{guid:D}' exists in this project.",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["textId"] = guid.ToString("D") }));
            }
            Contribute(provenance, words, "texts", fromTexts);
        }

        var pasted = request.Words
            .Select(word => word.Trim())
            .Where(word => word.Length > 0)
            .Select(word => word.Normalize(NormalizationForm.FormD))
            .ToArray();
        if (request.Words.Count > 0) Contribute(provenance, words, "pasted-words", pasted);

        AssessmentRecord? retrySource = null;
        if (request.RetryFailed || request.RetrySlowerThan is not null)
        {
            if (string.IsNullOrWhiteSpace(request.RetrySourceAssessmentId))
            {
                return CommandOutcome<SelectionComposition>.Refused(new Refusal(
                    "selection.retry-source-required", FailureReason.InvalidArgument,
                    "RetryFailed and RetrySlowerThan require an explicit source Assessment id."));
            }
            try
            {
                retrySource = assessmentRepository.Get(request.RetrySourceAssessmentId);
            }
            catch (KeyNotFoundException)
            {
                return CommandOutcome<SelectionComposition>.Refused(new Refusal(
                    "selection.retry-source-not-found", FailureReason.InvalidArgument,
                    $"Retry source Assessment '{request.RetrySourceAssessmentId}' was not found."));
            }

            if (retrySource.ProposalId is not null || retrySource.Kind != RetryKind)
            {
                return CommandOutcome<SelectionComposition>.Refused(new Refusal(
                    "selection.retry-source-invalid", FailureReason.InvalidArgument,
                    "Retry source must be a Baseline ParseTime Assessment."));
            }

            if (baselineToken is not null &&
                !StringComparer.Ordinal.Equals(retrySource.BaselineToken, baselineToken))
            {
                return CommandOutcome<SelectionComposition>.Refused(new Refusal(
                    "selection.retry-source-mismatch", FailureReason.InvalidArgument,
                    "Retry source Assessment belongs to a different Baseline."));
            }

            if (baselineToken is not null &&
                !RetrySourceBelongsToProject(cache, retrySource.BaselineToken, baselineToken))
            {
                return CommandOutcome<SelectionComposition>.Refused(new Refusal(
                    "selection.retry-source-project-mismatch", FailureReason.InvalidArgument,
                    "Retry source Assessment belongs to a different project."));
            }

            if (request.RetryFailed)
            {
                var failed = WordsWithOutcome(retrySource, WordOutcome.NoAnalysis, WordOutcome.Skipped);
                Contribute(provenance, words, "retry-failed", failed);
            }

            if (request.RetrySlowerThan is not null)
            {
                var slow = WordsSlowerThan(retrySource, request.RetrySlowerThan.Value);
                Contribute(provenance, words, "retry-slower-than", slow);
            }
        }
        else if (request.RetrySourceAssessmentId is not null)
        {
            return CommandOutcome<SelectionComposition>.Refused(new Refusal(
                "selection.retry-source-without-retry", FailureReason.InvalidArgument,
                "RetrySourceAssessmentId requires RetryFailed or RetrySlowerThan."));
        }

        if (words.Count == 0)
        {
            return CommandOutcome<SelectionComposition>.Refused(new Refusal(
                "selection.empty", FailureReason.Refused,
                "No requested source contributed any words, so there is nothing to compose a Selection from."));
        }

        var selection = Selection.Create(cache.ProjectId.Name, words);
        var projection = new SelectionProjection(selection.Words, provenance);
        var descriptor = new SelectionDescriptor(
            textIds, pasted, request.AllWordforms, request.RetryFailed,
            retrySource?.AssessmentId, request.RetrySlowerThan, selection.Words, selection.Sha256,
            projection.Provenance);
        descriptor = descriptor with { DescriptorSha256 = SelectionDescriptorDigest.Compute(descriptor) };
        return CommandOutcome<SelectionComposition>.Success(new SelectionComposition(selection, projection, descriptor));
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

    private static bool RetrySourceBelongsToProject(LcmCache cache, string sourceBaselineJson, string currentBaselineJson)
    {
        try
        {
            var current = JsonSerializer.Deserialize<BaselineToken>(currentBaselineJson, MotifJson.CreateOptions());
            var source = JsonSerializer.Deserialize<BaselineToken>(sourceBaselineJson, MotifJson.CreateOptions());
            return current is not null && source is not null &&
                StringComparer.Ordinal.Equals(current.ProjectIdentity, source.ProjectIdentity) &&
                StringComparer.Ordinal.Equals(current.ProjectIdentity, cache.LangProject.Guid.ToString("D"));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return false;
        }
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

    // A stored elapsed time is the real "how slow" signal; a timed-out word still recorded one (its cap).
    private static IEnumerable<string> WordsSlowerThan(AssessmentRecord? run, TimeSpan threshold)
    {
        if (run?.Words is null) yield break;

        var thresholdMs = threshold.TotalMilliseconds;
        foreach (var word in run.Words)
        {
            if (word.ElapsedMs is { } elapsed && elapsed > thresholdMs) yield return word.Word;
        }
    }
}
