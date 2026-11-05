using System.Runtime.CompilerServices;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the operation kind that creates a Text with its paragraphs, segments and occurrences, targeting
/// the project root.
/// </summary>
/// <remarks>
/// The closed <c>after</c> payload names every created identity and binds every occurrence to an existing
/// wordform by identity: <c>{ "name": { "ws", "text" }, "contents": id, "paragraphs": [ { "id", "ws", "segments":
/// [ { "id", "words": [wordformId, ...] } ] } ] }</c>. Legacy paragraphs join wordform forms by spaces.
/// A paragraph with text supplies segment beginOffset values and is parsed by LibLCM; annotations use analysisWs.
/// Wordform bindings remain explicit, and a wordform that is missing
/// or has no form in the paragraph's writing system is a conflict that fails the create. The effect records the Text's identity,
/// name, counts and a digest of its structure, never the other Texts.
/// </remarks>
public static class LangProjectTextsOperationKinds
{
    public const string Create = "texts/langProject/createTexts";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Create);
        OperationHandlerRegistry.Register(Create, new CreateTextHandler());
        OperationSlotRegistry.RegisterOwningCreate(Create, SnapshotFields.LangProjectTexts, false);
    }
#pragma warning restore CA2255
}

internal sealed class CreateTextHandler : IOperationHandler
{
    private const string Kind = LangProjectTextsOperationKinds.Create;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{Kind}': storage GUID overrides are not supported for this create.");
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        if (operation.Placement is not null)
            throw new InvalidOperationException($"'{Kind}': placement is not defined for texts.");
        var payload = LangProjectTextsCreatePayload.Parse(operation.After ??
            throw new InvalidOperationException($"'{Kind}' requires after."));
        var id = ResolveProject(cache, operation);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        foreach (var (created, expectedClass) in Identities(entityId, payload))
            CreateIdentity.RequireFresh(cache, created, Kind, expectedClass);
        var nameWs = Handle(cache, payload.NameWritingSystem);
        var analysisWs = payload.AnalysisWritingSystem is { } analysisTag ? Handle(cache, analysisTag) : cache.DefaultAnalWs;
        if (!cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Any(system => system.Handle == analysisWs))
            throw new InvalidOperationException($"'{Kind}': annotations need a project analysis writing system.");
        var resolved = payload.Paragraphs.Select(paragraph => (paragraph, ws: Handle(cache, paragraph.WritingSystem))).ToArray();
        var wordforms = new Dictionary<CanonicalId, IWfiWordform>(CanonicalId.ByGuid);
        foreach (var (paragraph, ws) in resolved)
            foreach (var word in paragraph.Segments.SelectMany(segment => segment.Words))
                if (!wordforms.ContainsKey(word))
                    wordforms[word] = repository.TryGetObject(word.ToGuid(), out var found) && found is IWfiWordform wordform
                        ? wordform : throw new InvalidOperationException(
                            $"'{Kind}': '{word.Value}' is not an existing wordform. Create it first.");

