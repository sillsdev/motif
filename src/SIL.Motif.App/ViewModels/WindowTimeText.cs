using System.Globalization;

namespace SIL.Motif.App.ViewModels;

internal static class WindowTimeText
{
    public static string Format(DateTimeOffset value, TimeProvider clock)
    {
        var local = TimeZoneInfo.ConvertTime(value, clock.LocalTimeZone);
        if (local.Date == clock.GetLocalNow().Date) return local.ToString("t", CultureInfo.CurrentCulture);
        var dateFormat = local.Year == clock.GetLocalNow().Year ? "ddd d MMM, " : "ddd d MMM yyyy, ";
        return local.ToString(dateFormat, CultureInfo.CurrentCulture) + local.ToString("t", CultureInfo.CurrentCulture);
    }
}
