namespace SIL.Motif.App;

internal static class ZoomPolicy
{
    private static readonly int[] SupportedPercentages = [80, 90, 100, 110, 125, 150];

    public const int DefaultPercent = 100;

    public static IReadOnlyList<int> Percentages => SupportedPercentages;

    public static int Next(int percent)
    {
        var index = Array.IndexOf(SupportedPercentages, percent);
        return index < 0 || index == SupportedPercentages.Length - 1
            ? SupportedPercentages[^1]
            : SupportedPercentages[index + 1];
    }

    public static int Previous(int percent)
    {
        var index = Array.IndexOf(SupportedPercentages, percent);
        return index <= 0 ? SupportedPercentages[0] : SupportedPercentages[index - 1];
    }

    public static bool Supports(int percent) => Array.IndexOf(SupportedPercentages, percent) >= 0;
}
