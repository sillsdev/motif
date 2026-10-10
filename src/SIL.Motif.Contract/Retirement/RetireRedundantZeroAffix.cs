namespace SIL.Motif.Contract.Retirement;

/// <summary>Names one affix entry whose complete graph may be retired when it is redundant and unused.</summary>
public sealed record RetireRedundantZeroAffixIntent(string Entry);

/// <summary>A closed request to retire one redundant zero-affix entry.</summary>
public sealed record RetireRedundantZeroAffixIntentDocument(
    RetireRedundantZeroAffixIntent Retirement)
{
    public string Format { get; init; } = "motif-retire-redundant-zero-affix";
    public int Version { get; init; } = 1;
}
