using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace XDripWidget
{
    public class CurrentGlucoseData
    {
        public double Mmol { get; set; }
        public string Direction { get; set; }
        public string Delta { get; set; }
        public int Battery { get; set; }
        public int MinutesAgo { get; set; }
        public long Timestamp { get; set; }

        public CurrentGlucoseData()
        {
            Mmol = 0.0;
            Direction = "Unknown";
            Delta = "?";
            Battery = -1;
            MinutesAgo = 0;
            Timestamp = 0;
        }

        public bool IsStale
        {
            get { return MinutesAgo > 15; }
        }
    }

    public class HistoryPoint
    {
        public long Timestamp { get; set; }
        public double Mmol { get; set; }
    }

    public static class Constants
    {
        // Clinical AGP 6-band thresholds (mmol/L)
        public const double HYPO_SEVERE  = 3.0;
        public const double HYPO_MILD    = 3.9;
        public const double HYPER_TIGHT  = 7.8;
        public const double HYPER_TARGET = 10.0;
        public const double HYPER_SEVERE = 13.9;
        public const int STALE_MINUTES   = 15;

        // Alerts
        public const double ALERT_HYPO     = 4.5;
        public const double ALERT_HYPER    = 9.0;
        public const double ALERT_CRITICAL = 14.0;

        // Colors
        public static readonly Color ColorVeryLow  = (Color)ColorConverter.ConvertFromString("#EF4444"); // Red < 3.0
        public static readonly Color ColorLow      = (Color)ColorConverter.ConvertFromString("#F59E0B"); // Amber 3.0 - 3.8
        public static readonly Color ColorTight    = (Color)ColorConverter.ConvertFromString("#4ADE80"); // Pale Green 3.9 - 7.8
        public static readonly Color ColorTarget   = (Color)ColorConverter.ConvertFromString("#10B981"); // Emerald 7.9 - 10.0
        public static readonly Color ColorHigh     = (Color)ColorConverter.ConvertFromString("#F59E0B"); // Amber 10.1 - 13.9
        public static readonly Color ColorVeryHigh = (Color)ColorConverter.ConvertFromString("#EF4444"); // Red >= 14.0
        public static readonly Color ColorGray     = (Color)ColorConverter.ConvertFromString("#94A3B8"); // Slate Gray
        public static readonly Color ColorBg       = (Color)ColorConverter.ConvertFromString("#0F172A"); // Slate Dark BG
        public static readonly Color ColorSurface  = (Color)ColorConverter.ConvertFromString("#1E293B"); // Card Surface
        public static readonly Color ColorBorder   = (Color)ColorConverter.ConvertFromString("#334155"); // Border
        public static readonly Color ColorSub      = (Color)ColorConverter.ConvertFromString("#94A3B8"); // Sub text

        private static readonly Dictionary<string, string> TrendArrows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DoubleUp",          "⇈" },
            { "SingleUp",          "↑" },
            { "FortyFiveUp",       "↗" },
            { "Flat",              "→" },
            { "FortyFiveDown",     "↘" },
            { "SingleDown",        "↓" },
            { "DoubleDown",        "⇊" },
            { "NOT COMPUTABLE",    "?" },
            { "RATE OUT OF RANGE", "⚡" },
            { "Unknown",           "?" }
        };

        public static string GetTrendArrow(string direction)
        {
            if (string.IsNullOrEmpty(direction)) return "?";
            string arrow;
            return TrendArrows.TryGetValue(direction, out arrow) ? arrow : "?";
        }

        public static Color GetGlucoseColor(double mmol, bool stale)
        {
            if (stale || mmol <= 0.0) return ColorGray;
            if (mmol < HYPO_SEVERE)   return ColorVeryLow;
            if (mmol < HYPO_MILD)     return ColorLow;
            if (mmol <= HYPER_TIGHT)  return ColorTight;
            if (mmol <= HYPER_TARGET) return ColorTarget;
            if (mmol <= HYPER_SEVERE) return ColorHigh;
            return ColorVeryHigh;
        }

        public static Color GetBatteryColor(int pct, bool stale)
        {
            if (stale || pct < 0) return ColorGray;
            if (pct <= 20) return ColorVeryLow;
            if (pct <= 50) return ColorLow;
            return ColorTarget;
        }

        public static string FormatTimeAgo(int minutesAgo)
        {
            if (minutesAgo < 60)
            {
                return string.Format("{0} м назад", minutesAgo);
            }
            if (minutesAgo < 1440)
            {
                return string.Format("{0} ч назад", minutesAgo / 60);
            }
            return string.Format("{0} д назад", minutesAgo / 1440);
        }
    }
}
