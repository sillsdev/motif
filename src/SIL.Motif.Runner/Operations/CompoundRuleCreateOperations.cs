using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Execution;

namespace SIL.Motif.Runner.Operations;

/// <summary>Names the creation of an endocentric or exocentric compound rule owned by the project's morphology data.</summary>
public static class CompoundRuleCreateOperationKinds
{
    public const string Create = "grammar/moMorphData/createCompoundRule";

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Create);
        OperationHandlerRegistry.Register(Create, CompoundRuleCreateHandler.Instance);
        OperationSlotRegistry.RegisterOwningCreate(Create, SnapshotFields.MoMorphDataCompoundRules, false);
    }
#pragma warning restore CA2255
}

/// <summary>
/// The closed <c>after</c> payload: the rule kind, an optional name, the left and right member categories, which
/// member heads an endocentric rule, and the result category (required for an exocentric rule, an optional override
/// of the head's category for an endocentric one).
/// </summary>
public sealed record CompoundRulePayload(string Kind, string? Name, CanonicalId Left, CanonicalId Right, bool HeadLast, CanonicalId? Result)
{
    public const string Endocentric = "endocentric";
    public const string Exocentric = "exocentric";

    public static CompoundRulePayload Parse(JsonElement after, string kind)
    {
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["kind", "name", "left", "right", "headLast", "result"], kind);
        var ruleKind = ClosedPayloadParsing.GetRequiredString(after, "kind", kind);
        if (ruleKind is not (Endocentric or Exocentric))
            throw new InvalidOperationException($"'{kind}': kind must be endocentric or exocentric.");
        string? name = after.TryGetProperty("name", out _) ? ClosedPayloadParsing.GetRequiredString(after, "name", kind) : null;
        var headLast = ruleKind == Endocentric
            ? ClosedPayloadParsing.GetRequiredBoolean(after, "headLast", kind)
            : after.TryGetProperty("headLast", out _)
                ? throw new InvalidOperationException($"'{kind}': an exocentric rule has no head; headLast applies only to an endocentric rule.")
                : true;
        var result = after.TryGetProperty("result", out _) ? ClosedPayloadParsing.GetRequiredCanonicalId(after, "result", kind) : (CanonicalId?)null;
        if (ruleKind == Exocentric && result is null)
            throw new InvalidOperationException($"'{kind}': an exocentric rule needs a result category, since neither member heads it.");
        return new(ruleKind, name, ClosedPayloadParsing.GetRequiredCanonicalId(after, "left", kind),
            ClosedPayloadParsing.GetRequiredCanonicalId(after, "right", kind), headLast, result);
    }
}

/// <summary>Checks, describes and builds a compound rule the way FieldWorks' two binary compound classes hold it.</summary>
public static class CompoundRule
{
    /// <summary>The categories a payload names, resolved against the project.</summary>
    public readonly record struct Categories(IPartOfSpeech Left, IPartOfSpeech Right, IPartOfSpeech? Result);

    /// <summary>Resolves the named categories, refusing an unknown one and a rule the project already holds.</summary>
    public static Categories Check(LcmCache cache, CompoundRulePayload payload)
    {
        var morphData = cache.LangProject.MorphologicalDataOA ?? throw new CompoundRuleRefusal(
            RefusalCodes.CompoundRuleNoMorphologyData, "The project has no morphology data to own a compound rule.");
        var categories = new Categories(Resolve(cache, payload.Left, "left"), Resolve(cache, payload.Right, "right"),
            payload.Result is { } result ? Resolve(cache, result, "result") : null);
        if (morphData.CompoundRulesOS.Any(existing => Same(existing, payload, categories)))
            throw new CompoundRuleRefusal(RefusalCodes.CompoundRuleDuplicate,
                $"A compound rule already exists: {Describe(payload, categories)}.");
        return categories;
    }

