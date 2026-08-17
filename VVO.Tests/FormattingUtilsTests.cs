using VVO.UI;

namespace VVO.Tests;

public class FormattingUtilsTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-1, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(1099511627776, "1 TB")]
    public void BytesAreScaledToTheirUnit(long bytes, string expected)
    {
        Assert.Equal(expected, FormattingUtils.FormatBytes(bytes));
    }

    // Rounding to two places first is what stops this reading '1024 KB'
    [Fact]
    public void ASizeThatRoundsUpToTheNextUnitIsCarriedOver()
    {
        Assert.Equal("1 MB", FormattingUtils.FormatBytes(1048575));
    }

    [Fact]
    public void TheLargestUnitIsNotOvershot()
    {
        Assert.EndsWith(" EB", FormattingUtils.FormatBytes(long.MaxValue));
    }

    [Fact]
    public void ATimestampIsShownToTheMinuteInLocalTime()
    {
        var utc = new DateTime(2026, 3, 28, 18, 37, 45, DateTimeKind.Utc);

        Assert.Equal(utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), FormattingUtils.FormatTimestamp(utc));
    }

    // Records scanned before timestamps were stored carry the default value
    [Fact]
    public void AnAbsentTimestampShowsNothing()
    {
        Assert.Equal(string.Empty, FormattingUtils.FormatTimestamp(default));
    }
}
