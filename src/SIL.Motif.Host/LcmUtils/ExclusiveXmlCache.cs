using System.Reflection;
using System.Runtime.ExceptionServices;
using SIL.LCModel;

namespace SIL.Motif.Host.LcmUtils;

/// <summary>
/// Opens an XML cache under its file lock without joining LibLCM's project-name-based shared commit log.
/// Project sharing settings and writing-system definitions remain unchanged.
/// </summary>
internal static class ExclusiveXmlCache
{
    private static readonly MethodInfo Initialize = typeof(LcmCache).GetMethod(
        "Initialize", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)
        ?? throw new InvalidOperationException("LibLCM no longer exposes the cache initialization required for an exclusive XML open.");

    internal static LcmCache Open(string path, LcmDirectories directories, LcmThreadedProgress progress)
    {
        // This selection bypasses sharing; the no-project factory does not enable a memory writing-system manager.
        var selection = new FileProjectId(path, BackendProviderType.kXMLWithMemoryOnlyWsMgr);
        var cache = LcmCache.CreateCacheWithNoLangProj(selection, null,
            new HeadlessLcmUi(progress.SynchronizeInvoke), directories, new LcmSettings());
        try
        {
            cache.ServiceLocator.DataSetup.UseMemoryWritingSystemManager = false;
            cache.ServiceLocator.DataSetup.StartupExtantLanguageProject(new FileProjectId(path), true, progress);
            // LcmCache.Initialize is private; skipping it omits default-writing-system checks and PrepareCache.
            Initialize.Invoke(cache, null);
            return cache;
        }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        {
            cache.Dispose();
            ExceptionDispatchInfo.Capture(failure.InnerException).Throw();
            throw;
        }
        catch
        {
            cache.Dispose();
            throw;
        }
    }
}
