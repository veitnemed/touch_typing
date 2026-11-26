using System.Globalization;

namespace TouchTypingStudio;

// Use this label everywhere a typing score is shown. A percentage must never
// round up across a card/correction boundary. Keep the raw ratio for analysis.
internal static class TypingPrecision
{
    internal static string Label(long correct, long errors)
    {
        decimal total = (decimal)correct + errors;
        if (correct < 0 || errors < 0 || total == 0) return "—";
        decimal basisPoints = decimal.Floor(correct * 10000m / total);
        return (basisPoints / 100m).ToString("0.##", CultureInfo.GetCultureInfo("ru-RU")) + "%";
    }
}
