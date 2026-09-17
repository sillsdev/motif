using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Baselines;

/// <summary>The one field a Baseline capture needs: which project's saved file to read.</summary>
public sealed record BaselineCaptureRequest(string ProjectPath);

/// <summary>
/// Captures a Baseline from a project's saved <c>.fwdata</c> — the file on disk, never the live model —
/// and publishes it synchronously as one command.
/// </summary>
/// <remarks>
/// <para>
/// Four steps have to happen in order: copy the saved files with delete sharing so a FieldWorks rename
/// never blocks, load the copy as a scratch model to compute its semantic identity, zip the copy into a
/// transport bundle, and publish that bundle atomically before recording it. Nothing here opens the
/// live project or touches its <c>.fwdata.lock</c> or <c>.bak</c> — see <see cref="SavedProjectFileCopier"/>.
/// </para>
/// <para>
/// This command is deliberately synchronous and enqueues nothing durable: it captures, publishes, and
/// returns within one call, for a caller who wants to wait on the result rather than poll a job.
/// </para>
/// </remarks>
public static class BaselineCaptureCommand
{
    /// <summary>Captures and publishes a Baseline, resolving the managed root the real installation uses.</summary>
    public static CommandOutcome<BaselineCaptureResponse> Capture(BaselineCaptureRequest request) =>
        Capture(request, RunnerOptions.ResolveRoot());

    /// <summary>
    /// Captures and publishes a Baseline under an explicitly supplied managed root. The single-argument
    /// overload is what production code and the CLI call; this one exists so a test can supply its own
    /// disposable root rather than sharing the one real installation on the machine.
    /// </summary>
    public static CommandOutcome<BaselineCaptureResponse> Capture(BaselineCaptureRequest request, string managedRoot)
    {
        return ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
        {
            var staging = Path.Combine(managedRoot, "captures");
            Directory.CreateDirectory(staging);
            var captureDirectory = Path.Combine(staging, Guid.NewGuid().ToString("N"));
            var bundlePath = captureDirectory + ".zip";
            try
            {
                SavedProjectFilesCopy copy;
                try
                {
                    copy = new SavedProjectFileCopier()
                        .CopyAsync(project.FullFwDataPath, captureDirectory, CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
                catch (InvalidDataException ex)
                {
                    return CommandOutcome<BaselineCaptureResponse>.Refused(new Refusal(
                        "baseline.source-incomplete", FailureReason.InvalidArgument, ex.Message,
                        Fact(("projectPath", request.ProjectPath))));
                }

                string projectIdentity;
                string semanticDigest;
                try
                {
                    using var cache = new FwDataProjectLoader().LoadScratchCache(copy.FwDataPath);
                    projectIdentity = cache.LangProject.Guid.ToString("D");
                    semanticDigest = BaselineSemanticDigest.Compute(cache, CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return CommandOutcome<BaselineCaptureResponse>.Refused(new Refusal(
                        "baseline.copy-unloadable", FailureReason.InvalidArgument, ex.Message,
                        Fact(("projectPath", request.ProjectPath))));
                }

                BaselineCapturePublication publication;
                try
                {
                    string bundleDigest;
                    using (var destination = File.Create(bundlePath))
                    {
                        // Stamp from the source's save time; the copy's own is now, rehashing every capture.
                        bundleDigest = new BaselineBundleWriter()
                            .WriteAsync(copy.FwDataPath, copy.WritingSystemPaths, destination,
                                CancellationToken.None, copy.SourceLastWriteUtc)
                            .GetAwaiter().GetResult();
                    }
                    var declaredToken = new BaselineToken(
                        projectIdentity, semanticDigest, BaselineSemanticDigest.ProjectionVersion,
                        DateTime.UtcNow.ToString("O"), bundleDigest);

                    publication = new BaselineCapturePublisher(database, managedRoot)
                        .PublishAsync(project, bundlePath, declaredToken, copy.SourceLastWriteUtc, CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
                catch (InvalidDataException ex)
                {
                    return CommandOutcome<BaselineCaptureResponse>.Refused(new Refusal(
                        "baseline.owned-root-violation", FailureReason.StoreInconsistent, ex.Message,
                        Fact(("projectPath", request.ProjectPath))));
                }
                catch (IOException ex)
                {
                    return CommandOutcome<BaselineCaptureResponse>.Refused(new Refusal(
                        "baseline.busy", FailureReason.Busy, ex.Message,
                        Fact(("projectPath", request.ProjectPath))));
                }

                var registrationFailure = RecordKnownProject(managedRoot, project);

                var held = File.Exists(project.FullFwDataPath + ".lock");
                return CommandOutcome<BaselineCaptureResponse>.Success(new BaselineCaptureResponse(
                    publication.Token, publication.FwDataPath, copy.SourceLastWriteUtc, held,
                    publication.ReusedExistingBytes, registrationFailure));
            }
            finally
            {
                DeleteDirectory(captureDirectory);
                DeleteFile(bundlePath);
            }
        });
    }

    // A captured project is one the machine knows about, whichever front end captured it (ADR 0043).
    private static KnownProjectRegistrationFailure? RecordKnownProject(string managedRoot, ProjectLocator project)
    {
        var failure = KnownProjectRecorder.TryRecord(managedRoot, project);
        return failure is null
            ? null
            : new KnownProjectRegistrationFailure(
                "The capture succeeded and was published, but machine-store registration failed for '" +
                Path.Combine(Path.GetFullPath(managedRoot), "motif.db") + "': " + failure.Message);
    }

    private static void DeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void DeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string ResolveProductVersion() => MotifProductVersion.CurrentText;

    private static Dictionary<string, string> Fact(params (string Key, string? Value)[] entries)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            if (value is not null) facts[key] = value;
        }
        return facts;
    }
}
