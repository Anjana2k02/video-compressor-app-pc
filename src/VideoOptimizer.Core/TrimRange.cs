using System.Globalization;

namespace VideoOptimizer.Core;

public sealed record TrimRange
{
    public TimeSpan Start { get; }
    public TimeSpan End { get; }
    public TimeSpan Duration => End - Start;

    private TrimRange(TimeSpan start, TimeSpan end) => (Start, End) = (start, end);

    public static bool TryCreate(TimeSpan start, TimeSpan end, TimeSpan duration,
        out TrimRange? range, out string? error)
    {
        range = null;
        error = null;
        if (duration <= TimeSpan.Zero || start < TimeSpan.Zero || start >= end || end > duration)
        {
            error = "Start must be at least 0, before end, and end cannot exceed the video duration.";
            return false;
        }
        range = new TrimRange(start, end);
        return true;
    }

    public static bool TryParseTime(string text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            && seconds >= 0 && seconds <= (decimal)long.MaxValue / TimeSpan.TicksPerSecond)
        {
            try { value = TimeSpan.FromTicks((long)(seconds * TimeSpan.TicksPerSecond)); return true; }
            catch (OverflowException) { return false; }
        }
        return TimeSpan.TryParseExact(text, [@"h\:mm\:ss", @"h\:mm\:ss\.FFF", @"hh\:mm\:ss", @"hh\:mm\:ss\.FFF"],
            CultureInfo.InvariantCulture, out value) && value >= TimeSpan.Zero;
    }

    // Decimal seconds preserve sub-millisecond probe durations on a round trip.
    public static string FormatInput(TimeSpan value) => ((decimal)value.Ticks / TimeSpan.TicksPerSecond).ToString("0.#######", CultureInfo.InvariantCulture);
    public static string FormatDisplay(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
}
