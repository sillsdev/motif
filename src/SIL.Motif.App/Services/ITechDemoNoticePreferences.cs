namespace SIL.Motif.App.Services;

/// <summary>Persists whether the person has acknowledged Motif's tech demo notice.</summary>
public interface ITechDemoNoticePreferences
{
    /// <summary>Whether the person has already acknowledged the notice.</summary>
    bool HasSeenTechDemoNotice { get; }

    /// <summary>Remembers that the person acknowledged the notice.</summary>
    void MarkTechDemoNoticeSeen();
}
