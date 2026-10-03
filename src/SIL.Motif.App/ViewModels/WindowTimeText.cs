using System.Globalization;

namespace SIL.Motif.App.ViewModels;

internal static class WindowTimeText
{
    public static string Format(DateTimeOffset value, TimeProvider clock)
    {
        var local = TimeZoneInfo.ConvertTime(value, clock.LocalTimeZone);
        return local.Date == clock.GetLocalNow().Date
            ? local.ToString("t", CultureInfo.CurrentCulture)
            : local.ToString("ddd d MMM, ", CultureInfo.CurrentCulture) +
              local.ToString("t", CultureInfo.CurrentCulture);
    }
}
