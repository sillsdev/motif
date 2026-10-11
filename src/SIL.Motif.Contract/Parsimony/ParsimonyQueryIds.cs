using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SIL.Motif.Contract.Parsimony;

/// <summary>The versioned fixed-query ids a Measure may register. Wire spellings never change.</summary>
public static class ParsimonyQueryIds
{
    /// <summary>The wire spelling <c>adhoc-duplicate/v1</c>.</summary>
    public const string AdhocDuplicate = "adhoc-duplicate/v1";

    /// <summary>The wire spelling <c>adhoc-slot-order/v1</c>.</summary>
    public const string AdhocSlotOrder = "adhoc-slot-order/v1";

    /// <summary>The wire spelling <c>allo-alternation-family/v1</c>.</summary>
    public const string AlloAlternationFamily = "allo-alternation-family/v1";

    /// <summary>The wire spelling <c>allo-unconditioned/v1</c>.</summary>
    public const string AlloUnconditioned = "allo-unconditioned/v1";

    /// <summary>The wire spelling <c>allomorph-duplicate-form/v1</c>.</summary>
    public const string AllomorphDuplicateForm = "allomorph-duplicate-form/v1";

    /// <summary>The wire spelling <c>disapproved-produced/v1</c>.</summary>
    public const string DisapprovedProduced = "disapproved-produced/v1";

    /// <summary>The wire spelling <c>environment-excess/v1</c>.</summary>
    public const string EnvironmentExcess = "environment-excess/v1";

    /// <summary>The wire spelling <c>natural-class-excess/v1</c>.</summary>
    public const string NaturalClassExcess = "natural-class-excess/v1";

    /// <summary>The wire spelling <c>negative-accepted/v1</c>.</summary>
    public const string NegativeAccepted = "negative-accepted/v1";

    /// <summary>The wire spelling <c>null-optional/v1</c>.</summary>
    public const string NullOptional = "null-optional/v1";

    /// <summary>The wire spelling <c>slot-blocking/v1</c>.</summary>
    public const string SlotBlocking = "slot-blocking/v1";

    /// <summary>The wire spelling <c>statement-unused/v1</c>.</summary>
    public const string StatementUnused = "statement-unused/v1";

    /// <summary>The wire spelling <c>template-precedence/v1</c>.</summary>
    public const string TemplatePrecedence = "template-precedence/v1";

    /// <summary>The wire spelling <c>unslotted-affix/v1</c>.</summary>
    public const string UnslottedAffix = "unslotted-affix/v1";

    /// <summary>Every query id, for validating a catalog entry.</summary>
    public static IReadOnlySet<string> All { get; } = typeof(ParsimonyQueryIds)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(field => field.IsLiteral)
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);
}
