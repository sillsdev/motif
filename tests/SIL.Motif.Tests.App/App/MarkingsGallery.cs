using Avalonia.Automation;
using Avalonia.Controls;
using SIL.Motif.App.Controls;

namespace SIL.Motif.Tests.App;

internal sealed class MarkingsGallery : StackPanel
{
    public MarkingsGallery()
    {
        Spacing = 8;
        Children.Add(Heading("Opinion marks"));
        ApprovedMark = new OpinionMark(OpinionMarkKind.Approved);
        DisapprovedMark = new OpinionMark(OpinionMarkKind.Disapproved);
        UnknownMark = new OpinionMark(OpinionMarkKind.Unknown);
        EmptyMark = new OpinionMark(OpinionMarkKind.None);
        Children.Add(Row(ApprovedMark, DisapprovedMark, UnknownMark, EmptyMark));

        Children.Add(Heading("PanGloss readings"));
        SameLine = Line("same", "Same");
        DifferentLine = Line("different", "Different");
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
            Classes = { "revealControl", "revealButton", "stagedUndo" },
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
            Classes = { "revealControl", "revealLink" },
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

    public OpinionMark ApprovedMark { get; }
    public OpinionMark DisapprovedMark { get; }
    public OpinionMark UnknownMark { get; }
    public OpinionMark EmptyMark { get; }
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

    private static Border Line(string state, string content)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var divider = new Avalonia.Controls.Shapes.Rectangle { Classes = { "panGlossDivider" } };
        Grid.SetRow(divider, 0);
        grid.Children.Add(divider);
        var label = Label(content);
        if (state == "different") label.Classes.Add("f");
        Grid.SetRow(label, 1);
        grid.Children.Add(label);
        return new Border
        {
            Classes = { "panGlossLine", state },
            Child = grid,
        };
    }

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
