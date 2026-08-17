using System;

namespace VVO.UI;

public static class FormattingUtils
{
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";

        string[] units = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };

        int unitIndex = 0;
        double displaySize = bytes;

        // Rounded to the precision it will be printed at, or 1023.999 slips through and
        // displays as "1024 KB"
        while (Math.Round(displaySize, 2) >= 1024 && unitIndex < units.Length - 1)
        {
            displaySize /= 1024;
            unitIndex++;
        }

        return $"{displaySize:0.##} {units[unitIndex]}";
    }

    public static string FormatTimestamp(DateTime utc)
    {
        // Records scanned before timestamps were stored carry the default value
        if (utc == default)
            return string.Empty;

        return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

}