using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.Support.TestFixtures;

/// <summary>Builds a measured response for tests that exercise only its Review Numbers.</summary>
public static class PendingMeasureResponseFactory
{
    /// <summary>Creates a response with a synthetic completed Trial and the requested Review Numbers.</summary>
    public static MeasurePendingResult Create(string jobId, string revision, ReviewNumbersResponse numbers)
    {
        const string digest = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
        var trial = new TrialRecord(jobId, "proposal/test", digest,
            new BaselineToken("project/test", digest, "projection/test", "2026-01-01T00:00:00Z", digest),
            null, new Dictionary<string, string>(StringComparer.Ordinal), [], false, null,
            JobStatus.Completed, DateTimeOffset.UnixEpoch, TrialFreshness.Current);
        return new MeasurePendingResult(trial, numbers) { Revision = revision };
    }
}
