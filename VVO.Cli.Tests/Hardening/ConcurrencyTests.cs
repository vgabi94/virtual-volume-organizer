using VVO.Core.Models;

namespace VVO.Cli.Tests.Hardening;

// Agents run commands side by side, and the GUI may have the catalogue open too
public class ConcurrencyTests
{
    [Fact]
    public async Task TenWritersAtOnceAllLandOrAreToldTheCatalogueIsBusy()
    {
        using var catalogue = await TempCatalogue.CreateAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(index =>
            ProcessRunner.RunAsync(["volume", "create", $"Volume {index}", "--db", catalogue.Path])));

        Assert.All(results, result => Assert.True(
            result.ExitCode == 0 || result.ErrorCode == "database_busy", result.Stdout + result.Stderr));

        var created = results.Where(result => result.ExitCode == 0)
            .Select(result => result.Json.GetProperty("id").GetGuid())
            .Order();
        var stored = (await catalogue.Volumes.GetVirtualVolumesAsync()).Select(volume => volume.Id).Order();

        Assert.Equal(created, stored);
        Assert.Equal(0, (await CliRunner.RunAsync("db", "info", "--db", catalogue.Path)).ExitCode);
    }

    // The scan goes in as one transaction, so a reader sees all of it or none of it
    [Fact]
    public async Task AReaderBesideAWriterNeverSeesHalfAFolder()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");

        const int files = 3000;
        using var disk = new DiskTree();
        for (var i = 0; i < files; i++)
        {
            disk.File($"shot{i:0000}.jpg", 1);
        }

        var writer = ProcessRunner.RunAsync(["folder", "scan", disk.Root, "--volume", volume.Id.ToString(), "--db", catalogue.Path]);

        var seen = new List<int>();
        while (!writer.IsCompleted)
        {
            var read = await CliRunner.RunAsync("search", "shot", "--limit", "1", "--db", catalogue.Path);

            if (read.ErrorCode == "database_busy")
                continue;

            Assert.True(read.ExitCode == 0, read.Stdout);
            seen.Add(read.Json.GetProperty("total").GetInt32());
        }

        Assert.Equal(0, (await writer).ExitCode);
        Assert.All(seen, total => Assert.True(total is 0 or files, $"Saw {total} of {files}."));
        Assert.Equal(files, (await catalogue.Database.FindItemsAsync<FileRecord>(record => record.Name.StartsWith("shot"))).Count);
    }
}
