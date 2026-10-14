using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The captured state supplying a read's project facts. These times describe that capture, independently
/// of later FieldWorks saves or a replacement Baseline.
/// </summary>
/// <param name="Token">The exact Baseline token, including capture and bundle evidence.</param>
/// <param name="SourceLastWriteUtc">The source file's last-write time at capture.</param>
/// <param name="PublishedUtc">When this Baseline was published in the Motif store.</param>
public sealed record BaselineReadSource(
    BaselineToken Token, DateTimeOffset SourceLastWriteUtc, DateTimeOffset PublishedUtc);
