using System.Globalization;
using System.Resources;

namespace SIL.Motif.Host.Store;

internal static class StoreUpgradeMessages
{
    private static readonly ResourceManager Resources = new("SIL.Motif.Host.Store.StoreUpgradeMessages", typeof(StoreUpgradeMessages).Assembly);
    public static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    public static string Format(string key, params object[] values) => string.Format(CultureInfo.CurrentCulture, Get(key), values);
}
