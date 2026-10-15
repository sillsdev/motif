using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Authors one Text and the wordforms its tokens need. A token reuses the existing wordform whose form is that
/// exact NFD string in the intent's writing system; any other token gets one new wordform, created by an operation
/// the Text operation declares it depends on. No wordform is matched by similarity.
/// </summary>
public static class AuthorTextComposer
{
    private const string ConstructName = "AuthorText";
    private const string Rationale = "Authored by the AuthorText composer.";

    /// <param name="cache">The project that decides which tokens already have a wordform.</param>
    /// <param name="intent">The authored construct.</param>
    /// <param name="mintId">Overrides id minting; tests supply a deterministic source.</param>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, AuthorTextIntent intent, Func<CanonicalId>? mintId = null)
    {
        if (cache is null) throw new ArgumentNullException(nameof(cache));
        if (intent is null) throw new ArgumentNullException(nameof(intent));
        var mint = mintId ?? (() => CanonicalId.Mint());
        var ws = cache.WritingSystemFactory.GetWsFromStr(intent.WritingSystem);
        if (ws == 0)
            throw new InvalidOperationException(
                $"'{ConstructName}': writing system '{intent.WritingSystem}' is not known to this project.");
        var project = CanonicalId.FromGuid(cache.LangProject.Guid);
        var repository = cache.ServiceLocator.GetInstance<IWfiWordformRepository>();

        var wordforms = new Dictionary<string, CanonicalId>(StringComparer.Ordinal);
        var creates = new List<OperationEnvelope>();
        foreach (var token in intent.Paragraphs.SelectMany(p => p.Segments).SelectMany(s => s))
        {
            if (wordforms.ContainsKey(token)) continue;
            if (repository.TryGetObject(TsStringUtils.MakeString(token, ws), out var existing))
            {
                wordforms[token] = CanonicalId.FromGuid(existing.Guid);
                continue;
            }
            var id = mint();
            wordforms[token] = id;
            creates.Add(new OperationEnvelope(
                operationId: mint(), kind: LangProjectWordformsOperationKinds.Create, entityId: id, target: project,
                after: JsonSerializer.SerializeToElement(new { ws = intent.WritingSystem, text = token }),
                rationale: Rationale));
        }

        var textId = mint();
        var after = JsonSerializer.SerializeToElement(new
        {
            name = new { ws = intent.WritingSystem, text = intent.Title },
            contents = mint().Value,
            paragraphs = intent.Paragraphs.Select(paragraph => new
            {
                id = mint().Value,
                ws = intent.WritingSystem,
                segments = paragraph.Segments.Select(segment => new
                {
                    id = mint().Value,
                    words = segment.Select(token => wordforms[token].Value).ToArray(),
                }).ToArray(),
            }).ToArray(),
        });
        LangProjectTextsCreatePayload.Parse(after);
        foreach (var create in creates) LangProjectWordformsCreatePayload.Parse(create.After!.Value);

        var createText = new OperationEnvelope(
            operationId: mint(), kind: LangProjectTextsOperationKinds.Create, entityId: textId, target: project,
            after: after, dependsOn: creates.Select(create => new OperationDependency(create.OperationId)).ToArray(),
            rationale: Rationale);
        return [.. creates, createText];
    }
}
