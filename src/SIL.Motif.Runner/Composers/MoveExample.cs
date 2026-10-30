using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Moves one example sentence, with its translations, to another sense, which may belong to another entry.</summary>
/// <param name="Example">The example to move.</param>
/// <param name="ToSense">The sense that owns it afterwards.</param>
/// <param name="After">The example of <paramref name="ToSense"/> to place it after; absent with <paramref name="Before"/> means last.</param>
/// <param name="Before">The example of <paramref name="ToSense"/> to place it before.</param>
public sealed record MoveExampleIntent(CanonicalId Example, CanonicalId ToSense, CanonicalId? After = null, CanonicalId? Before = null);

/// <summary>Parses the closed JSON intent for moving an example.</summary>
public static class MoveExampleIntentParser
{
    /// <summary>Reads the example and the target sense, and at most one of <c>after</c> and <c>before</c>.</summary>
    public static MoveExampleIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "MoveExample", "example", "toSense", "after", "before");
        if (authored.TryGetProperty("after", out _) && authored.TryGetProperty("before", out _))
            throw new Contract.Parsing.ContractParseException("'MoveExample': give at most one of 'after' and 'before'.");
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "example")),
            LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "toSense")),
            authored.TryGetProperty("after", out _) ? LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "after")) : null,
            authored.TryGetProperty("before", out _) ? LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "before")) : null);
    }
}

/// <summary>
/// Lowers an example move to one <see cref="LexExampleOperationKinds.Move"/>. The example keeps its identity and
/// translations. The composer declines an example that anything outside it refers to, a move to the sense that already
/// owns it, and a neighbour that is not an example of the target sense. Without a neighbour the example goes last.
/// </summary>
public static class MoveExampleComposer
{
    private const string ConstructName = "MoveExample";
    private const string Rationale = "Authored by the MoveExample composer: the example belongs to the other sense.";

    /// <summary>Composes the move without mutating the caller's project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, MoveExampleIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var example = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Example, ConstructName) as ILexExampleSentence
            ?? throw new InvalidOperationException($"'{ConstructName}': 'example' must name an example sentence.");
        var to = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.ToSense, ConstructName) as ILexSense
            ?? throw new InvalidOperationException($"'{ConstructName}': 'toSense' must name a sense.");
        var from = (ILexSense)example.Owner;
        if (from.Guid == to.Guid)
            throw new InvalidOperationException($"'{ConstructName}': the example already belongs to that sense.");
        LexExampleMoveHandler.RequireMovable(example);
        var placement = ExamplePlacement.For(to, intent.After, intent.Before, ConstructName);
        var after = JsonSerializer.SerializeToElement(new
        {
            fromSense = CanonicalId.FromGuid(from.Guid).Value,
            toSense = CanonicalId.FromGuid(to.Guid).Value,
        });
        return [new(CanonicalId.Mint(), LexExampleOperationKinds.Move, target: LexicalFormAuthoring.Normalize(intent.Example),
            after: after, placement: placement, rationale: Rationale)];
    }
}

/// <summary>Chooses where a new or moved example sits among a sense's examples.</summary>
internal static class ExamplePlacement
{
    /// <summary>
    /// The placement the operation carries: next to the named example, or after the last one, or none when the sense
    /// has no examples yet.
    /// </summary>
    internal static Placement? For(ILexSense sense, CanonicalId? after, CanonicalId? before, string construct)
    {
        var examples = sense.ExamplesOS;
        foreach (var neighbour in new[] { after, before }.OfType<CanonicalId>())
            if (examples.All(item => item.Guid != neighbour.ToGuid()))
                throw new InvalidOperationException(
                    $"'{construct}': '{neighbour.Value}' is not an example of the target sense.");
        if (after is not null || before is not null)
        {
            // The operation names both neighbours, so it stays valid only while they are adjacent.
            var at = after is { } left ? examples.IndexOf(examples.First(item => item.Guid == left.ToGuid())) + 1
                : examples.IndexOf(examples.First(item => item.Guid == before!.Value.ToGuid()));
            return new Placement(at > 0 ? CanonicalId.FromGuid(examples[at - 1].Guid) : null,
                at < examples.Count ? CanonicalId.FromGuid(examples[at].Guid) : null);
        }
        return examples.Count == 0 ? null : new Placement(CanonicalId.FromGuid(examples[^1].Guid), null);
    }
}
