using System;
using System.Linq;
using SIL.LCModel;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Seeds FieldWorks' reserved boundary markers as owned rows in a phoneme set.</summary>
public static class OwnedBoundaryMarkerFixture
{
    /// <summary>Adds missing word and morpheme markers to <paramref name="set"/>.</summary>
    public static void EnsureReservedMarkers(LcmCache cache, IPhPhonemeSet set)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(set);

        Ensure(cache, set, LangProjectTags.kguidPhRuleWordBdry, "#");
        Ensure(cache, set, LangProjectTags.kguidPhRuleMorphBdry, "+");
    }

    private static void Ensure(LcmCache cache, IPhPhonemeSet set, Guid id, string representation)
    {
        var repository = cache.ServiceLocator.ObjectRepository;
        IPhBdryMarker marker;
        if (repository.TryGetObject(id, out var existing))
        {
            if (existing is not IPhBdryMarker existingMarker || !set.BoundaryMarkersOC.Contains(existingMarker))
                throw new InvalidOperationException("A reserved boundary marker must be owned by its phoneme set.");
            return;
        }

        marker = cache.ServiceLocator.GetInstance<IPhBdryMarkerFactory>().Create(id);
        set.BoundaryMarkersOC.Add(marker);
        marker.Name.set_String(cache.DefaultVernWs, representation);
        var code = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
        marker.CodesOS.Add(code);
        code.Representation.set_String(cache.DefaultVernWs, representation);
    }
}
