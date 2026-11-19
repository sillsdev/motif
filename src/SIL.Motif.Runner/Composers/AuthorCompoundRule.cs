using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names a compound rule to create: its kind, optional name, member categories, head and result category.</summary>
public sealed record AuthorCompoundRuleIntent(string Kind, string? Name, CanonicalId Left, CanonicalId Right, bool HeadLast, CanonicalId? Result);

/// <summary>Reads a closed author-compound-rule intent; an endocentric rule is right-headed unless head says left.</summary>
public static class AuthorCompoundRuleIntentParser
{
    public static readonly IReadOnlyList<string> Heads = ["left", "right"];

    public static AuthorCompoundRuleIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "AuthorCompoundRule", "kind", "name", "left", "right", "head", "result");
        var kind = SoundSystemIntentParsing.Text(input, "kind");
        if (kind is not (CompoundRulePayload.Endocentric or CompoundRulePayload.Exocentric))
            throw new ContractParseException("'AuthorCompoundRule': kind must be endocentric or exocentric.");
        var head = input.TryGetProperty("head", out _) ? SoundSystemIntentParsing.Text(input, "head") : null;
        if (head is not null && !Heads.Contains(head))
            throw new ContractParseException("'AuthorCompoundRule': head must be left or right.");
        if (kind == CompoundRulePayload.Exocentric && head is not null)
            throw new ContractParseException("'AuthorCompoundRule': an exocentric rule has no head; leave head out.");
        CanonicalId? result = input.TryGetProperty("result", out _) ? SoundSystemIntentParsing.Id(input, "result") : null;
        if (kind == CompoundRulePayload.Exocentric && result is null)
            throw new ContractParseException("'AuthorCompoundRule': an exocentric rule needs a result category, since neither member heads it.");
        var name = input.TryGetProperty("name", out _) ? SoundSystemIntentParsing.Text(input, "name") : null;
        return new(kind, name, SoundSystemIntentParsing.Id(input, "left"), SoundSystemIntentParsing.Id(input, "right"),
            head != "left", result);
    }
}

/// <summary>Stages the creation of an endocentric or exocentric compound rule, FieldWorks' compounding rule.</summary>
public static class AuthorCompoundRuleComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorCompoundRuleIntent intent, Func<CanonicalId>? mintId = null)
    {
        var payload = new CompoundRulePayload(intent.Kind, intent.Name, intent.Left, intent.Right, intent.HeadLast, intent.Result);
        var categories = CompoundRule.Check(cache, payload);
        var morphData = cache.LangProject.MorphologicalDataOA;
        var mint = mintId ?? (() => CanonicalId.Mint());
        var after = new Dictionary<string, object?>
        {
            ["kind"] = intent.Kind, ["left"] = intent.Left.Value, ["right"] = intent.Right.Value,
        };
        if (intent.Name is not null) after["name"] = intent.Name;
        if (intent.Kind == CompoundRulePayload.Endocentric) after["headLast"] = intent.HeadLast;
        if (intent.Result is { } result) after["result"] = result.Value;
        return [new(mint(), CompoundRuleCreateOperationKinds.Create, target: CanonicalId.FromGuid(morphData.Guid), entityId: mint(),
            after: JsonSerializer.SerializeToElement(after),
            rationale: "Compound rule: " + CompoundRule.Describe(payload, categories) + ".")];
    }
}
