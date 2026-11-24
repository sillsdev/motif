namespace SIL.Motif.Contract.Query;

/// <summary>The tiers of <c>realizes(M, tier)</c>: where the variant set of a morpheme comes from.</summary>
public static class RealizesTiers
{
    /// <summary>The surface strings of the morpheme's morph in the stored analyses.</summary>
    public const string Attested = "attested";
    /// <summary>A superset from the rules that may change the morpheme; not yet available.</summary>
    public const string Possible = "possible";
    /// <summary>The forms PanGloss generates over chosen contexts; not yet available.</summary>
    public const string Generated = "generated";

    /// <summary>Every tier, in the order the schema lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Attested, Possible, Generated];
}

/// <summary>
/// A morpheme in any of its surface forms: <c>{realizes: {morpheme: "sfx:-an", tier: "attested"}}</c>. Running it resolves the
/// morpheme to a variant set, which compiles to a search over surface forms (see <see cref="RealizesPatternCompiler"/>).
/// </summary>
/// <param name="Morpheme">The morpheme as the person or agent wrote it: <c>entry:msa</c>, an entry guid or Motif id, or a form, optionally led by <c>root:</c>, <c>pfx:</c> or <c>sfx:</c>.</param>
/// <param name="Tier">One of <see cref="RealizesTiers.All"/>.</param>
public sealed record QueryRealizes(string Morpheme, string Tier) : QueryExpr;
