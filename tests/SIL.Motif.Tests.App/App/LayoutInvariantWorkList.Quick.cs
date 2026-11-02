namespace SIL.Motif.Tests.App;

internal static partial class LayoutInvariantWorkList
{
    /// <summary>
    /// The quick tier's known layout findings. Each entry is a follow-up owned by the named area; an entry that stops recurring fails the run, and
    /// a finding not listed here fails it too.
    /// </summary>
    internal static IReadOnlyList<LayoutException> QuickEntries { get; } =
    [
    ];

    /// <summary>The quick tier's findings when the words, texts and Review change are Arabic script.</summary>
    internal static IReadOnlyList<LayoutException> QuickRightToLeftEntries { get; } =
    [
    ];

    /// <summary>The quick tier's findings with one long Proposal name and one long Parsimony limitation.</summary>
    internal static IReadOnlyList<LayoutException> QuickLongEntries { get; } =
    [
    ];
}
