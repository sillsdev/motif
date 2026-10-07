using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>Names the source whose complete morphology reading an overflow action requests.</summary>
public enum MorphologyProducer
{
    /// <summary>The complete reading stored in FieldWorks.</summary>
    FieldWorks,

    /// <summary>The complete reading produced by PanGloss.</summary>
    PanGloss,
}

/// <summary>Identifies the word and evidence whose complete morphology reading is requested.</summary>
public sealed record MorphemeOverflowRequest
{
    /// <summary>Creates a request tied to stable word and evidence identities and the evidence revision.</summary>
    /// <param name="wordIdentity">The stable identity of the word owning this reading.</param>
    /// <param name="evidenceIdentity">The stable identity of the evidence containing this reading.</param>
    /// <param name="evidenceRevision">The revision of that evidence.</param>
    /// <param name="producer">The system that produced the reading.</param>
    public MorphemeOverflowRequest(
        string wordIdentity,
        string evidenceIdentity,
        long evidenceRevision,
        MorphologyProducer producer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wordIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceIdentity);
        if (evidenceRevision < 0) throw new ArgumentOutOfRangeException(nameof(evidenceRevision));
        if (!Enum.IsDefined(producer)) throw new ArgumentOutOfRangeException(nameof(producer));
        WordIdentity = wordIdentity;
        EvidenceIdentity = evidenceIdentity;
        EvidenceRevision = evidenceRevision;
        Producer = producer;
    }

    /// <summary>The stable identity of the word owning this reading.</summary>
    public string WordIdentity { get; }

    /// <summary>The stable identity of the evidence containing this reading.</summary>
    public string EvidenceIdentity { get; }

    /// <summary>The revision of that evidence.</summary>
    public long EvidenceRevision { get; }

    /// <summary>The system that produced the reading.</summary>
    public MorphologyProducer Producer { get; }
}

/// <summary>A bubbling host request to show the full reading identified by <see cref="Request"/>.</summary>
public sealed class MorphemeOverflowRequestedEventArgs(
    MorphemeOverflowRequest request,
    Control origin) : RoutedEventArgs(MorphemePanel.OverflowRequestedEvent)
{
    /// <summary>The word and evidence whose complete reading the user asked to open.</summary>
    public MorphemeOverflowRequest Request { get; } = request;

    /// <summary>The native control that received the user's action.</summary>
    public Control Origin { get; } = origin;
}
