using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SIL.LCModel;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.LiveHost.Baselines;

/// <summary>Zips the minimal file-backed Baseline, from a live saved model or from an already-copied one.</summary>
/// <remarks>
/// The <see cref="LcmCache"/> overload neither saves nor disposes the supplied model; project
/// persistence and lifecycle remain the caller's responsibility. The path-based overload packages
/// files a caller has already copied and validated and never touches a live project itself.
/// </remarks>
public sealed class BaselineBundleWriter
{
    private const int CopyBufferSize = 32 * 1024;
    private const string WritingSystemStore = "WritingSystemStore";

    /// <summary>Writes one transport archive and returns its semantic and exact-byte identity.</summary>
    public async Task<BaselineToken> WriteAsync(
        LcmCache savedCache,
        Stream destination,
        CancellationToken cancellationToken)
    {
        if (savedCache is null) throw new ArgumentNullException(nameof(savedCache));
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        if (!destination.CanWrite) throw new ArgumentException("The destination must be writable.", nameof(destination));
        cancellationToken.ThrowIfCancellationRequested();

        var fwDataPath = Path.GetFullPath(savedCache.ProjectId.Path);
        if (!File.Exists(fwDataPath))
            throw new InvalidOperationException("The live model must be backed by an existing saved .fwdata file.");
        if (!string.Equals(Path.GetExtension(fwDataPath), ".fwdata", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The live model must be backed by a .fwdata file.");

        var projectFolder = Path.GetDirectoryName(fwDataPath)!;
        var writingSystemFolder = Path.Combine(projectFolder, WritingSystemStore);
        if (!Directory.Exists(writingSystemFolder))
            throw new InvalidOperationException("The saved project has no WritingSystemStore directory.");

        var semanticDigest = BaselineSemanticDigest.Compute(savedCache, cancellationToken);
        using var hashingDestination = new HashingWriteStream(destination);
        using (var archive = new ZipArchive(hashingDestination, ZipArchiveMode.Create, true))
        {
            await AddFileAsync(
                archive, fwDataPath, Path.GetFileName(fwDataPath), cancellationToken).ConfigureAwait(false);
            foreach (var ldmlPath in Directory.EnumerateFiles(writingSystemFolder, "*.ldml", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
            {
                await AddFileAsync(
                    archive,
                    ldmlPath,
                    WritingSystemStore + "/" + Path.GetFileName(ldmlPath),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        var bundleDigest = hashingDestination.Complete();
        return new BaselineToken(
            savedCache.LangProject.Guid.ToString("D"),
            semanticDigest,
            BaselineSemanticDigest.ProjectionVersion,
            DateTime.UtcNow.ToString("O"),
            bundleDigest);
    }

    /// <summary>
    /// Zips an already-copied, already-validated saved project — <paramref name="fwDataPath"/> plus
    /// <paramref name="writingSystemPaths"/> — and returns the archive's exact-byte digest.
    /// </summary>
    /// <remarks>
    /// Takes paths rather than a live <see cref="LcmCache"/> because the caller has already captured
    /// the project through <see cref="SavedProjectFileCopier"/>; this method only packages what that
    /// capture produced. It does not compute a <see cref="BaselineToken"/>, since the semantic digest
    /// that token needs comes from loading the copy as a scratch model, not from these file paths.
    /// </remarks>
    public async Task<string> WriteAsync(
        string fwDataPath,
        IReadOnlyList<string> writingSystemPaths,
        Stream destination,
        CancellationToken cancellationToken,
        DateTimeOffset? sourceLastWriteUtc = null)
    {
        if (fwDataPath is null) throw new ArgumentNullException(nameof(fwDataPath));
        if (writingSystemPaths is null) throw new ArgumentNullException(nameof(writingSystemPaths));
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        if (!destination.CanWrite) throw new ArgumentException("The destination must be writable.", nameof(destination));
        if (!File.Exists(fwDataPath))
            throw new ArgumentException("The copied .fwdata file does not exist.", nameof(fwDataPath));
        cancellationToken.ThrowIfCancellationRequested();

        using var hashingDestination = new HashingWriteStream(destination);
        using (var archive = new ZipArchive(hashingDestination, ZipArchiveMode.Create, true))
        {
            await AddFileAsync(
                archive, fwDataPath, Path.GetFileName(fwDataPath), cancellationToken, sourceLastWriteUtc)
                .ConfigureAwait(false);
            foreach (var ldmlPath in writingSystemPaths.OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
            {
                await AddFileAsync(
                    archive,
                    ldmlPath,
                    WritingSystemStore + "/" + Path.GetFileName(ldmlPath),
                    cancellationToken,
                    sourceLastWriteUtc).ConfigureAwait(false);
            }
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        return hashingDestination.Complete();
    }

    private static async Task AddFileAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        CancellationToken cancellationToken,
        DateTimeOffset? entryLastWrite = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var source = SavedProjectFileCopier.OpenSavedFile(sourcePath);
        entry.LastWriteTime = entryLastWrite
            ?? WindowsSavedFileMetadata.GetLastWriteTimeUtc(source.SafeFileHandle);
        using var target = entry.Open();
        var buffer = new byte[CopyBufferSize];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
    }

    private sealed class HashingWriteStream : Stream
    {
        private readonly Stream _destination;
        private readonly SHA256 _sha = SHA256.Create();
        private bool _complete;

        public HashingWriteStream(Stream destination) => _destination = destination;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _destination.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _destination.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _destination.Write(buffer, offset, count);
            _sha.TransformBlock(buffer, offset, count, null, 0);
        }

        public override async Task WriteAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _destination.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            _sha.TransformBlock(buffer, offset, count, null, 0);
        }

        public string Complete()
        {
            if (_complete) throw new InvalidOperationException("The bundle digest has already been completed.");
            _sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            _complete = true;
            return BaselineSemanticDigest.FormatDigest(_sha.Hash!);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _sha.Dispose();
            base.Dispose(disposing);
        }
    }
}
