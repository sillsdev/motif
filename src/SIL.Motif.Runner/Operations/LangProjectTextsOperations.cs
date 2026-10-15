using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
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
/// [ { "id", "words": [wordformId, ...] } ] } ] }</c>. Paragraph text is the segments' wordform forms in that
/// writing system joined by single spaces. Nothing is tokenised or matched by form, and a wordform that is missing
/// or has no form in the paragraph's writing system is a conflict that fails the create. The effect records the Text's identity,
/// name, counts and a digest of its structure, never the other Texts.
/// </remarks>
public static class LangProjectTextsOperationKinds
{
    public const string Create = "texts/langProject/createTexts";

    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Create);
        OperationHandlerRegistry.Register(Create, new CreateTextHandler());
        OperationSlotRegistry.RegisterOwningCreate(Create, SnapshotFields.LangProjectTexts, false);
    }
}

/// <summary>One occurrence-bearing segment of a created paragraph.</summary>
public sealed record TextCreateSegment(CanonicalId Id, IReadOnlyList<CanonicalId> Words);

/// <summary>One created paragraph and its segments.</summary>
public sealed record TextCreateParagraph(CanonicalId Id, string WritingSystem, IReadOnlyList<TextCreateSegment> Segments);

/// <summary>The parsed, closed payload of <see cref="LangProjectTextsOperationKinds.Create"/>.</summary>
public sealed record TextCreatePayload(
    string NameWritingSystem, string NameText, CanonicalId Contents, IReadOnlyList<TextCreateParagraph> Paragraphs);

/// <summary>Parses the closed payload of <see cref="LangProjectTextsOperationKinds.Create"/>.</summary>
public static class LangProjectTextsCreatePayload
{
    public const int MaxParagraphs = 1000;
    public const int MaxWordsPerSegment = 500;
    public const int MaxWords = 20000;

    public static TextCreatePayload Parse(JsonElement after)
    {
        var kind = LangProjectTextsOperationKinds.Create;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["name", "contents", "paragraphs"], kind);
        var name = Required(after, "name", JsonValueKind.Object);
        ClosedPayloadParsing.RequireObject(name, kind);
        ClosedPayloadParsing.RejectUnknownProperties(name, ["ws", "text"], kind);
        var nameWs = ClosedPayloadParsing.GetRequiredString(name, "ws", kind);
        var nameText = ClosedPayloadParsing.GetRequiredString(name, "text", kind);
        if (string.IsNullOrWhiteSpace(nameWs) || string.IsNullOrWhiteSpace(nameText) || nameText != nameText.Trim() ||
            !nameText.IsNormalized(NormalizationForm.FormD))
            throw Refuse("'after.name' needs a writing system and a nonblank NFD title.");
        var contents = ClosedPayloadParsing.GetRequiredCanonicalId(after, "contents", kind);
        var seen = new HashSet<CanonicalId> { contents };
        var paragraphs = new List<TextCreateParagraph>();
        var words = 0;
        var array = Required(after, "paragraphs", JsonValueKind.Array);
        foreach (var item in array.EnumerateArray())
        {
            ClosedPayloadParsing.RequireObject(item, kind);
            ClosedPayloadParsing.RejectUnknownProperties(item, ["id", "ws", "segments"], kind);
            var paragraphId = Unique(ClosedPayloadParsing.GetRequiredCanonicalId(item, "id", kind), seen);
            var ws = ClosedPayloadParsing.GetRequiredString(item, "ws", kind);
            if (string.IsNullOrWhiteSpace(ws)) throw Refuse("each paragraph needs a writing system.");
            var segments = new List<TextCreateSegment>();
            foreach (var segmentItem in Required(item, "segments", JsonValueKind.Array).EnumerateArray())
            {
                ClosedPayloadParsing.RequireObject(segmentItem, kind);
                ClosedPayloadParsing.RejectUnknownProperties(segmentItem, ["id", "words"], kind);
                var segmentId = Unique(ClosedPayloadParsing.GetRequiredCanonicalId(segmentItem, "id", kind), seen);
                var ids = new List<CanonicalId>();
                foreach (var word in Required(segmentItem, "words", JsonValueKind.Array).EnumerateArray())
                {
                    if (word.ValueKind != JsonValueKind.String || !CanonicalId.TryParse(word.GetString()!, out var id))
                        throw Refuse("every word is a wordform identity.");
                    ids.Add(id);
                }
                if (ids.Count == 0 || ids.Count > MaxWordsPerSegment)
                    throw Refuse($"each segment holds 1 to {MaxWordsPerSegment} words.");
                words += ids.Count;
                segments.Add(new TextCreateSegment(segmentId, ids));
            }
            if (segments.Count == 0) throw Refuse("each paragraph holds at least one segment.");
            paragraphs.Add(new TextCreateParagraph(paragraphId, ws, segments));
        }
        if (paragraphs.Count == 0 || paragraphs.Count > MaxParagraphs || words > MaxWords)
            throw Refuse($"a text holds 1 to {MaxParagraphs} paragraphs and at most {MaxWords} words.");
        return new TextCreatePayload(nameWs, nameText, contents, paragraphs);
    }

    private static JsonElement Required(JsonElement parent, string name, JsonValueKind kind) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == kind ? value
            : throw Refuse($"'{name}' is required and must be a{(kind == JsonValueKind.Array ? "n array" : "n object")}.");

    private static CanonicalId Unique(CanonicalId id, HashSet<CanonicalId> seen) =>
        seen.Add(id) ? id : throw Refuse($"identity '{id.Value}' is used twice.");

    private static ContractParseException Refuse(string message) =>
        new($"'{LangProjectTextsOperationKinds.Create}' operation 'after': {message}");
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
        foreach (var created in Identities(entityId, payload))
            if (repository.IsValidObjectId(created.ToGuid()))
                throw new InvalidOperationException($"'{Kind}': identity '{created.Value}' already exists as " +
                    $"{repository.GetObject(created.ToGuid()).ClassName}; this is a semantic conflict. Choose a fresh identity.");
        var nameWs = Handle(cache, payload.NameWritingSystem);
        var resolved = payload.Paragraphs.Select(paragraph => (paragraph, ws: Handle(cache, paragraph.WritingSystem))).ToArray();
        var wordforms = new Dictionary<CanonicalId, IWfiWordform>();
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

    private static IEnumerable<CanonicalId> Identities(CanonicalId entityId, TextCreatePayload payload) =>
        new[] { entityId, payload.Contents }.Concat(payload.Paragraphs.SelectMany(paragraph =>
            new[] { paragraph.Id }.Concat(paragraph.Segments.Select(segment => segment.Id))));

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
