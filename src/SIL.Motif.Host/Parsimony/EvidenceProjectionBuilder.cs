using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Texts;
using SIL.Motif.Projection.HumanJudgments;

namespace SIL.Motif.Host.Parsimony;

/// <summary>Reads the Baseline's Texts, wordforms, and analyses into disposable report evidence.</summary>
public static class EvidenceProjectionBuilder
{
    /// <summary>Projects every project wordform and every saved Text occurrence from one loaded Baseline.</summary>
    public static ParsimonyEvidenceProjection Build(LcmCache cache, TextWordsProjection textProjection,
        ParsimonyScopeBinding scopeBinding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(textProjection);
        ArgumentNullException.ThrowIfNull(scopeBinding);
        cancellationToken.ThrowIfCancellationRequested();
        var wordforms = new Dictionary<Guid, EvidenceWordform>();
        foreach (var wordform in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
                     .OrderBy(item => item.Guid.ToString("D"), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var forms = ReadForms(cache, wordform.Form);
            var approved = wordform.HumanApprovedAnalyses.ToHashSet();
            var disapproved = wordform.HumanDisapprovedParses.ToHashSet();
            var analyses = wordform.AnalysesOC.OrderBy(item => item.Guid.ToString("D"), StringComparer.Ordinal)
                .Select(analysis =>
                {
                    var morphs = analysis.MorphBundlesOS.Select(bundle =>
                    {
                        var morphForms = ReadForms(cache, bundle.Form);
                        var glosses = bundle.SenseRA is { } sense ? ReadForms(cache, sense.Gloss) : [];
                        var category = analysis.CategoryRA;
                        var categoryTexts = category is null ? [] : ReadForms(cache,
                            category.Abbreviation.AvailableWritingSystemIds.Any()
                                ? category.Abbreviation
                                : category.Name);
                        return new EvidenceMorph(bundle.MorphRA?.Guid, bundle.MsaRA?.Guid,
                            bundle.InflTypeRA?.Guid, bundle.MorphRA?.Owner?.Guid, bundle.SenseRA?.Guid,
                            morphForms, glosses, categoryTexts);
                    }).ToArray();
                    var opinion = approved.Contains(analysis) ? "approved" : disapproved.Contains(analysis)
                        ? "disapproved" : "unknown";
                    return new EvidenceAnalysis(analysis.Guid, opinion, "fieldworks", Digest(morphs), morphs);
            }).ToArray();
            wordforms.Add(wordform.Guid, new EvidenceWordform(wordform.Guid,
                (int)wordform.SpellingStatus, forms, analyses));
        }

        var texts = new List<EvidenceText>();
        var occurrences = new List<EvidenceOccurrence>();
        foreach (var text in textProjection.Texts)
        {
            texts.Add(new EvidenceText(text.TextId, text.Title, text.TitleWritingSystem));
            foreach (var line in text.Lines)
            {
                foreach (var token in line.Tokens)
                {
                    var forms = token.Forms.Select(form => new EvidenceForm(
                        form.WritingSystem, form.Text, Normalize(form.Text))).ToArray();
                    occurrences.Add(new EvidenceOccurrence(text.TextId, line.ParagraphId, line.SegmentId,
                        token.OccurrenceIndex, token.WordformId, token.Text, token.AnalysisId,
                        token.Status, forms));
                }
            }
        }
        var segments = textProjection.Texts.SelectMany(text => text.Lines.Select(line => new EvidenceSegment(
            text.TextId, line.ParagraphId, line.SegmentId, line.Number, line.Sentence,
            line.SentenceWritingSystem, line.ParseIsCurrent))).ToArray();
        var orderedWordforms = wordforms.Values.OrderBy(item => item.Guid.ToString("D"), StringComparer.Ordinal).ToArray();
        var scopeData = BuildScopes(scopeBinding, textProjection, orderedWordforms);
        var humanJudgments = JudgmentLineageResolver.Resolve(HumanJudgmentReader.Read(cache));
        var expectations = ParsimonyExpectationProjectionBuilder.Build(cache);
        return new ParsimonyEvidenceProjection(texts, segments, occurrences, orderedWordforms,
            scopeData.Scopes, scopeData.Texts, scopeData.Words, CaptureCapabilities(), humanJudgments)
        {
            ReviewedNegatives = expectations.ReviewedNegatives,
        };
    }

    private static EvidenceForm[] ReadForms(LcmCache cache, IMultiAccessorBase accessor)
    {
        var strings = (ITsMultiString)accessor;
        return accessor.AvailableWritingSystemIds.Order()
            .Select(ws => (Tag: cache.WritingSystemFactory.GetStrFromWs(ws), Text: strings.get_String(ws)?.Text))
            .Where(form => !string.IsNullOrEmpty(form.Text))
            .Select(form => new EvidenceForm(form.Tag, form.Text!, Normalize(form.Text!))).ToArray();
    }

    private static EvidenceScopeRows BuildScopes(ParsimonyScopeBinding binding, TextWordsProjection textProjection,
        IReadOnlyList<EvidenceWordform> wordforms)
    {
        var scopes = new List<EvidenceScopeDescriptor>();
        var scopeTexts = new List<EvidenceScopeText>();
        var scopeWords = new List<EvidenceScopeWord>();
        var projectApproved = wordforms.Where(wordform => wordform.Analyses.Any(analysis =>
                analysis.Opinion is "approved" or "disapproved"))
            .OrderBy(wordform => wordform.Guid.ToString("D"), StringComparer.Ordinal).ToArray();
        scopes.Add(new EvidenceScopeDescriptor("project-approved", "complete", null, null,
            projectApproved.Length, null, "{}"));
        var ordinal = 0;
        foreach (var wordform in projectApproved)
        {
            if (wordform.Forms.Count == 0)
                scopeWords.Add(new EvidenceScopeWord("project-approved", ordinal++, null, null,
                    wordform.Guid, "judged-wordform"));
            foreach (var form in wordform.Forms)
                scopeWords.Add(new EvidenceScopeWord("project-approved", ordinal++, form.TextNfd,
                    form.WritingSystem, wordform.Guid, "judged-wordform"));
        }

        var selection = binding.Selection;
        if (selection is null)
        {
            scopes.Add(new EvidenceScopeDescriptor("default-selection", "unavailable", null, null,
                0, null, "{}", "default_selection_not_configured"));
            return new EvidenceScopeRows(scopes, scopeTexts, scopeWords);
        }

        var textIds = selection.TextIds.ToHashSet();
        var presentTextIds = textProjection.Texts.Select(text => text.TextId).ToHashSet();
        var missingTextId = textIds.FirstOrDefault(id => !presentTextIds.Contains(id));
        if (missingTextId != Guid.Empty)
            throw new InvalidDataException($"The frozen Selection names Text '{missingTextId:D}' outside its evidence projection.");
        var resolvedWords = selection.ResolvedWords.ToHashSet(StringComparer.Ordinal);
        var membership = new HashSet<(string Form, string? WritingSystem, Guid Wordform)>();
        foreach (var text in textProjection.Texts.Where(text => textIds.Contains(text.TextId)))
        {
            scopeTexts.Add(new EvidenceScopeText("default-selection", text.TextId));
            foreach (var token in text.Lines.SelectMany(line => line.Tokens))
            {
                if (token.WordformId is not { } wordformGuid) continue;
                foreach (var form in token.Forms)
                {
                    var normalized = Normalize(form.Text);
                    if (resolvedWords.Contains(normalized)) membership.Add((normalized, form.WritingSystem, wordformGuid));
                }
            }
        }
        ordinal = 0;
        foreach (var item in membership.OrderBy(item => item.Form, StringComparer.Ordinal)
                     .ThenBy(item => item.WritingSystem, StringComparer.Ordinal)
                     .ThenBy(item => item.Wordform.ToString("D"), StringComparer.Ordinal))
            scopeWords.Add(new EvidenceScopeWord("default-selection", ordinal++, item.Form,
                item.WritingSystem, item.Wordform, "text"));
        foreach (var typed in selection.AddedWords.Select(Normalize).Where(resolvedWords.Contains)
                     .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            scopeWords.Add(new EvidenceScopeWord("default-selection", ordinal++, typed, null, null, "typed"));

        var represented = membership.Select(item => item.Form).Concat(selection.AddedWords.Select(Normalize))
            .ToHashSet(StringComparer.Ordinal);
        if (selection.ResolvedWords.Any(word => !represented.Contains(word)))
            throw new InvalidDataException("The frozen Selection contains a word outside its Text and typed-word sources.");
        var sourceJson = JsonSerializer.Serialize(new
        {
            textIds = selection.TextIds.Select(GuidText).ToArray(),
            addedWords = selection.AddedWords,
        });
        // Approved analyses reach a measure only through the Selection's wordforms, so none means nothing to read.
        var selectionWordforms = membership.Select(item => item.Wordform).ToHashSet();
        var hasApprovedWords = wordforms.Any(wordform => selectionWordforms.Contains(wordform.Guid) &&
            wordform.Analyses.Any(analysis => analysis.Opinion == "approved"));
        scopes.Add(hasApprovedWords
            ? new EvidenceScopeDescriptor("default-selection", "complete", selection.Name,
                selection.SelectionSha256, selection.ResolvedWords.Count, selection.TextIds.Count, sourceJson)
            : new EvidenceScopeDescriptor("default-selection", "unavailable", selection.Name,
                selection.SelectionSha256, selection.ResolvedWords.Count, selection.TextIds.Count, sourceJson,
                "no_approved_words"));
        return new EvidenceScopeRows(scopes, scopeTexts, scopeWords);
    }

    private static EvidenceCaptureCapability[] CaptureCapabilities() =>
    [
        new("analyses", "complete", null),
        new("analysis-morph-identities", "complete", null),
        new("analysis-morph-forms", "complete", null),
        new("occurrence-analysis-links", "complete", null),
        new("selection-membership", "complete", null),
        new("judge-identity", "unavailable", "FieldWorks does not expose the judging person in this projection."),
        new("judgment-time", "unavailable", "FieldWorks does not expose a judgment time in this projection."),
        new("genre", "unavailable", "Text genre is not captured."),
        new("character-offsets", "unavailable", "Token character offsets are not captured."),
        new("phonological-boundaries", "unavailable", "No phonological boundary evidence is captured."),
        new("expectations", "complete", null),
        new("reviewed-negative-expectations", "complete", null),
        new("parser-overlay", "not_requested", "No Assessment overlay was requested."),
        new("PhonRule.uses", "unavailable", "PanGloss did not supply this counter for the captured parser run."),
    ];

    internal static string Normalize(string value) => value.Normalize(NormalizationForm.FormD);

    private static string GuidText(Guid value) => value.ToString("D").ToLowerInvariant();

    private static string Digest(IReadOnlyList<EvidenceMorph> morphs)
    {
        var canonical = JsonSerializer.Serialize(morphs.Select(morph => new
        {
            morph.MorphGuid,
            morph.MsaGuid,
            morph.InflTypeGuid,
            morph.EntryGuid,
            morph.SenseGuid,
            forms = morph.Forms.Select(form => new { form.WritingSystem, form.TextNfd }),
            glosses = morph.Glosses.Select(form => new { form.WritingSystem, form.TextNfd }),
            categories = morph.Categories.Select(form => new { form.WritingSystem, form.TextNfd }),
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>One frozen evidence projection before it is written to SQLite.</summary>
public sealed record ParsimonyEvidenceProjection(
    IReadOnlyList<EvidenceText> Texts,
    IReadOnlyList<EvidenceSegment> Segments,
    IReadOnlyList<EvidenceOccurrence> Occurrences,
    IReadOnlyList<EvidenceWordform> Wordforms,
    IReadOnlyList<EvidenceScopeDescriptor> Scopes,
    IReadOnlyList<EvidenceScopeText> ScopeTexts,
    IReadOnlyList<EvidenceScopeWord> ScopeWords,
    IReadOnlyList<EvidenceCaptureCapability> Capabilities,
    HumanJudgmentLineageProjection HumanJudgments)
{
    /// <summary>The optional parser cases explicitly named for this report.</summary>
    public ParserOverlayProjection ParserOverlay { get; init; } = ParserOverlayProjection.NotRequested;

    /// <summary>The project-backed reviewed-negative input used by parser-tier measures.</summary>
    public IReadOnlyList<ReviewedNegativeExpectation> ReviewedNegatives { get; init; } = [];
}

/// <summary>One Text identity and its available title.</summary>
public sealed record EvidenceText(Guid Guid, string Title, string? WritingSystem);

/// <summary>One ordered sentence segment in a Text.</summary>
public sealed record EvidenceSegment(Guid TextGuid, Guid ParagraphGuid, Guid SegmentGuid, int LineNumber,
    string Sentence, string? WritingSystem, bool ParseCurrent);

/// <summary>One token occurrence and the analysis selected for it, if any.</summary>
public sealed record EvidenceOccurrence(Guid TextGuid, Guid ParagraphGuid, Guid SegmentGuid, int Ordinal,
    Guid? WordformGuid, string Text, Guid? SelectedAnalysisGuid, string? Status, IReadOnlyList<EvidenceForm> Forms);

/// <summary>One project wordform, including analyses whether or not a Text uses them.</summary>
public sealed record EvidenceWordform(Guid Guid, int SpellingStatus, IReadOnlyList<EvidenceForm> Forms,
    IReadOnlyList<EvidenceAnalysis> Analyses);

/// <summary>One populated writing-system form in a wordform, token, or morph.</summary>
public sealed record EvidenceForm(string? WritingSystem, string Text, string TextNfd);

/// <summary>One FieldWorks analysis with its human Opinion and ordered morph bundles.</summary>
public sealed record EvidenceAnalysis(Guid Guid, string Opinion, string SourceKind, string ContentSha256,
    IReadOnlyList<EvidenceMorph> Morphs);

/// <summary>One ordered morph bundle with nullable authored identity references.</summary>
public sealed record EvidenceMorph(Guid? MorphGuid, Guid? MsaGuid, Guid? InflTypeGuid,
    Guid? EntryGuid, Guid? SenseGuid, IReadOnlyList<EvidenceForm> Forms,
    IReadOnlyList<EvidenceForm> Glosses, IReadOnlyList<EvidenceForm> Categories);

/// <summary>One named evidence scope and its frozen identity.</summary>
public sealed record EvidenceScopeDescriptor(string ScopeId, string Status, string? Name,
    string? SelectionSha256, int WordCount, int? TextCount, string ProvenanceJson, string? ReasonCode = null);

/// <summary>One Text identity included in a captured scope.</summary>
public sealed record EvidenceScopeText(string ScopeId, Guid TextGuid);

/// <summary>One word or wordform membership in a captured scope.</summary>
public sealed record EvidenceScopeWord(string ScopeId, int Ordinal, string? Form, string? WritingSystem,
    Guid? WordformGuid, string SourceKind);

/// <summary>Whether the artifact captured or requested one evidence capability.</summary>
public sealed record EvidenceCaptureCapability(string Capability, string Status, string? Reason);

internal sealed record EvidenceScopeRows(IReadOnlyList<EvidenceScopeDescriptor> Scopes,
    IReadOnlyList<EvidenceScopeText> Texts, IReadOnlyList<EvidenceScopeWord> Words);
