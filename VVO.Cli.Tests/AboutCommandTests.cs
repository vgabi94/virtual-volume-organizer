namespace VVO.Cli.Tests;

public class AboutCommandTests
{
    [Fact]
    public async Task AboutCarriesTheVersionAndTheTextsVvoHasToDistribute()
    {
        var result = await CliRunner.RunAsync("about");

        Assert.Equal(0, result.ExitCode);
        var about = result.Json;
        Assert.Equal("Virtual Volume Organizer", about.GetProperty("product").GetString());
        Assert.False(string.IsNullOrWhiteSpace(about.GetProperty("version").GetString()));
        Assert.Contains("MIT License", about.GetProperty("license").GetString());
        Assert.Contains("LiteDB", about.GetProperty("thirdPartyNotices").GetString());
        Assert.Contains("System.CommandLine", about.GetProperty("thirdPartyNotices").GetString());
    }
}
