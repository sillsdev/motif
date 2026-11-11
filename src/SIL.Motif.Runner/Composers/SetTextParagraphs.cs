using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Resolution;

namespace SIL.Motif.Runner.Composers;

/// <summary>A person's edit of one StText-valued field: the owner, the field and the paragraphs' new state.</summary>
public sealed record SetTextParagraphsIntent(CanonicalId Target, TextParagraphsPayload Paragraphs);

/// <summary>Lowers a person's paragraph edit to one <c>text/setParagraphs</c> operation.</summary>
public static class SetTextParagraphsComposer
{
    public const string Name = "SetTextParagraphs";

    /// <summary>
    /// The operation for the edit, or none when it changes and removes nothing, so a field edited back to its start
    /// leaves the Draft.
    /// </summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SetTextParagraphsIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var after = intent.Paragraphs.ToJson();
        var payload = TextParagraphsPayload.Parse(after);
        TextParagraphFields.Read(CanonicalIdResolver.Resolve(cache, intent.Target), payload.Field);
        if (payload.Changed.Count == 0 && payload.Removed.Count == 0) return [];
        RichTextRuns.RequireStyles(cache, payload.Changed.Select(change => change.Style),
            payload.Changed.SelectMany(change => change.Runs));
        return [new(CanonicalId.Mint(), TextParagraphsOperationKinds.Set, target: intent.Target, after: after,
            rationale: $"Edit the paragraphs of {payload.Field}.")];
    }

    /// <summary>The authored intent JSON the Draft's provenance and replacement read: the target plus the payload.</summary>
    public static JsonElement Authored(SetTextParagraphsIntent intent)
    {
        var after = intent.Paragraphs.ToJson();
        var authored = new Dictionary<string, JsonElement> { ["target"] = JsonSerializer.SerializeToElement(intent.Target.Value) };
        foreach (var property in after.EnumerateObject()) authored[property.Name] = property.Value.Clone();
        return JsonSerializer.SerializeToElement(authored);
    }
}
