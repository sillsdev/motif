using Avalonia.Automation;
using Avalonia.Controls;

namespace SIL.Motif.Tests.App;

internal sealed class MarkingsGallery : StackPanel
{
    public MarkingsGallery()
    {
        Spacing = 8;
        Children.Add(Heading("Opinion marks"));
        ApprovedMark = Mark("approved", "A");
        DisapprovedMark = Mark("disapproved", "D");
        UnknownMark = Mark("unknown", "U");
        EmptyMark = Mark("none", null);
        Children.Add(Row(ApprovedMark, DisapprovedMark, UnknownMark, EmptyMark));

        Children.Add(Heading("PanGloss readings"));
        SameLine = Line("same", "Same");
        DifferentLine = Line("different", "Different reading");
        ExtraCount = new Border { Classes = { "panGlossExtra" }, Child = Label("+2") };
        NoneLine = Line("none", "No reading");
        CappedLine = Line("capped", "Search capped");
        Children.Add(Row(SameLine, DifferentLine, ExtraCount, NoneLine, CappedLine));

        Children.Add(Heading("Actions and staged work"));
        PrimaryAction = new Button
        {
            Content = "✓",
            Classes = { "actionChip", "primary" },
        };
        AutomationProperties.SetName(PrimaryAction, "Approve analysis");
        FixAction = new Button
        {
            Content = "Fix ▾",
            Classes = { "actionChip", "fix" },
        };
        StagedUndo = new Button
        {
            Content = "Undo",
            Classes = { "revealControl", "stagedUndo" },
        };
        AutomationProperties.SetName(StagedUndo, "Undo staged approval");
        StagedStrip = new Border
        {
            Classes = { "stagedStrip" },
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Staged", Classes = { "stagedLabel" } },
                    Label("Will approve"),
                    StagedUndo,
                },
            },
        };
        Children.Add(Row(PrimaryAction, FixAction, StagedStrip));

        Children.Add(Heading("Hover or focus to reveal"));
        FieldWorksLink = new Button
        {
            Content = "FW ↗",
            Classes = { "revealControl" },
        };
        AutomationProperties.SetName(FieldWorksLink, "Open in FieldWorks");
        HoverRegion = new Border
        {
            Classes = { "hoverReveal" },
            Child = FieldWorksLink,
        };
        Children.Add(HoverRegion);

        Children.Add(Heading("Density"));
        CompactText = DensitySample("Compact default");
        NormalText = DensitySample("Normal");
        CompactRoot = DensityRoot(CompactText, normal: false);
        NormalRoot = DensityRoot(NormalText, normal: true);
        Children.Add(Row(CompactRoot, NormalRoot));
    }

    public Border ApprovedMark { get; }
    public Border DisapprovedMark { get; }
    public Border UnknownMark { get; }
    public Border EmptyMark { get; }
    public Border SameLine { get; }
    public Border DifferentLine { get; }
    public Border ExtraCount { get; }
    public Border NoneLine { get; }
    public Border CappedLine { get; }
    public Button PrimaryAction { get; }
    public Button FixAction { get; }
    public Border StagedStrip { get; }
    public Button StagedUndo { get; }
    public Border HoverRegion { get; }
    public Button FieldWorksLink { get; }
    public TextBlock CompactText { get; }
    public TextBlock NormalText { get; }
    public StackPanel CompactRoot { get; }
    public StackPanel NormalRoot { get; }

    private static Border Mark(string state, string? content)
    {
        var mark = new Border { Classes = { "opinionMark", state } };
        if (state == "none")
            mark.Child = new Grid { Children = { new Avalonia.Controls.Shapes.Rectangle { Classes = { "opinionDash" } } } };
        else
            mark.Child = Label(content!);
        return mark;
    }

    private static Border Line(string state, string content) => new()
    {
        Classes = { "panGlossLine", state },
        Child = Label(content),
    };

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    private static TextBlock DensitySample(string text)
    {
        var sample = Label(text);
        sample.Classes.Add("densitySample");
        return sample;
    }

    private static StackPanel DensityRoot(TextBlock sample, bool normal)
    {
        var root = new StackPanel { Classes = { "analysisDensity" } };
        if (normal) root.Classes.Add("normal");
        root.Children.Add(sample);
        return root;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = Avalonia.Media.FontWeight.Bold,
    };

    private static TextBlock Label(string text) => new() { Text = text };
}
