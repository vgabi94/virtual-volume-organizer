using System.IO.Pipelines;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VVO.Cli.Mcp;

namespace VVO.Cli.Tests;

public class McpServerTests : IAsyncLifetime
{
    private readonly string _registry = Path.Combine(Path.GetTempPath(), $"VVO_Mcp_{Guid.NewGuid()}");
    private readonly CancellationTokenSource _stop = new();
    private TempCatalogue _catalogue = null!;
    private Task _server = Task.CompletedTask;

    public async Task InitializeAsync() => _catalogue = await TempCatalogue.CreateAsync();

    public async Task DisposeAsync()
    {
        await _stop.CancelAsync();
        await _server;
        _catalogue.Dispose();

        if (Directory.Exists(_registry))
        {
            Directory.Delete(_registry, recursive: true);
        }
    }

    /// <param name="answer">What the user types into the elicitation form. Left out, the client cannot ask.</param>
    private async Task<McpClient> ConnectAsync(string? defaultDb = null, Func<ElicitRequestParams?, ElicitResult>? answer = null)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();

        var transport = new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        _server = VvoMcpServer.RunAsync(transport, defaultDb, _stop.Token, registry: new RunningServers(_registry));

        var options = new McpClientOptions();
        if (answer != null)
        {
            options.Handlers.ElicitationHandler = (request, _) => ValueTask.FromResult(answer(request));
        }

