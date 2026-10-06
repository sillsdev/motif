namespace SIL.Motif.App.Services;

/// <summary>Supplies the saved Plain or Expert trace presentation choice.</summary>
public interface ITraceViewPreferences
{
    /// <summary>Whether the trace uses its Expert presentation.</summary>
    bool IsExpert { get; set; }
}

/// <summary>Adapts trace presentation reads and writes to the shared App preference store.</summary>
public sealed class TraceViewPreferencesAdapter(IUserPreferencesStore store) : ITraceViewPreferences
{
    /// <inheritdoc />
    public bool IsExpert
    {
        get => store.Current.Trace == TracePresentation.Expert;
        set => store.Update(preferences => preferences with
        {
            Trace = value ? TracePresentation.Expert : TracePresentation.Plain,
        });
    }
}

/// <summary>Adapts the tech demo acknowledgment to the shared App preference store.</summary>
public sealed class TechDemoNoticePreferencesAdapter(IUserPreferencesStore store) : ITechDemoNoticePreferences
{
    /// <inheritdoc />
    public bool HasSeenTechDemoNotice => store.Current.HasSeenTechDemoNotice;

    /// <inheritdoc />
    public void MarkTechDemoNoticeSeen() =>
        store.Update(preferences => preferences with { HasSeenTechDemoNotice = true });
}
