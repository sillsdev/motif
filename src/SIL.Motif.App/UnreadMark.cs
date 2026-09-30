using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;

namespace SIL.Motif.App.Controls;

/// <summary>A visible and accessible label for an occurrence with no current Read marker.</summary>
public sealed class UnreadMark : Border
{
    /// <summary>Creates a mark whose text and accessible name identify the occurrence as Unread.</summary>
    public UnreadMark()
    {
        Classes.Add("unreadMark");
        AutomationProperties.SetName(this, "Unread");
        Child = new StackPanel
        {
            Classes = { "unreadMarkContent" },
            Children =
            {
                new Ellipse { Classes = { "unreadMarkDot" } },
                new TextBlock { Text = "Unread" },
            },
        };
    }
}
