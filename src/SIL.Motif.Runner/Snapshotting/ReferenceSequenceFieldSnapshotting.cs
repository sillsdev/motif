using System.Collections.Generic;
using System.Globalization;
using SIL.Motif.Contract.Ids;
using SIL.LCModel;

namespace SIL.Motif.Runner.Snapshotting;

internal static class ReferenceSequenceFieldSnapshotting
{
    public static IReadOnlyDictionary<string, string> ReadAlternatives<TRef>(IList<TRef> sequence)
        where TRef : ICmObject
    {
        var result = new Dictionary<string, string>();
        for (var index = 0; index < sequence.Count; index++)
            result[index.ToString("D10", CultureInfo.InvariantCulture)] =
                CanonicalId.FromGuid(sequence[index].Guid).Value;
        return result;
    }
}
