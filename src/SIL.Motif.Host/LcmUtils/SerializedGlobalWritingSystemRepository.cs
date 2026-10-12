using SIL.LCModel.Core.WritingSystems;

namespace SIL.Motif.Host.LcmUtils;

/// <summary>
/// A <see cref="CoreGlobalWritingSystemRepository"/> whose writes run one at a time and whose <c>Set</c>
/// of an id the store already holds replaces it, because the base type throws for a second save of an id.
/// </summary>
/// <remarks>
/// Disposing a <see cref="SIL.LCModel.LcmCache"/> saves its local writing-system store, and each save
/// reaches the process-wide shared repository through <c>Set</c>. The caller picks <c>Set</c> or <c>Replace</c>
/// by asking whether the store holds the id, and that check and the write are separate steps. Two caches
/// disposed together (two Applies finishing, or an Apply beside a project close) can both see the id absent,
/// and the second <c>Set</c> throws "Unable to set writing system 'en' because this id already exists".
/// <see cref="FwDataProjectLoader.Init"/> installs this type so every cache the process opens shares one lock
/// around check and write. Pinned by <c>ConcurrentCacheDisposalsNeverCollideInTheSharedWritingSystemRepository</c>.
/// </remarks>
internal sealed class SerializedGlobalWritingSystemRepository : CoreGlobalWritingSystemRepository
{
    private static readonly object WriteGate = new();

    /// <inheritdoc />
    public override void Set(CoreWritingSystemDefinition ws)
    {
        lock (WriteGate)
        {
            if (Contains(ws.LanguageTag)) base.Replace(ws.LanguageTag, ws);
            else base.Set(ws);
        }
    }

    /// <inheritdoc />
    public override void Replace(string languageTag, CoreWritingSystemDefinition newWs)
    {
        lock (WriteGate) base.Replace(languageTag, newWs);
    }
}
