using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SIL.Motif.App.Controls;

/// <summary>A native text block that reports after its text layout has been drawn.</summary>
public class WritingSystemTextBlock : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        base.RenderTextLayout(context, origin);
        WritingSystemText.ReportTextPresented(this, WritingSystemText.CapturePresentation(this));
    }
}

/// <summary>A selectable native text block that reports after its text layout has been drawn.</summary>
public class WritingSystemSelectableTextBlock : SelectableTextBlock
{
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        base.RenderTextLayout(context, origin);
        WritingSystemText.ReportTextPresented(this, WritingSystemText.CapturePresentation(this));
    }
}

/// <summary>A native text input that reports after its rendered visual tree is complete.</summary>
public class WritingSystemTextBox : TextBox
{
    internal int RenderPassCount { get; private set; }
    internal WritingSystemText.PresentationTicket? LastPresentationTicket { get; private set; }
    internal Action<Action>? PresentationReportScheduler { get; set; }

    protected override Type StyleKeyOverride => typeof(TextBox);

    public override void Render(DrawingContext context)
    {
        RenderPassCount++;
        base.Render(context);
        var presentation = WritingSystemText.CapturePresentation(this);
        LastPresentationTicket = presentation;
        Action report = () => WritingSystemText.ReportTextPresented(this, presentation);
        if (PresentationReportScheduler is { } schedule) schedule(report);
        else Dispatcher.UIThread.Post(report, DispatcherPriority.Background);
    }
}
