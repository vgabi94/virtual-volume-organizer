using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace VVO.Cli.Mcp;

public static class McpCommand
{
    public static Command Create(IServiceProvider services)
    {
        var db = new Option<string>("--db")
        {
            Description = "The catalogue tools work on when they are not given one."
        };

        var command = new Command(
            "mcp",
            "Run an MCP server on stdin and stdout, offering every command as a tool. It ends when the "
            + "client disconnects, or when 'vvo mcp stop' is run.")
        {
            db,
            Stop(services)
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var defaultDb = parseResult.GetValue(db) is { } path ? Path.GetFullPath(path) : null;

            await VvoMcpServer.RunAsync(
                new StdioServerTransport("vvo"), defaultDb, cancellationToken,
                registry: services.GetRequiredService<RunningServers>());
            return 0;
        });

        return command.CliOnly();
    }

    private static Command Stop(IServiceProvider services)
    {
        var command = new Command("stop", "Stop every vvo MCP server this user is running.");

        command.SetJsonAction(services, async context => new
        {
            Stopped = await context.Service<RunningServers>().StopAllAsync(context.CancellationToken)
        });

        return command;
    }
}
