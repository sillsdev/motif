using SIL.LCModel;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Worker.Baselines;

namespace SIL.Motif.Commands;

/// <summary>Projects saved facts without taking ownership of the original project or published Baseline.</summary>
internal static class ProjectReadCache
{
    /// <summary>Uses the matching Baseline, or a consistent saved-project copy when the source has changed.</summary>
    internal static T ReadCurrent<T>(ProjectLocator project, BaselineRecord? baseline,
        long liveLastWriteTicks, Func<LcmCache, long, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (baseline is not null && liveLastWriteTicks == baseline.SourceLastWriteUtc.UtcDateTime.Ticks)
        {
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            return action(reader.Cache, liveLastWriteTicks);
        }
        return ReadSaved(project, (copy, cache) => action(cache, copy.SourceLastWriteUtc.UtcDateTime.Ticks));
    }

    /// <summary>Reads one complete saved file through its private scratch copy, retaining that save's timestamp.</summary>
    internal static T ReadSaved<T>(ProjectLocator project, Func<SavedProjectFilesCopy, LcmCache, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var directory = Path.Combine(Path.GetTempPath(), "SIL.Motif.ProjectRead", Guid.NewGuid().ToString("N"));
        try
        {
            SavedProjectFilesCopy copy;
            try
            {
                copy = new SavedProjectFileCopier().CopyAsync(project.FullFwDataPath, directory,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (InvalidDataException)
            {
                throw new ProjectSavingException();
            }
            using var cache = new FwDataProjectLoader().LoadScratchCache(copy.FwDataPath);
            return action(copy, cache);
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