    /// <summary>Appends the rule, enabled, with its own member and result analyses, inside the caller's unit of work.</summary>
    public static void Create(LcmCache cache, IMoMorphData owner, Guid guid, CompoundRulePayload payload, Categories categories)
    {
        var stems = cache.ServiceLocator.GetInstance<IMoStemMsaFactory>();
        IMoBinaryCompoundRule rule;
        if (payload.Kind == CompoundRulePayload.Endocentric)
        {
            var endo = cache.ServiceLocator.GetInstance<IMoEndoCompoundFactory>().Create(guid);
            owner.CompoundRulesOS.Add(endo);
            endo.HeadLast = payload.HeadLast;
            // FieldWorks' parser loader dereferences the override MSA, so every endocentric rule owns one.
            endo.OverridingMsaOA = stems.Create();
            endo.OverridingMsaOA.PartOfSpeechRA = categories.Result;
            rule = endo;
        }
        else
        {
            var exo = cache.ServiceLocator.GetInstance<IMoExoCompoundFactory>().Create(guid);
            owner.CompoundRulesOS.Add(exo);
            exo.ToMsaOA = stems.Create();
            exo.ToMsaOA.PartOfSpeechRA = categories.Result;
            rule = exo;
        }
        rule.LeftMsaOA = stems.Create();
        rule.LeftMsaOA.PartOfSpeechRA = categories.Left;
        rule.RightMsaOA = stems.Create();
        rule.RightMsaOA.PartOfSpeechRA = categories.Right;
        rule.Disabled = false;
        if (!string.IsNullOrWhiteSpace(payload.Name))
            rule.Name.set_String(cache.DefaultAnalWs, payload.Name);
    }

    /// <summary>The plain statement of what the rule joins and what it produces, for example
    /// "noun + noun makes a noun; the right member is the head".</summary>
    public static string Describe(CompoundRulePayload payload, Categories categories)
    {
        var left = Label(categories.Left);
        var right = Label(categories.Right);
        if (payload.Kind == CompoundRulePayload.Exocentric)
            return $"exocentric: {left} + {right} makes a {Label(categories.Result!)}; neither member is the head";
        var head = payload.HeadLast ? categories.Right : categories.Left;
        var result = categories.Result is { } overriding
            ? $"a {Label(overriding)} (overriding the head's category)"
            : $"a {Label(head)} (the head's category)";
        return $"endocentric: {left} + {right} makes {result}; the {(payload.HeadLast ? "right" : "left")} member is the head";
    }

    /// <summary>
    /// Sentences saying the rule cannot yet match anything, because no entry holds a stem analysis in a member's
    /// category (or one of its subcategories), or an empty list. Read at Dry Run, so earlier operations of the same
    /// Proposal count.
    /// </summary>
    public static IReadOnlyList<string> Warnings(LcmCache cache, Categories categories)
    {
        var warnings = new List<string>();
        foreach (var (side, category) in new[] { ("left", categories.Left), ("right", categories.Right) })
            if (!HasStem(cache, category))
                warnings.Add($"No entry has a stem analysis in the {side} category '{Label(category)}', so this rule has " +
                    $"nothing to join on the {side}; the parser cannot use it until one exists.");
        return warnings;
    }

    private static bool HasStem(LcmCache cache, IPartOfSpeech category)
    {
        var wanted = new HashSet<Guid> { category.Guid };
        foreach (var sub in category.ReallyReallyAllPossibilities) wanted.Add(sub.Guid);
        return cache.ServiceLocator.GetInstance<IMoStemMsaRepository>().AllInstances()
            .Any(msa => msa.Owner is ILexEntry && msa.PartOfSpeechRA is { } pos && wanted.Contains(pos.Guid));
    }

    private static IPartOfSpeech Resolve(LcmCache cache, CanonicalId id, string role)
    {
        try
        {
            return ReferenceFieldLowering.Resolve<IPartOfSpeech>(cache, id, CompoundRuleCreateOperationKinds.Create);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw new CompoundRuleRefusal(RefusalCodes.CompoundRuleInvalidCategory,
                $"The {role} category '{id.Value}' is not a category (part of speech) in the project.");
        }
    }

