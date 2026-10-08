using System.Globalization;

namespace Tapybara.App.Controls;

/// <summary>Один формат времени для полей границ и шкалы; секунды допустимы при вставке.</summary>
public static class AudioTime
{
    public static string Format(TimeSpan value)
    {
        TimeSpan rounded = TimeSpan.FromMilliseconds(Math.Round(value.TotalMilliseconds));
        return string.Create(CultureInfo.InvariantCulture,
            $"{(int)rounded.TotalMinutes:00}:{rounded.Seconds:00}.{rounded.Milliseconds:000}");
    }

    public static bool TryParse(string text, out TimeSpan value)
    {
        value = default;
        string[] parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length is < 1 or > 3) { return false; }
        double seconds = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            NumberStyles style = i == parts.Length - 1 ? NumberStyles.AllowDecimalPoint : NumberStyles.None;
            if (!double.TryParse(parts[i], style, CultureInfo.InvariantCulture, out double part)
                || !double.IsFinite(part) || part < 0 || (i > 0 && part >= 60)) { return false; }
            seconds = seconds * 60 + part;
        }
        if (seconds >= TimeSpan.MaxValue.TotalSeconds) { return false; }
        value = TimeSpan.FromSeconds(seconds);
        return true;
    }
}