        return await McpClient.CreateAsync(
            new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), options);
    }

    private static ElicitResult Typing(string word) => new()
    {
        Action = "accept",
        Content = new Dictionary<string, JsonElement> { ["confirmation"] = JsonSerializer.SerializeToElement(word) }
    };

    private static JsonElement Json(CallToolResult result) =>
        JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement;

    private static string? ErrorCode(CallToolResult result) =>
        Json(result).GetProperty("error").GetProperty("code").GetString();

    [Fact]
    public async Task EveryCommandIsATool()
    {
        await using var client = await ConnectAsync();

        var names = (await client.ListToolsAsync()).Select(tool => tool.Name).ToHashSet();

        string[] expected =
        [
            "db_new", "db_info", "db_copy", "db_shrink", "db_export", "db_import",
            "volume_list", "volume_create", "volume_update", "volume_delete",
            "folder_list", "folder_scan", "folder_update", "folder_copy", "folder_move", "folder_delete", "folder_rescan",
            "ls", "tree", "stat", "search", "compare", "add", "rm", "about"
        ];
        Assert.Equal(expected.ToHashSet(), names);
    }

    [Fact]
    public async Task ParametersFollowTheCommandsArgumentsAndOptions()
    {
        await using var client = await ConnectAsync();

        var tools = await client.ListToolsAsync();
        var search = tools.Single(tool => tool.Name == "search").JsonSchema;
        var compare = tools.Single(tool => tool.Name == "compare").JsonSchema;
        var add = tools.Single(tool => tool.Name == "add").JsonSchema;

        Assert.Equal(["term", "db"], search.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("uuid", search.GetProperty("properties").GetProperty("folder").GetProperty("format").GetString());
        Assert.Equal(1000, search.GetProperty("properties").GetProperty("limit").GetProperty("default").GetInt32());

        var compared = compare.GetProperty("properties");
        Assert.Equal("boolean", compared.GetProperty("include_unchanged").GetProperty("type").GetString());
        Assert.False(compared.TryGetProperty("exit_code", out _));
        Assert.False(compared.TryGetProperty("quiet", out _));
        Assert.False(compared.TryGetProperty("format", out _));

        Assert.Equal("array", add.GetProperty("properties").GetProperty("paths").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ADefaultCatalogueMakesDbOptional()
    {
        await using var client = await ConnectAsync(_catalogue.Path);
        await _catalogue.AddVolumeAsync("Backups");

        var info = (await client.ListToolsAsync()).Single(tool => tool.Name == "volume_list").JsonSchema;
        var result = await client.CallToolAsync("volume_list");

        Assert.False(info.TryGetProperty("required", out _));
        Assert.True(result.IsError is null or false);
        Assert.Equal("Backups", Json(result)[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ACallAnswersWithTheCommandsJson()
    {
        await using var client = await ConnectAsync();

        var created = await client.CallToolAsync("volume_create", new Dictionary<string, object?>
        {
            ["name"] = "Archive",
            ["color"] = "#FF0000",
            ["db"] = _catalogue.Path
        });
        var listed = await client.CallToolAsync("volume_list", new Dictionary<string, object?> { ["db"] = _catalogue.Path });

        Assert.True(created.IsError is null or false);
        Assert.Equal("Archive", Json(created).GetProperty("name").GetString());
        Assert.Equal(Json(created).GetProperty("id").GetString(), Json(listed)[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task AValueStartingWithADashIsNotTakenForAnOption()
    {
        await using var client = await ConnectAsync(_catalogue.Path);

        var created = await client.CallToolAsync("volume_create", new Dictionary<string, object?> { ["name"] = "-q" });
        var updated = await client.CallToolAsync("volume_update", new Dictionary<string, object?>
        {
            ["id"] = Json(created).GetProperty("id").GetString(),
            ["name"] = "--db"
        });

        Assert.Equal("-q", Json(created).GetProperty("name").GetString());
        Assert.Equal("--db", Json(updated).GetProperty("name").GetString());
    }

    [Fact]
    public async Task AFailureIsAnErrorResult()
    {
        await using var client = await ConnectAsync(_catalogue.Path);

        var result = await client.CallToolAsync("stat", new Dictionary<string, object?> { ["id"] = Guid.NewGuid() });

        Assert.True(result.IsError);
        Assert.Equal("not_found", ErrorCode(result));
    }

    [Fact]
    public async Task AnUnknownParameterIsAUsageError()
    {
        await using var client = await ConnectAsync(_catalogue.Path);

        var result = await client.CallToolAsync("volume_list", new Dictionary<string, object?> { ["colour"] = "red" });

        Assert.True(result.IsError);
        Assert.Equal("usage", ErrorCode(result));
    }

    [Fact]
    public async Task AMissingArgumentComesWithTheCommandsHelp()
    {
        await using var client = await ConnectAsync(_catalogue.Path);

        var result = await client.CallToolAsync("search");

        Assert.True(result.IsError);
        Assert.Equal("usage", ErrorCode(result));
        Assert.Contains("--limit", ((TextContentBlock)result.Content[1]).Text);
    }

    [Fact]
    public async Task WithoutElicitationADeletionNeedsTheTerminal()
    {
        await using var client = await ConnectAsync(_catalogue.Path);
        var volume = await _catalogue.AddVolumeAsync("Backups");

        var result = await client.CallToolAsync("volume_delete", new Dictionary<string, object?> { ["id"] = volume.Id });

        Assert.True(result.IsError);
        Assert.Equal("confirmation_required", ErrorCode(result));
        Assert.StartsWith("vvo volume delete", Json(result).GetProperty("error").GetProperty("command").GetString());
        Assert.Single(await _catalogue.Volumes.GetVirtualVolumesAsync());
    }

    [Fact]
    public async Task TheUserConfirmsThroughElicitation()
    {
        ElicitRequestParams? asked = null;
        await using var client = await ConnectAsync(_catalogue.Path, request =>
        {
            asked = request;
            return Typing("delete");
        });
        var volume = await _catalogue.AddVolumeAsync("Backups");

        var result = await client.CallToolAsync("volume_delete", new Dictionary<string, object?> { ["id"] = volume.Id });

        Assert.True(result.IsError is null or false);
        Assert.Contains("Backups", asked!.Message);
        Assert.Contains("Type delete to confirm.", asked.Message);
        Assert.Empty(await _catalogue.Volumes.GetVirtualVolumesAsync());
    }

    [Fact]
    public async Task AnyOtherAnswerChangesNothing()
    {
        await using var client = await ConnectAsync(_catalogue.Path, _ => Typing("Delete"));
        var volume = await _catalogue.AddVolumeAsync("Backups");

        var result = await client.CallToolAsync("volume_delete", new Dictionary<string, object?> { ["id"] = volume.Id });

        Assert.Equal("cancelled", ErrorCode(result));
        Assert.Single(await _catalogue.Volumes.GetVirtualVolumesAsync());
    }

    [Fact]
    public async Task DecliningChangesNothing()
    {
        await using var client = await ConnectAsync(_catalogue.Path, _ => new ElicitResult { Action = "decline" });
        var volume = await _catalogue.AddVolumeAsync("Backups");

        var result = await client.CallToolAsync("volume_delete", new Dictionary<string, object?> { ["id"] = volume.Id });

        Assert.Equal("cancelled", ErrorCode(result));
        Assert.Single(await _catalogue.Volumes.GetVirtualVolumesAsync());
    }

    // stdout carries the protocol, so anything else written to it would break the session
    [Fact]
    public async Task TheExecutableServesOverStdio()
    {
        await _catalogue.AddVolumeAsync("Backups");

        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [typeof(CliApp).Assembly.Location, "mcp", "--db", _catalogue.Path]
        }));

        var result = await client.CallToolAsync("volume_list");

        Assert.Equal("vvo", client.ServerInfo.Name);
        Assert.Equal("Backups", Json(result)[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task StopEndsTheServer()
    {
        await using var client = await ConnectAsync(_catalogue.Path);
        var registry = new RunningServers(_registry);

        var stopped = await registry.StopAllAsync(CancellationToken.None);
        await _server.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(Environment.ProcessId, Assert.Single(stopped).Pid);
        Assert.Equal(_catalogue.Path, stopped[0].Db);
        Assert.Empty(Directory.EnumerateFiles(_registry));
    }

    [Fact]
    public async Task StopSkipsServersThatAreGone()
    {
        Directory.CreateDirectory(_registry);
        await File.WriteAllTextAsync(
            Path.Combine(_registry, "1-crashed.json"),
            """{ "pid": 2147483647, "startedAt": "2020-01-01T00:00:00Z" }""");

        var stopped = await new RunningServers(_registry).StopAllAsync(CancellationToken.None);

        Assert.Empty(stopped);
        Assert.Empty(Directory.EnumerateFiles(_registry));
    }

    [Fact]
    public async Task TheServerLeavesNoRegistrationBehind()
    {
        await using (await ConnectAsync())
        {
            Assert.Single(Directory.EnumerateFiles(_registry));
        }

        await _stop.CancelAsync();
        await _server;

        Assert.Empty(Directory.EnumerateFiles(_registry));
    }
}