        var before = Read(cache, entityId);
        var services = cache.ServiceLocator;
        var text = services.GetInstance<ITextFactory>().Create(entityId.ToGuid());
        text.Name.set_String(nameWs, TsStringUtils.MakeString(payload.NameText, nameWs));
        text.ContentsOA = services.GetInstance<IStTextFactory>().Create(payload.Contents.ToGuid());
        foreach (var (paragraph, ws) in resolved)
        {
            if (paragraph.Text is not null)
            {
                CreateParsedParagraph(cache, text.ContentsOA, paragraph, ws, analysisWs, wordforms);
                continue;
            }
            var forms = paragraph.Segments.Select(segment => segment.Words.Select(word =>
                    Form(wordforms[word], ws, paragraph.WritingSystem, word)).ToArray()).ToArray();
            var created = services.GetInstance<IStTxtParaFactory>().Create(paragraph.Id.ToGuid());
            text.ContentsOA.ParagraphsOS.Add(created);
            created.Contents = TsStringUtils.MakeString(string.Join(" ", forms.SelectMany(line => line)), ws);
            while (created.SegmentsOS.Count > 0) created.SegmentsOS.RemoveAt(created.SegmentsOS.Count - 1);
            var offset = 0;
            for (var index = 0; index < paragraph.Segments.Count; index++)
            {
                var segment = services.GetInstance<ISegmentFactory>().Create(created, offset, cache,
                    paragraph.Segments[index].Id.ToGuid());
                offset += string.Join(" ", forms[index]).Length + 1;
                foreach (var word in paragraph.Segments[index].Words) segment.AnalysesRS.Add(wordforms[word]);
                Annotate(cache, segment, paragraph.Segments[index], analysisWs);
            }
            created.ParseIsCurrent = true;
        }
        touchedTargets.Add(id);
        return new ExpectedEffect(id, SnapshotFields.LangProjectTexts, before, Read(cache, entityId));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var id = ResolveProject(cache, operation);
        var current = Read(cache, entityId);
        return new ExpectedEffect(id, SnapshotFields.LangProjectTexts, current, current);
    }

    private static IEnumerable<(CanonicalId Id, string ExpectedClass)> Identities(CanonicalId entityId,
        TextCreatePayload payload) =>
        new[] { (entityId, "Text"), (payload.Contents, "StText") }.Concat(payload.Paragraphs.SelectMany(paragraph =>
            new[] { (paragraph.Id, "StTxtPara") }.Concat(paragraph.Segments.SelectMany(segment =>
                new[] { (segment.Id, "Segment") }.Concat(segment.Notes.Select(note => (note.Id, "Note")))))));

    private static void CreateParsedParagraph(LcmCache cache, IStText contents, TextCreateParagraph payload, int ws,
        int analysisWs, IReadOnlyDictionary<CanonicalId, IWfiWordform> wordforms)
    {
        var baseline = TsStringUtils.MakeString(payload.Text!, ws);
        var boundaries = baseline.GetSegments(cache.WritingSystemFactory);
        if (boundaries.Count != payload.Segments.Count)
            throw new InvalidOperationException($"'{Kind}': FieldWorks segment boundaries changed; compose the text again.");
        for (var index = 0; index < boundaries.Count; index++)
        {
            var planned = payload.Segments[index];
            if (boundaries[index].IchMin != planned.BeginOffset)
                throw new InvalidOperationException($"'{Kind}': FieldWorks segment boundaries changed; compose the text again.");
            foreach (var word in planned.Words) _ = Form(wordforms[word], ws, payload.WritingSystem, word);
        }
        var para = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create(payload.Id.ToGuid());
        contents.ParagraphsOS.Add(para);
        para.Contents = baseline;
        while (para.SegmentsOS.Count > 0) para.SegmentsOS.RemoveAt(para.SegmentsOS.Count - 1);
        foreach (var planned in payload.Segments)
            cache.ServiceLocator.GetInstance<ISegmentFactory>().Create(para, planned.BeginOffset!.Value, cache, planned.Id.ToGuid());
        using (var parser = new ParagraphParser(cache)) parser.ForceParse(para);
        for (var index = 0; index < payload.Segments.Count; index++)
        {
            var planned = payload.Segments[index];
            var segment = para.SegmentsOS[index];
            if (segment.Guid != planned.Id.ToGuid())
                throw new InvalidOperationException($"'{Kind}': FieldWorks did not preserve the planned segment identity.");
            var word = 0;
            for (var occurrence = 0; occurrence < segment.AnalysesRS.Count; occurrence++)
                if (segment.AnalysesRS[occurrence].Wordform is not null)
                {
                    if (word >= planned.Words.Count)
                        throw new InvalidOperationException($"'{Kind}': FieldWorks returned a different word count.");
                    var expected = wordforms[planned.Words[word++]];
                    if (segment.AnalysesRS[occurrence].Wordform.Guid != expected.Guid)
                        throw new InvalidOperationException($"'{Kind}': FieldWorks word bindings changed; compose the text again.");
                    if (!ReferenceEquals(segment.AnalysesRS[occurrence], expected)) segment.AnalysesRS[occurrence] = expected;
                }
            if (word != planned.Words.Count)
                throw new InvalidOperationException($"'{Kind}': FieldWorks returned a different word count.");
            Annotate(cache, segment, planned, analysisWs);
        }
    }

    private static void Annotate(LcmCache cache, ISegment segment, TextCreateSegment payload, int ws)
    {
        if (payload.FreeTranslation is { } free) segment.FreeTranslation.set_String(ws, TsStringUtils.MakeString(free, ws));
        if (payload.LiteralTranslation is { } literal) segment.LiteralTranslation.set_String(ws, TsStringUtils.MakeString(literal, ws));
        foreach (var item in payload.Notes)
        {
            var note = cache.ServiceLocator.GetInstance<INoteFactory>().Create(item.Id.ToGuid());
            segment.NotesOS.Add(note);
            note.Content.set_String(ws, TsStringUtils.MakeString(item.Text, ws));
        }
    }

    private static string Form(IWfiWordform wordform, int ws, string tag, CanonicalId id)
    {
        var text = wordform.Form.get_String(ws).Text;
        return string.IsNullOrEmpty(text)
            ? throw new InvalidOperationException($"'{Kind}': wordform '{id.Value}' has no form in '{tag}'.")
            : text.Normalize(NormalizationForm.FormD);
    }

    private static CanonicalId ResolveProject(LcmCache cache, OperationEnvelope operation)
    {
        var (id, project) = TargetResolution.Resolve<ILangProject>(cache, operation, Kind);
        if (project.Guid != cache.LangProject.Guid)
            throw new InvalidOperationException($"'{Kind}': texts are created only in this project.");
        return id;
    }

    private static int Handle(LcmCache cache, string ws)
    {
        var handle = cache.WritingSystemFactory.GetWsFromStr(ws);
        return handle != 0 ? handle
            : throw new InvalidOperationException($"'{Kind}' operation: writing system tag '{ws}' is not known to this project.");
    }

    private static Dictionary<string, string> Read(LcmCache cache, CanonicalId entityId)
    {
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        if (!repository.TryGetObject(entityId.ToGuid(), out var found)) return new Dictionary<string, string>(StringComparer.Ordinal);
        return found is IText text ? TextOwningSnapshots.Summary(cache, text)
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["collision"] = entityId.Value };
    }
}
