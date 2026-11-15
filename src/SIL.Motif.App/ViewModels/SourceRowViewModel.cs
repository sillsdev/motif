using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>A stored Source identity; its label is never used as its key.</summary>
public sealed class SourceRowViewModel(SourceSummary source)
{
    public SourceSummary Source { get; } = source;
    public string Id => Source.Id;
    public string Name => Source.Name;
    public string Kind => Source.Kind;
    public string State => Source.Status == "Removed" ? MotifAppStrings.SourcesRemoved :
        Source.Error is not null ? MotifAppStrings.SourcesUnavailable : MotifAppStrings.SourcesActive;
}