    private static bool Same(IMoCompoundRule existing, CompoundRulePayload payload, Categories categories)
    {
        if (existing is not IMoBinaryCompoundRule binary) return false;
        if (binary.LeftMsaOA?.PartOfSpeechRA?.Guid != categories.Left.Guid ||
            binary.RightMsaOA?.PartOfSpeechRA?.Guid != categories.Right.Guid) return false;
        switch (existing)
        {
            case IMoEndoCompound endo when payload.Kind == CompoundRulePayload.Endocentric:
            {
                // Compare the resulting category: an override equal to the head is no override.
                var head = payload.HeadLast ? categories.Right : categories.Left;
                var existingHead = endo.HeadLast ? binary.RightMsaOA?.PartOfSpeechRA : binary.LeftMsaOA?.PartOfSpeechRA;
                var existingResult = endo.OverridingMsaOA?.PartOfSpeechRA ?? existingHead;
                return endo.HeadLast == payload.HeadLast && existingResult?.Guid == (categories.Result ?? head).Guid;
            }
            case IMoExoCompound exo when payload.Kind == CompoundRulePayload.Exocentric:
                return exo.ToMsaOA?.PartOfSpeechRA?.Guid == categories.Result?.Guid;
            default:
                return false;
        }
    }

    private static string Label(IPartOfSpeech category) =>
        category.Name.BestAnalysisAlternative?.Text is { Length: > 0 } text && text != "***"
            ? text : CanonicalId.FromGuid(category.Guid).Value;
}

internal sealed class CompoundRuleRefusal : ProposalRefusalException
{
    internal CompoundRuleRefusal(string code, string message)
        : base(code, FailureReason.Refused, message, new Dictionary<string, string>()) { }
}

internal sealed class CompoundRuleCreateHandler : IOperationHandler
{
    internal static readonly CompoundRuleCreateHandler Instance = new();
    private const string Kind = CompoundRuleCreateOperationKinds.Create;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{Kind}': storage GUID overrides are not supported for this create.");
        if (operation.Placement is not null)
            throw new InvalidOperationException($"'{Kind}': a compound rule is appended after the existing rules; placement is not defined.");
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var payload = CompoundRulePayload.Parse(OperationSlotRegistry.After(operation), Kind);
        var (id, data) = TargetResolution.Resolve<IMoMorphData>(cache, operation, Kind);
        CreateIdentity.RequireFresh(cache, entityId, Kind,
            payload.Kind == CompoundRulePayload.Endocentric ? "MoEndoCompound" : "MoExoCompound");
        var categories = CompoundRule.Check(cache, payload);
        var before = Read(data, entityId);
        touchedTargets.Add(id);
        CompoundRule.Create(cache, data, entityId.ToGuid(), payload, categories);
        return new ExpectedEffect(id, SnapshotFields.MoMorphDataCompoundRules, before, Read(data, entityId),
            Preview(payload, categories, CompoundRule.Warnings(cache, categories)));
    }

    /// <summary>Reads only whether the rule's identity exists: the named categories may be created by earlier
    /// operations of the same Proposal, so they cannot be resolved before it runs.</summary>
    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        CompoundRulePayload.Parse(OperationSlotRegistry.After(operation), Kind);
        var (id, data) = TargetResolution.Resolve<IMoMorphData>(cache, operation, Kind);
        var current = Read(data, entityId);
        if (!data.CompoundRulesOS.Any(item => item.Guid == entityId.ToGuid()))
            CreateIdentity.AddCollision(cache, entityId, null, current);
        return new ExpectedEffect(id, SnapshotFields.MoMorphDataCompoundRules, current, current);
    }

    private static JsonElement Preview(CompoundRulePayload payload, CompoundRule.Categories categories, IReadOnlyList<string> warnings) =>
        JsonSerializer.SerializeToElement(new
        {
            compounds = CompoundRule.Describe(payload, categories),
            warnings = warnings.Count == 0 ? null : warnings,
            kind = payload.Kind,
            headLast = payload.Kind == CompoundRulePayload.Endocentric ? payload.HeadLast : (bool?)null,
            left = payload.Left.Value,
            right = payload.Right.Value,
            result = payload.Result?.Value,
        }, OmitNull);

    private static readonly JsonSerializerOptions OmitNull = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private static Dictionary<string, string> Read(IMoMorphData data, CanonicalId entityId)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var rules = data.CompoundRulesOS;
        for (var i = 0; i < rules.Count; i++)
            result[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = CanonicalId.FromGuid(rules[i].Guid).Value;
        return result;
    }
}
