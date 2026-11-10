using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;

namespace SIL.Motif.App.Preview;

/// <summary>
/// What the preview windows share about a field shown as FieldWorks Markdown (SIL.TextEditing ADR 0035): the toggle on
/// the selection bar, the spoken change, and returning fields to rich text before the window changes or applies them.
/// </summary>
internal static class PreviewSourceMode
{
    /// <summary>What the toggle is called, on the bar and to a screen reader.</summary>
    public const string ToggleName = "Markdown";

    /// <summary>What the window says when a field cannot go back to rich text before Compare or Apply.</summary>
    public const string FixFirstMessage =
        "Your Markdown has an error. Fix what is marked, or throw away your Markdown changes, first.";

    /// <summary>A bar button that switches the field <paramref name="toggle"/> acts on; it never takes focus from the field.</summary>
    public static Button Toggle(Func<bool> toggle)
    {
        var button = new Button { Content = ToggleName, Focusable = false, Margin = new Avalonia.Thickness(0, 0, 6, 0) };
        AutomationProperties.SetName(button, "Edit as Markdown");
        button.Click += (_, _) => toggle();
        return button;
    }

    /// <summary>Makes <paramref name="status"/> a polite live region, so what is written to it is spoken.</summary>
    public static void Speak(TextBlock status) => AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);

    /// <summary>
    /// The context menu for the Markdown in source view <paramref name="view"/> at <paramref name="info"/>: the fix of each
    /// finding there, then Cut, Copy and Paste, all acting on the Markdown.
    /// </summary>
    public static IReadOnlyList<MenuItem> Menu(RichTextField view, ContextInfo info)
    {
        var items = new List<MenuItem>();
        foreach (var finding in info.Diagnostics.Where(finding => finding.Fix is not null))
        {
            var fix = new MenuItem { Header = finding.Fix!.Title };
            fix.Click += (_, _) => view.ApplyDiagnosticFix(finding);
            items.Add(fix);
        }
        items.Add(Item("Cut", () => view.CopyAsync(cut: true)));
        items.Add(Item("Copy", () => view.CopyAsync(cut: false)));
        items.Add(Item("Paste", view.PasteAsync));
        return items;
    }

    private static MenuItem Item(string header, Func<Task> act)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await act();
        return item;
    }

    /// <summary>
    /// Returns every field of <paramref name="fields"/> to rich text; false, with <see cref="FixFirstMessage"/> in
    /// <paramref name="status"/>, when one holds Markdown with an error. A clean return stages as any edit does.
    /// </summary>
    public static bool ReturnToRichText(IEnumerable<RichTextField> fields, TextBlock status)
    {
        foreach (var field in fields)
        {
            if (field.LeaveSourceMode()) continue;
            status.Text = FixFirstMessage;
            return false;
        }
        return true;
    }
}
