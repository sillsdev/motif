namespace SIL.Motif.App.Services;

/// <summary>Persists whether the person has acknowledged Motif's beta notice.</summary>
public interface IBetaNoticePreferences
{
    /// <summary>Whether the person has already acknowledged the notice.</summary>
    bool HasSeenBetaNotice { get; }

    /// <summary>Remembers that the person acknowledged the notice.</summary>
    void MarkBetaNoticeSeen();
}
