using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Worker.Baselines;

/// <summary>What publishing one already-written capture bundle produced.</summary>
public sealed record BaselineCapturePublication(BaselineToken Token, string FwDataPath, bool ReusedExistingBytes);

/// <summary>
/// Verifies a capture bundle a caller already wrote to disk, publishes it atomically under the managed
/// Baseline root, and records the publication — the two steps of the pipeline that need
/// <see cref="BaselineBundleReceiver"/> and <see cref="BaselineRepository"/>, both internal to this
/// assembly.
/// </summary>
/// <remarks>
/// A public seam for a caller outside this assembly — <c>SIL.Motif.Commands</c>'s synchronous
/// <c>baseline capture</c> handler — that has already written a capture bundle to disk and needs it
/// published, without this assembly widening <see cref="BaselineBundleReceiver"/> or
/// <see cref="BaselineRepository"/> themselves to <c>public</c>.
/// </remarks>
public sealed class BaselineCapturePublisher
{
    private readonly MotifDatabase _database;
    private readonly string _managedRoot;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>Creates a publisher over one project's paired database and one managed Baseline root.</summary>
    public BaselineCapturePublisher(MotifDatabase database, string managedRoot, Func<DateTimeOffset>? now = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        if (string.IsNullOrWhiteSpace(managedRoot))
            throw new ArgumentException("A managed Baseline root is required.", nameof(managedRoot));
        _managedRoot = managedRoot;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Verifies the bundle at <paramref name="bundlePath"/> against <paramref name="declaredToken"/>,
    /// publishes it under this instance's managed root, and records the publication for
    /// <paramref name="project"/>.
    /// </summary>
    public async Task<BaselineCapturePublication> PublishAsync(
        ProjectLocator project,
        string bundlePath,
        BaselineToken declaredToken,
        DateTimeOffset sourceLastWriteUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(declaredToken);

        var transfer = Verified(bundlePath);
        var target = new BaselinePublicationTarget(
            Path.Combine(_managedRoot, "baselines"), declaredToken.ProjectIdentity);
        var outcome = await new BaselineBundleReceiver()
            .PublishVerifiedWithOutcomeAsync(transfer, declaredToken, target, cancellationToken)
            .ConfigureAwait(false);

        var baselines = new BaselineRepository(_database);
        baselines.Record(
            ProjectWorkspaceKey.Compute(project), outcome.Publication, _now(), sourceLastWriteUtc);

        return new BaselineCapturePublication(
            outcome.Publication.Token, outcome.Publication.FwDataPath, !outcome.Created);
    }

    /// Hashes what actually landed on disk rather than trusting what the caller reported writing.
    private static VerifiedBinaryTransfer Verified(string bundlePath)
    {
        using var stream = File.OpenRead(bundlePath);
        using var sha = SHA256.Create();
        var digest = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        return new VerifiedBinaryTransfer(Guid.NewGuid().ToString("N"), bundlePath, stream.Length, digest);
    }
}
