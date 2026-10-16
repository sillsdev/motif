namespace SIL.Motif.Commands;

/// <summary>
/// The shapes of the allomorph retirement documents. Every object here is closed and non-recursive, so a shape
/// describes the whole document down to its leaves.
/// </summary>
internal static class RetirementShapes
{
    /// <summary>The expected semantic identity of one allomorph; every field is present, stemName explicitly null.</summary>
    public static IntentShape AllomorphIdentity { get; } = IntentShape.Of(
        Field.Id("id", IdRole.Reference, required: true),
        Field.Id("entry", IdRole.Reference, required: true),
        Field.Choice("class", ["MoAffixAllomorph", "MoStemAllomorph"], required: true),
        Field.Choice("location", ["alternate", "lexeme"], required: true),
        Field.Choice("position", ["prefix", "suffix", "root", "stem"], required: true),
        Field.Text("semanticDigest", required: true),
        Field.Id("stemName", IdRole.Reference, required: true, nullable: true));

    /// <summary>One retirement component: the entry, its scope, the forms to retire and every reference to move.</summary>
    public static IntentShape Retirement { get; } = IntentShape.Of(
        Field.Id("entry", IdRole.Reference, required: true),
        Field.Choice("scope", ["affix", "stem"], required: true),
        Field.Nested("retiredForms", AllomorphIdentity, required: true, many: true),
        Field.Nested("roleReplacements", IntentShape.Of(
            Field.Id("retiredForm", IdRole.Reference, required: true),
            Field.Id("msa", IdRole.Reference, required: true),
            Field.Id("inflType", IdRole.Reference, required: true, nullable: true),
            Field.Choice("expansionRole", ["whole"], required: true),
            Field.Nested("replacement", AllomorphIdentity, required: true)), required: true, many: true),
        Field.Nested("bundles", IntentShape.Of(
            Field.Id("bundle", IdRole.Reference, required: true),
            Field.Id("analysis", IdRole.Reference, required: true),
            Field.Id("wordform", IdRole.Reference, required: true),
            Field.Id("retiredForm", IdRole.Reference, required: true),
            Field.Id("msa", IdRole.Reference, required: true),
            Field.Id("inflType", IdRole.Reference, required: true, nullable: true),
            Field.Choice("expansionRole", ["whole"], required: true)), required: true, many: true),
        Field.Nested("adhocReplacements", IntentShape.Of(
            Field.Id("rule", IdRole.Reference, required: true),
            Field.Choice("field", ["FirstAllomorph", "RestOfAllos", "Allomorphs"], required: true),
            Field.Number("ordinal", required: true, nullable: true),
            Field.Id("retiredForm", IdRole.Reference, required: true),
            Field.Nested("replacement", AllomorphIdentity, required: true)), required: true, many: true));

    /// <summary>The sound class a rule replacement creates or reuses.</summary>
    public static IntentShape NaturalClass { get; } = IntentShape.Of(
        Field.Choice("mode", ["create", "reuse"], required: true),
        Field.Id("id", IdRole.Opaque, required: true),
        Field.Text("name", required: true),
        Field.Text("abbreviation", required: true),
        Field.Ids("members", IdRole.Reference, required: true),
        Field.Text("semanticDigest", required: true, nullable: true));

    private static IntentShape ContextAtom { get; } = IntentShape.Of(
        Field.Choice("kind", ["segment", "natural-class", "boundary"], required: true),
        Field.Id("id", IdRole.Reference, required: true));

    /// <summary>The rewrite rule a rule replacement authors.</summary>
    public static IntentShape Rule { get; } = IntentShape.Of(
        Field.Id("id", IdRole.Opaque, required: true),
        Field.Text("name", required: true),
        Field.Ids("input", IdRole.Reference, required: true),
        Field.Ids("output", IdRole.Reference, required: true),
        Field.Nested("left", ContextAtom, required: true, many: true),
        Field.Nested("right", ContextAtom, required: true, many: true),
        Field.Nested("placement", IntentShape.Of(
            Field.Choice("kind", ["first", "last", "before", "after"], required: true),
            Field.Id("anchor", IdRole.Reference, required: true, nullable: true)), required: true),
        Field.Flag("enabled", required: true));

    /// <summary>A binding of one concrete operation id to its closed slot, with its declared dependencies.</summary>
    public static IntentShape OperationBinding { get; } = IntentShape.Of(
        Field.Id("operationId", IdRole.Opaque, required: true),
        Field.Text("slot", required: true),
        Field.Id("target", IdRole.Reference, required: true),
        Field.Id("member", IdRole.Reference, required: true, nullable: true),
        Field.Ids("dependsOn", IdRole.Opaque, required: true));

    /// <summary>Review copy that is not part of the retirement's digest.</summary>
    public static IntentShape Display { get; } = IntentShape.Of(
        Field.Text("title", required: true),
        Field.Text("description", required: true));
}
