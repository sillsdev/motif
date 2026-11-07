using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Authors a Text using LibLCM's segment and word boundaries. Exact NFD forms reuse existing wordforms;
/// missing forms get declared creations. Free Translation, Literal Translation and Notes belong to segments.
/// </summary>
public static class AuthorTextComposer
{
    private const string ConstructName = "AuthorText";
    private const string Rationale = "Authored by the AuthorText composer.";

    /// <param name="cache">The project whose writing systems and existing forms determine tokenization and reuse.</param>
    /// <param name="scratch">A disposable copy with the same wordforms and writing-system definitions.</param>
    /// <param name="intent">The authored text and annotations.</param>
    /// <param name="mintId">Overrides identity minting for deterministic callers.</param>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, LcmCache scratch, AuthorTextIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(scratch);
        if (ReferenceEquals(cache, scratch)) throw new ArgumentException("The parsing cache must be disposable.", nameof(scratch));
        ArgumentNullException.ThrowIfNull(intent);
        var mint = mintId ?? (() => CanonicalId.Mint());
        var ws = cache.WritingSystemFactory.GetWsFromStr(intent.WritingSystem);
        if (ws == 0) throw new InvalidOperationException($"'{ConstructName}': writing system '{intent.WritingSystem}' is not known to this project.");
        var analysisWs = intent.AnalysisWritingSystem ?? cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        if (!cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Any(system => system.Id == analysisWs))
            throw new InvalidOperationException($"'{ConstructName}': '{analysisWs}' is not an analysis writing system of this project.");
        var project = CanonicalId.FromGuid(cache.LangProject.Guid);
        var repository = cache.ServiceLocator.GetInstance<IWfiWordformRepository>();
        var wordforms = new Dictionary<string, CanonicalId>(StringComparer.Ordinal);
        var creates = new List<OperationEnvelope>();
        CanonicalId Word(string form)
        {
            if (wordforms.TryGetValue(form, out var known)) return known;
            if (repository.TryGetObject(TsStringUtils.MakeString(form, ws), out var existing))
                return wordforms[form] = CanonicalId.FromGuid(existing.Guid);
            var id = mint();
            wordforms[form] = id;
            creates.Add(new OperationEnvelope(mint(), LangProjectWordformsOperationKinds.Create, id, project,
                JsonSerializer.SerializeToElement(new { ws = intent.WritingSystem, text = form }), rationale: Rationale));
            return id;
        }

        var scratchWs = scratch.WritingSystemFactory.GetWsFromStr(intent.WritingSystem);
        if (scratchWs == 0) throw new InvalidOperationException($"'{ConstructName}': the parsing cache lacks the writing system.");
        var parsed = new List<IStTxtPara>();
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
        {
            // Fresh paragraphs bind against the complete inventory that the Proposal creates before the Text.
            for (var pass = 0; pass < 2; pass++)
            {
                var text = scratch.ServiceLocator.GetInstance<ITextFactory>().Create();
                text.ContentsOA = scratch.ServiceLocator.GetInstance<IStTextFactory>().Create();
                foreach (var paragraph in intent.Paragraphs)
                {
                    var para = scratch.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                    text.ContentsOA.ParagraphsOS.Add(para);
                    para.Contents = TsStringUtils.MakeString(string.Join(" ", paragraph.Segments.Select(segment => segment.Text)), scratchWs);
                    ParagraphParser.ParseParagraph(para);
                    if (pass == 0)
                    {
                        foreach (var analysis in para.SegmentsOS.SelectMany(segment => segment.AnalysesRS))
                            if (analysis.Wordform is not null) Word(analysis.Wordform.Form.get_String(scratchWs).Text);
                    }
                    else parsed.Add(para);
                }
            }
        });
        var paragraphs = intent.Paragraphs.Select((paragraph, paragraphIndex) =>
        {
            var baseline = string.Join(" ", paragraph.Segments.Select(segment => segment.Text));
            var text = TsStringUtils.MakeString(baseline, ws);
            var boundaries = text.GetSegments(cache.WritingSystemFactory);
            var byOffset = boundaries.ToDictionary(segment => segment.IchMin);
            var annotations = new Dictionary<int, AuthorTextSegment>();
            var offset = 0;
            foreach (var authored in paragraph.Segments)
            {
                if (authored.FreeTranslation is not null || authored.LiteralTranslation is not null || authored.Notes?.Count > 0)
                {
                    if (!byOffset.TryGetValue(offset, out var boundary) || boundary.Text.TrimEnd() != authored.Text)
                        throw new InvalidOperationException($"'{ConstructName}': annotated text must be exactly one FieldWorks segment; use sentence punctuation or a separate paragraph.");
                    annotations[offset] = authored;
                }
                offset += authored.Text.Length + 1;
            }
            return new
            {
                id = mint().Value, ws = intent.WritingSystem, text = baseline,
                segments = parsed[paragraphIndex].SegmentsOS.Select(segment =>
                {
                    annotations.TryGetValue(segment.BeginOffset, out var annotation);
                    return new
                    {
                        id = mint().Value, beginOffset = segment.BeginOffset,
                        words = segment.AnalysesRS.Where(item => item.Wordform is not null)
                            .Select(item => Word(item.Wordform.Form.get_String(scratchWs).Text).Value).ToArray(),
                        freeTranslation = annotation?.FreeTranslation,
                        literalTranslation = annotation?.LiteralTranslation,
                        notes = annotation?.Notes?.Select(note => new { id = mint().Value, text = note }).ToArray(),
                    };
                }).ToArray(),
            };
        }).ToArray();
        var after = JsonSerializer.SerializeToElement(new
        {
            name = new { ws = intent.WritingSystem, text = intent.Title }, analysisWs,
            contents = mint().Value, paragraphs,
        }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        LangProjectTextsCreatePayload.Parse(after);
        foreach (var create in creates) LangProjectWordformsCreatePayload.Parse(create.After!.Value);
        var createText = new OperationEnvelope(mint(), LangProjectTextsOperationKinds.Create, mint(), project, after,
            dependsOn: creates.Select(create => new OperationDependency(create.OperationId)).ToArray(), rationale: Rationale);
        return [.. creates, createText];
    }
}
