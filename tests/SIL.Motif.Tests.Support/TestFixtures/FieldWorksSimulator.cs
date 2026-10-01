using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Simulates FieldWorks holding and saving one temporary project used by a test.</summary>
public sealed class FieldWorksSimulator
{
    private readonly string _projectPath;
    private readonly TimeProvider _timeProvider;

    public FieldWorksSimulator(string projectPath, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        _projectPath = projectPath;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Holds the project's <c>.lock</c> file open with no sharing until disposed, pinned by
    /// `AHoldCreatesTheLockExclusivelyAndRemovesItOnRelease`. A lock file that already existed is left behind
    /// on release, pinned by `AHoldOnALockThatAlreadyExistedLeavesThatLockBehind`.
    /// </summary>
    public IDisposable Hold()
    {
        var lockPath = _projectPath + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(lockPath))!);
        var existed = File.Exists(lockPath);
        return new HeldProject(lockPath, removeOnRelease: !existed, new FileStream(
            lockPath, existed ? FileMode.Open : FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));
    }

    public void SaveEdit(Action<LcmCache> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var before = File.GetLastWriteTimeUtc(_projectPath);
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(_projectPath))
        {
            edit(cache);
            loader.Save(cache);
        }

        var savedAt = _timeProvider.GetUtcNow().UtcDateTime;
        if (savedAt <= before) savedAt = before.AddSeconds(1);
        File.SetLastWriteTimeUtc(_projectPath, savedAt);
    }

    /// <summary>
    /// Saves a change FieldWorks could make, for a test that needs a new Baseline. An empty save is not one:
    /// unchanged bytes saved within two seconds rebuild the same bundle, because zip entry times have that
    /// resolution.
    /// </summary>
    public void AddWordform(string form) => SaveEdit(cache =>
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(form, cache.DefaultVernWs))));

    public void DeleteWordform(string form) => SaveEdit(cache =>
    {
        var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
            .Single(candidate => candidate.Form.VernacularDefaultWritingSystem?.Text == form);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, wordform.Delete);
    });

    private sealed class HeldProject(string lockPath, bool removeOnRelease, FileStream stream) : IDisposable
    {
        public void Dispose()
        {
            stream.Dispose();
            if (!removeOnRelease) return;
            try
            {
                File.Delete(lockPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
