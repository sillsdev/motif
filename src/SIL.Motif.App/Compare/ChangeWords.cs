using System.Text;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;

namespace SIL.Motif.App.Compare;

/// <summary>
/// The words the viewer puts on screen and gives a screen reader. "Conflict", "Keep", "Accept" and "Reject" are never
/// among them: a change is a choice between two versions, and those words belong to other parts of Motif.
/// </summary>
public static class ChangeWords
{
    public const string UseMine = "Use mine";
    public const string UseFieldWorks = "Use FieldWorks";
    public const string UseBoth = "Use both";
    public const string BothChanged = "Both changed this";
    public const string NoLongerFits = "no longer fits";
    public const string OpenSideBySide = "Open side by side…";
    public const string EditSideBySide = "Edit it in the side-by-side view…";
    public const string BackToCombined = "Back to combined view";
    public const string UseThis = "Use this →";
    public const string Done = "Done";
    public const string DecideLater = "Decide later";
    public const string Compare = "Compare…";
    public const string StartAgain = "Start again from the FieldWorks text";
    public const string ShowResult = "Show result";
    public const string UseMineForAll = "Use mine for all";
    public const string UseFieldWorksForAll = "Use FieldWorks for all";
    public const string MineTag = "You";
    public const string FieldWorksTag = "FW";
    public const string RewrittenInBoth = "Rewritten in both places";

    /// <summary>The words of <paramref name="tokens"/>, as a person reads them (NFC).</summary>
    public static string Text(IEnumerable<Token> tokens) => string.Concat(tokens.Select(token => token.Text)).Normalize(NormalizationForm.FormC);

    /// <summary>What <paramref name="change"/> is, in a sentence without a full stop.</summary>
    public static string Describe(TextChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var who = change.Author switch
        {
            ChangeAuthor.Mine => "You",
            ChangeAuthor.FieldWorks => "FieldWorks",
            _ => BothChanged,
        };
        if (change.Author == ChangeAuthor.Both)
            return change.Kind switch
            {
                ChangeKind.Rewritten => RewrittenInBoth,
                ChangeKind.ParagraphRemoved => change.Mine.Count == 0
                    ? "Removed by you, changed in FieldWorks"
                    : "Changed by you, removed in FieldWorks",
                ChangeKind.ParagraphFormatting => $"{BothChanged}: the paragraph's style",
                _ => $"{BothChanged}. Yours ‘{Text(change.Mine)}’, FieldWorks ‘{Text(change.FieldWorks)}’",
            };
        var mine = change.Author == ChangeAuthor.Mine;
        var after = mine ? change.Mine : change.FieldWorks;
        var before = change.Starting;
        return change.Kind switch
        {
            ChangeKind.ParagraphAdded => mine ? "Paragraph added by you" : "Paragraph added in FieldWorks",
            ChangeKind.ParagraphRemoved => mine ? "Paragraph removed by you" : "Paragraph removed in FieldWorks",
            ChangeKind.ParagraphFormatting => $"Formatting only: paragraph style changed {(mine ? "by you" : "in FieldWorks")}",
            ChangeKind.WritingSystem => $"Writing system changed: ‘{Text(after)}’ from {WritingSystems(before)} to {WritingSystems(after)}" +
                (mine ? " by you" : " in FieldWorks"),
            ChangeKind.Formatting => $"Formatting only: ‘{Text(after)}’ {Formatting(before, after)} {(mine ? "by you" : "in FieldWorks")}",
            _ when before.Count == 0 => $"{who} added ‘{Text(after)}’",
            _ when after.Count == 0 => $"{who} removed ‘{Text(before)}’",
            _ => $"{who} changed ‘{Text(before)}’ to ‘{Text(after)}’",
        };
    }

    /// <summary>
    /// What a screen reader says on a change: what it is, where, and which version is in use, then the key that switches it.
    /// </summary>
    public static string Announce(TextChange change, Choice? choice)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Author == ChangeAuthor.Both)
            return $"{Describe(change)}, {change.Location}. " + choice switch
            {
                Choice.Mine => "Yours in use.",
                Choice.FieldWorks => "FieldWorks in use.",
                Choice.Both => "Both in use.",
                _ => "Needs a choice.",
            };
        var inUse = choice == change.Initial;
        var other = change.Author == ChangeAuthor.Mine ? "Alt+F for FieldWorks." : "Alt+M for yours.";
        return $"{Describe(change)}, in {change.Location}. {(inUse ? "In use." : "Not in use.")} {(inUse ? other : change.Author == ChangeAuthor.Mine ? "Alt+M to use it." : "Alt+F to use it.")}";
    }

    private static string WritingSystems(IReadOnlyList<Token> tokens) =>
        string.Join(", ", tokens.SelectMany(token => token.Pieces).Select(piece => piece.Ws).Distinct());

    // Names the few properties a person sets by hand; anything else is "reformatted".
    private static string Formatting(IReadOnlyList<Token> before, IReadOnlyList<Token> after)
    {
        var from = before.SelectMany(token => token.Pieces).FirstOrDefault();
        var to = after.SelectMany(token => token.Pieces).FirstOrDefault();
        if (from is null || to is null) return "reformatted";
        var said = new List<string>();
        if (from.CharacterStyle != to.CharacterStyle)
            said.Add($"style {from.CharacterStyle ?? "none"} → {to.CharacterStyle ?? "none"}");
        foreach (var (number, name) in new[] { (TextPropertyNumbers.Bold, "bold"), (TextPropertyNumbers.Italic, "italic") })
        {
            var (was, now) = (On(from.Props, number), On(to.Props, number));
            if (was != now) said.Add(now ? $"made {name}" : $"no longer {name}");
        }
        return said.Count == 0 ? "reformatted" : string.Join(", ", said);
    }

    private static bool On(TextProps? props, int number) => props?.Ints.TryGetValue(number, out var value) == true && value.Value != 0;
}
