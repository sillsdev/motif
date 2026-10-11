using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SIL.Motif.Contract.Parsimony;

/// <summary>The closed codes of the fixed Parsimony views. Wire spellings never change.</summary>
public static class ParsimonyViews
{
    /// <summary>The wire spelling <c>adhoc-context</c>.</summary>
    public const string AdhocContext = "adhoc-context";

    /// <summary>The wire spelling <c>affix-context</c>.</summary>
    public const string AffixContext = "affix-context";

    /// <summary>The wire spelling <c>allomorph-context</c>.</summary>
    public const string AllomorphContext = "allomorph-context";

    /// <summary>The wire spelling <c>alternation-families</c>.</summary>
    public const string AlternationFamilies = "alternation-families";

    /// <summary>The wire spelling <c>approved-morph-sequences</c>.</summary>
    public const string ApprovedMorphSequences = "approved-morph-sequences";

    /// <summary>The wire spelling <c>environment-excess</c>.</summary>
    public const string EnvironmentExcess = "environment-excess";

    /// <summary>The wire spelling <c>join-quality</c>.</summary>
    public const string JoinQuality = "join-quality";

    /// <summary>The wire spelling <c>natural-class-context</c>.</summary>
    public const string NaturalClassContext = "natural-class-context";

    /// <summary>The wire spelling <c>natural-class-excess</c>.</summary>
    public const string NaturalClassExcess = "natural-class-excess";

    /// <summary>The wire spelling <c>null-optional</c>.</summary>
    public const string NullOptional = "null-optional";

    /// <summary>The wire spelling <c>parser-cases</c>.</summary>
    public const string ParserCases = "parser-cases";

    /// <summary>The wire spelling <c>parsimony-active-findings</c>.</summary>
    public const string ParsimonyActiveFindings = "parsimony-active-findings";

    /// <summary>The wire spelling <c>parsimony-suppressed</c>.</summary>
    public const string ParsimonySuppressed = "parsimony-suppressed";

    /// <summary>The wire spelling <c>parsimony-suppression-history</c>.</summary>
    public const string ParsimonySuppressionHistory = "parsimony-suppression-history";

    /// <summary>The wire spelling <c>slot-context</c>.</summary>
    public const string SlotContext = "slot-context";

    /// <summary>The wire spelling <c>statement-usage</c>.</summary>
    public const string StatementUsage = "statement-usage";

    /// <summary>The wire spelling <c>template-order</c>.</summary>
    public const string TemplateOrder = "template-order";

    /// <summary>The wire spelling <c>unslotted-affixes</c>.</summary>
    public const string UnslottedAffixes = "unslotted-affixes";

    /// <summary>Every fixed view code.</summary>
    public static IReadOnlySet<string> All { get; } = typeof(ParsimonyViews)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(field => field.IsLiteral)
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);
}
