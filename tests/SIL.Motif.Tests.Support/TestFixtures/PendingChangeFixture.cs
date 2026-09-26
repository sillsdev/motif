using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Identity of a spelling change stored in a test project's Motif store.</summary>
public sealed record PendingSpellingChange(Guid WordformId, string Word);

/// <summary>Adds a word as FieldWorks would, captures the project, and stores a spelling change through Motif's real commands.</summary>
public static class PendingChangeFixture
{
    /// <summary>
    /// Adds a FieldWorks wordform, captures the project, and stores an incorrect-spelling change for it.
    /// </summary>
    /// <param name="fwDataPath">The file-backed FieldWorks project to change.</param>
    /// <param name="managedRoot">The Motif root that owns the project's store.</param>
    /// <param name="word">The vernacular spelling to mark as incorrect.</param>
    /// <returns>The wordform identity and spelling used by the stored change.</returns>
    public static PendingSpellingChange AddIncorrectSpelling(string fwDataPath, string managedRoot, string word)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(word);

        var wordformId = Guid.Empty;
        new FieldWorksSimulator(fwDataPath).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs)).Guid));

        var capture = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(fwDataPath), managedRoot);
        if (!capture.Succeeded)
            throw new InvalidOperationException(capture.Refusal?.Message ?? "The Baseline could not be captured.");

        var version = MotifProductVersion.CurrentText;
        var loaded = PendingChanges.Load(new PendingChangesRequest(fwDataPath, version));
        if (!loaded.Succeeded)
            throw new InvalidOperationException(loaded.Refusal?.Message ?? "Pending changes could not be loaded.");

        var put = PendingChanges.Put(new PutPendingChangeRequest(
            fwDataPath, version, loaded.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, word)));
        if (!put.Succeeded)
            throw new InvalidOperationException(put.Refusal?.Message ?? "The spelling change could not be stored.");

        return new PendingSpellingChange(wordformId, word);
    }
}
