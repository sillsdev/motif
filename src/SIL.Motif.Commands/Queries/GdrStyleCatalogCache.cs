using SIL.Motif.Contract.Baselines;
using SIL.Motif.Host.Gdr;

namespace SIL.Motif.Commands.Queries;

internal sealed class GdrStyleCatalogCache
{
    private readonly object _gate = new();
    // Alternating projects trade cache hits for the bounded memory of one Baseline.
    private (string Path, BaselineToken Token, GdrStyleCatalog Catalog)? _current;

    internal GdrStyleCatalog For(string path, BaselineToken token)
    {
        lock (_gate)
        {
            if (_current is not { } current || current.Path != path || current.Token != token)
                _current = (path, token, new GdrStyleCatalog());
            return _current.Value.Catalog;
        }
    }
}
