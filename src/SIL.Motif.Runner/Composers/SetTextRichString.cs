using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Resolution;

namespace SIL.Motif.Runner.Composers;

/// <summary>A person's edit of one String field, or one writing system of a MultiString field: the owner and the new runs.</summary>
public sealed record SetTextRichStringIntent(CanonicalId Target, TextRichStringPayload RichString);

/// <summary>Lowers a person's rich-string edit to one <c>text/setRichString</c> operation.</summary>
public static class SetTextRichStringComposer
{
    public const string Name = "SetTextRichString";

    /// <summary>
    /// The operation for the edit, or none when the runs are what the edit began from, so a field edited back to its
    /// start leaves the Draft.
    /// </summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SetTextRichStringIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var payload = Canonical(cache, TextRichStringPayload.Parse(intent.RichString.ToJson()));
        var after = payload.ToJson();
        var owner = CanonicalIdResolver.Resolve(cache, intent.Target);
        var flid = TextRichStringFields.Flid(owner, payload.Field);
        if (payload.Text.Normalize(NormalizationForm.FormC) == payload.Expected.Text.Normalize(NormalizationForm.FormC) &&
            RunsDigest(payload) == payload.Expected.Runs) return [];
        var ws = payload.Ws is { } tag ? RichTextRuns.WritingSystem(cache, tag, TextRichStringOperationKinds.Set) : 0;
        RichTextRuns.RequireStyles(cache, [], payload.Runs,
            RichTextRuns.StylesIn(TextRichStringFields.Read(owner, flid, ws)).ToHashSet(StringComparer.Ordinal));
        return [new(CanonicalId.Mint(), TextRichStringOperationKinds.Set, target: intent.Target, after: after,
            rationale: $"Edit the {payload.Field}{(payload.Ws is null ? "" : $" ({payload.Ws})")}.")];
    }

    /// <summary>The authored intent JSON the Draft's provenance and replacement read: the target plus the payload.</summary>
    public static JsonElement Authored(SetTextRichStringIntent intent)
    {
        var after = intent.RichString.ToJson();
        var authored = new Dictionary<string, JsonElement> { ["target"] = JsonSerializer.SerializeToElement(intent.Target.Value) };
        foreach (var property in after.EnumerateObject()) authored[property.Name] = property.Value.Clone();
        return JsonSerializer.SerializeToElement(authored);
    }

    /// <summary>
    /// <paramref name="payload"/> with every writing-system tag as the project spells it, so an edit's slot and its
    /// replacement match however the tag was cased; a tag the project lacks is refused.
    /// </summary>
    public static TextRichStringPayload Canonical(LcmCache cache, TextRichStringPayload payload)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(payload);
        string Tag(string ws) => cache.WritingSystemFactory.GetStrFromWs(RichTextRuns.WritingSystem(cache, ws, TextRichStringOperationKinds.Set));
        return payload with { Ws = payload.Ws is { } ws ? Tag(ws) : null, Runs = payload.Runs.Select(run => run with { Ws = Tag(run.Ws) }).ToArray() };
    }

    // The digest the runs would have once stored, as the payload states them; empty text has no runs.
    private static string RunsDigest(TextRichStringPayload payload) => Contract.Responses.TextRunsDigest.Of(payload.Runs
        .Where(run => run.Text.Length > 0)
        .Select(run => new Contract.Responses.TextRunsDigest.RunFacts(run.Text, run.Ws,
            (run.Props?.Ints ?? new Dictionary<int, (int, int)>()).Select(pair => KeyValuePair.Create(pair.Key, pair.Value)),
            (run.Props?.Strings ?? new Dictionary<int, string>())
                .Concat(run.CharacterStyle is { } style ? [KeyValuePair.Create(133, style)] : []), run.ObjData)));
}
