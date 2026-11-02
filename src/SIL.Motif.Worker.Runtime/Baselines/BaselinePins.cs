namespace SIL.Motif.Worker.Baselines;

/// <summary>The durable categories of work that keep a published Baseline directory alive.</summary>
[Flags]
public enum BaselinePinSources
{
    None = 0,
    ActiveJob = 1,
    DryRun = 2,
    Decision = 4,
    Receipt = 8,
    ParsimonyJob = 16,
    ParsimonyReport = 32,
    ParsimonyCheckRun = 64
}

/// <summary>Provides the durable source categories that pin a Baseline.</summary>
public interface IBaselinePinQuery
{
    BaselinePinSources GetPinSources(string baselinePath);
}
