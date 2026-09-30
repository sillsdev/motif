namespace SIL.Motif.Projection.Usage;

/// <summary>Appends one value-free usage entry to its durable store.</summary>
public interface IUsageLogSink
{
    void Append(UsageLogEntry entry);
}
