using VideoOptimizer.Core;
using Xunit;

namespace VideoOptimizer.Tests;

public sealed class TrimTests
{
    [Theory]
    [InlineData(0, 10, 10, true)]
    [InlineData(0.1, 0.2, 1, true)]
    [InlineData(-1, 2, 10, false)]
    [InlineData(2, 2, 10, false)]
    [InlineData(3, 2, 10, false)]
    [InlineData(0, 11, 10, false)]
    [InlineData(0, 1, 0, false)]
    public void ValidatesBounds(double start, double end, double duration, bool valid)
    {
        Assert.Equal(valid, TrimRange.TryCreate(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end),
            TimeSpan.FromSeconds(duration), out var range, out var error));
        if (valid) { Assert.NotNull(range); Assert.Equal(end - start, range.Duration.TotalSeconds, 6); Assert.Null(error); }
        else { Assert.Null(range); Assert.NotNull(error); }
    }
    [Theory]
    [InlineData("2.5", 2.5)]
    [InlineData("01:02:03.125", 3723.125)]
    [InlineData("0", 0)]
    public void ParsesAccessibleTimeFields(string input, double seconds)
    {
        Assert.True(TrimRange.TryParseTime(input, out var value));
        Assert.Equal(seconds, value.TotalSeconds);
    }
    [Theory]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e999")]
    [InlineData("")]
    [InlineData("tomorrow")]
    [InlineData("00:99:99")]
    public void RejectsInvalidTimes(string text) => Assert.False(TrimRange.TryParseTime(text, out _));

    [Fact]
    public void DoesNotRoundEndPastSourceDuration()
    {
        var source = TimeSpan.FromTicks(125125123);
        Assert.True(TrimRange.TryParseTime(TrimRange.FormatInput(source), out var roundTrip));
        Assert.Equal(source, roundTrip);
    }
}
