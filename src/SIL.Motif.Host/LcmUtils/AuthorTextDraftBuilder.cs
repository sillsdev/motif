using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Host.LcmUtils;

/// <summary>Plans authored Text bindings with a disposable copy of the live project.</summary>
public static class AuthorTextDraftBuilder
{
    /// <summary>Runs the full paragraph parser without changing or saving the live project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorTextIntent intent,
        Func<CanonicalId>? mintId = null)
    {
        using var scratch = new ScratchCacheFactory().CreateInMemoryCopy(cache);
        foreach (var source in cache.ServiceLocator.WritingSystems.AllWritingSystems)
        {
            scratch.ServiceLocator.WritingSystemManager.GetOrSet(source.Id, out var destination);
            destination.Copy(source);
        }
        var handle = cache.WritingSystemFactory.GetWsFromStr(intent.WritingSystem);
        if (handle != 0 && cache.ServiceLocator.WritingSystemManager.Get(handle) is { } requested)
        {
            scratch.ServiceLocator.WritingSystemManager.GetOrSet(requested.Id, out var destination);
            destination.Copy(requested);
        }
        return AuthorTextComposer.Build(cache, scratch, intent, mintId);
    }
}
