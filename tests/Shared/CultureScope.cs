using System.Globalization;

namespace SIL.Motif.Tests.TestFixtures;

internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _originalCulture;

    public CultureScope(CultureInfo culture)
    {
        _originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
    }

    public void Dispose() => CultureInfo.CurrentCulture = _originalCulture;
}
