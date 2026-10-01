using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Views;

/// <summary>
/// A name that opens the inspector on a click: a morpheme, rule or other object, by identity. It raises
/// <see cref="RequestedEvent"/>, which bubbles to the window, so the page that shows the name never needs the shell.
/// </summary>
public sealed class InspectLink : HyperlinkButton
{
    /// <summary>Raised, bubbling, when a name asks to be shown in the inspector.</summary>
    public static readonly RoutedEvent<InspectRequestedEventArgs> RequestedEvent =
        RoutedEvent.Register<InspectLink, InspectRequestedEventArgs>("InspectRequested", RoutingStrategies.Bubble);

    /// <summary>
    /// What the inspector's first breadcrumb names when it opens from inside this element, such as the word whose
    /// card holds the name. Inherited, so one setting on a card covers every name in it; unset names the page.
    /// </summary>
    public static readonly AttachedProperty<string?> FromProperty =
        AvaloniaProperty.RegisterAttached<InspectLink, Control, string?>("From", inherits: true);

    /// <summary>
    /// The trace the names inside this element were recorded in, such as Try a Word's. Inherited, so one setting on
    /// the page covers every name; unset means the names are not from a trace.
    /// </summary>
    public static readonly AttachedProperty<InspectorTrace?> TraceProperty =
        AvaloniaProperty.RegisterAttached<InspectLink, Control, InspectorTrace?>("Trace", inherits: true);

    public static readonly StyledProperty<IReadOnlyList<InspectorDetail>?> CapturedProperty =
        AvaloniaProperty.Register<InspectLink, IReadOnlyList<InspectorDetail>?>(nameof(Captured));

    public static readonly StyledProperty<InspectorSubject?> SubjectProperty =
        AvaloniaProperty.Register<InspectLink, InspectorSubject?>(nameof(Subject));

    static InspectLink()
    {
        SubjectProperty.Changed.AddClassHandler<InspectLink>((link, _) => link.NameForReaders());
        ContentProperty.Changed.AddClassHandler<InspectLink>((link, _) => link.NameForReaders());
    }

    public InspectLink() => Classes.Add("inspectLink");

    /// <summary>The object the name stands for; without one the link does nothing.</summary>
    public InspectorSubject? Subject
    {
        get => GetValue(SubjectProperty);
        set => SetValue(SubjectProperty, value);
    }

    /// <summary>What the trace recorded about the name, which the inspector shows apart from the Baseline's facts.</summary>
    public IReadOnlyList<InspectorDetail>? Captured
    {
        get => GetValue(CapturedProperty);
        set => SetValue(CapturedProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(HyperlinkButton);

    public static InspectorTrace? GetTrace(Control control) => control.GetValue(TraceProperty);

    public static void SetTrace(Control control, InspectorTrace? value) => control.SetValue(TraceProperty, value);

    public static string? GetFrom(Control control) => control.GetValue(FromProperty);

    public static void SetFrom(Control control, string? value) => control.SetValue(FromProperty, value);

    /// <summary>Asks the window to show <paramref name="subject"/> in the inspector, on behalf of <paramref name="source"/>.</summary>
    public static void Request(Control source, InspectorSubject subject, IReadOnlyList<InspectorDetail>? captured = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(subject);
        source.RaiseEvent(new InspectRequestedEventArgs(subject, source) { Captured = captured ?? [] });
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (Subject is { } subject) Request(this, subject, Captured);
    }

    private void NameForReaders()
    {
        var label = Subject?.Label ?? (Content as string);
        if (label is { Length: > 0 }) AutomationProperties.SetName(this, $"Inspect {label}");
    }
}

/// <summary>A request to show an object in the inspector, and the control it came from, for focus to return to.</summary>
public sealed class InspectRequestedEventArgs(InspectorSubject subject, Control origin)
    : RoutedEventArgs(InspectLink.RequestedEvent)
{
    /// <summary>The object to show.</summary>
    public InspectorSubject Subject { get; } = subject;

    /// <summary>What a trace recorded about the name, when it was named in one.</summary>
    public IReadOnlyList<InspectorDetail> Captured { get; init; } = [];

    /// <summary>The control that asked, which takes the keyboard back when the inspector closes.</summary>
    public Control Origin { get; } = origin;
}
