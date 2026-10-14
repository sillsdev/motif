using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Operations;

/// <summary>An addressed field value, entire field, or deleted entity. A null field denotes entity deletion.</summary>
public readonly record struct OperationSlot(
    CanonicalId Target, string? Field, string? Discriminator = null, bool WholeField = false);

/// <summary>Closed slot policies for every operation kind, registered with its handler.</summary>
public static class OperationSlotRegistry
{
    private sealed record Policy(Func<OperationEnvelope, IReadOnlyList<OperationSlot>> Select, string? Exemption);
    private static readonly Dictionary<string, Policy> Policies = new(StringComparer.Ordinal);

    public static IReadOnlyCollection<string> RegisteredKinds => Policies.Keys;

    public static void Register(string kind, Func<OperationEnvelope, OperationSlot?> selector) =>
        RegisterMany(kind, operation => selector(operation) is { } slot ? [slot] : []);

    public static void RegisterMany(string kind, Func<OperationEnvelope, IReadOnlyList<OperationSlot>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Add(kind, new Policy(selector, null));
    }

    /// <summary>Records why a kind addresses no slot; exemptions remain explicit coverage decisions.</summary>
    public static void RegisterExempt(string kind, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Add(kind, new Policy(_ => [], reason));
    }

    public static IReadOnlyList<OperationSlot> Resolve(OperationEnvelope operation)
    {
        if (!Policies.TryGetValue(operation.Kind, out var policy))
            throw new ContractParseException($"Operation kind '{operation.Kind}' has no registered slot policy.");
        return policy.Select(operation);
    }

    public static string? ExemptionReason(string kind) => Policies.TryGetValue(kind, out var policy)
        ? policy.Exemption : null;

    public static void RegisterScalar(string kind, string field) =>
        Register(kind, operation => new OperationSlot(Target(operation), field));

    public static void RegisterAlternative(string kind, string field) =>
        Register(kind, operation => new OperationSlot(Target(operation), field,
            ClosedPayloadParsing.GetRequiredString(After(operation), "ws", kind)));

    public static void RegisterMember(string kind, string field) =>
        Register(kind, operation => new OperationSlot(Target(operation), field,
            ClosedPayloadParsing.GetRequiredCanonicalId(After(operation), "member", kind).Value));

    public static void RegisterOwningCreate(string kind, string field, bool atomic) =>
        RegisterMany(kind, operation =>
        {
            var member = operation.EntityId ?? throw new ContractParseException($"'{kind}' requires 'entityId'.");
            return [new OperationSlot(Target(operation), field, atomic ? null : member.Value),
                new OperationSlot(member, "$identity")];
        });

    public static void RegisterEntityDeletion(string kind) =>
        Register(kind, operation => new OperationSlot(Target(operation), null));

    public static CanonicalId Target(OperationEnvelope operation) => operation.Target ??
        throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{operation.Kind}' requires 'target'.");

    public static JsonElement After(OperationEnvelope operation) => operation.After ??
        throw new ContractParseException($"'{operation.Kind}' requires 'after'.");

    private static void Add(string kind, Policy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (!Policies.TryAdd(kind, policy))
            throw new InvalidOperationException($"A slot policy is already registered for kind '{kind}'.");
    }
}
