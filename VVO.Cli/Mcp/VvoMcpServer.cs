using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VVO.Cli.Output;

namespace VVO.Cli.Mcp;

/// <summary>
/// Serves the command tree over MCP. A tool call runs its command in-process, as the command line
/// would, and answers with the JSON the command line would print.
/// </summary>
public static class VvoMcpServer
{
    public const string Instructions =
        """
        Tools read and edit .vvo catalogues: records of what is on offline drives and backups (names, sizes,
        timestamps), grouped into virtual volumes. Files on disk are only ever read, when scanning.

        - Every tool that works on a catalogue takes `db`, the path of the .vvo file. Give absolute paths.
        - Each result is one JSON document. A failure is `{ "error": { "code", "message", ... } }`.
        - Everything is named by id. List first (volume_list, folder_list), then act on the ids returned.
          ls, tree and stat look inside a folder; search finds files by name. A folder entry id stands
          for the top of its tree.
        - compare with `disk` shows what changed on disk since a scan; with `other` it diffs two folders.
          Neither writes anything.
        - Deleting, rescanning and replacing a file ask the user to type a word first. When the client
          cannot ask them, the tool changes nothing and fails with `confirmation_required`: show the user
          `message`, and ask them to run `command` themselves in a terminal. Never try to answer for them.
        """;

    /// <param name="defaultDb">The catalogue a tool works on when the call names none.</param>
    /// <param name="configure">Replaces service registrations for each call, as the tests do.</param>
    public static async Task RunAsync(
        ITransport transport,
        string? defaultDb,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configure = null,
        RunningServers? registry = null)
    {
        IReadOnlyList<CommandTool> tools;
        using (var services = ServiceConfiguration.ConfigureServices())
        {
            tools = CommandTool.From(CliApp.BuildRoot(services), defaultDb);
        }

        var byName = tools.ToDictionary(tool => tool.Tool.Name);

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "vvo",
                Title = "Virtual Volume Organizer",
                Version = typeof(VvoMcpServer).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0"
            },
            ServerInstructions = Instructions,
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
                {
                    Tools = [.. tools.Select(tool => tool.Tool)]
                }),
                CallToolHandler = (request, token) => CallAsync(request, byName, configure, token)
            }
        };

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var registration = (registry ?? RunningServers.Default).Register(defaultDb, stop);
        await using var server = McpServer.Create(transport, options);

        try
        {
            await server.RunAsync(stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    private static async ValueTask<CallToolResult> CallAsync(
        RequestContext<CallToolRequestParams> request,
        IReadOnlyDictionary<string, CommandTool> tools,
        Action<IServiceCollection>? configure,
        CancellationToken cancellationToken)
    {
        var name = request.Params?.Name ?? string.Empty;
        if (!tools.TryGetValue(name, out var tool))
            return Failure(CliException.Usage($"There is no tool '{name}'."));

        var progressToken = request.Params?.ProgressToken;

        IReadOnlyList<string> args;
        try
        {
            args = tool.CommandLine(request.Params?.Arguments, quiet: progressToken == null);
        }
        catch (CliException e)
        {
            return Failure(e);
        }

        // Fresh per call: the database service remembers the file it was last pointed at
        using var services = ServiceConfiguration.ConfigureServices(collection =>
        {
            collection.AddSingleton<ITerminal>(new NoTerminal());
            collection.AddSingleton<IConfirmationPrompt>(new ElicitationPrompt(request.Server));
            configure?.Invoke(collection);
        });

        var output = new StringWriter();
        await using var error = new ProgressWriter(request.Server, progressToken);

        var exitCode = await CliApp.RunAsync(
            CliApp.BuildRoot(services), args, output, error, cancellationToken, handleTermination: false);

        await error.FlushAsync(cancellationToken);

        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = output.ToString().TrimEnd() }],
            IsError = exitCode != (int)ExitCode.Success
        };

        // The help for the command, which the command line shows on stderr
        if (exitCode == (int)ExitCode.Usage && error.Text.Trim() is { Length: > 0 } help)
        {
            result.Content.Add(new TextContentBlock { Text = help });
        }

        return result;
    }

    private static CallToolResult Failure(CliException error) => new()
    {
        Content = [new TextContentBlock { Text = System.Text.Json.JsonSerializer.Serialize(error.ToEnvelope(), Json.Options) }],
        IsError = true
    };

    private sealed class NoTerminal : ITerminal
    {
        // stdin carries the protocol; no person is typing into it
        public bool IsInteractive => false;

        public string? ReadLine() => null;
    }

    /// <summary>
    /// Asks the user through the client's elicitation form. The client shows the form to the user,
    /// not to the model, so an agent cannot answer it.
    /// </summary>
    private sealed class ElicitationPrompt(McpServer server) : IConfirmationPrompt
    {
        private const string Field = "confirmation";

        // A client declaring elicitation without saying which modes supports forms
        public bool CanAsk => server.ClientCapabilities?.Elicitation is { } elicitation
            && (elicitation.Form != null || elicitation.Url == null);

        public async Task<string?> AskAsync(ConfirmationRequest request, CancellationToken cancellationToken)
        {
            var typeWord = $"Type {request.Word} to confirm.";
            var message = string.Join('\n',
                [.. request.Explanation ?? [], request.Title, request.ItemName, request.Message, typeWord]);

            var result = await server.ElicitAsync(new ElicitRequestParams
            {
                Message = message,
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = { [Field] = new ElicitRequestParams.StringSchema { Title = typeWord } },
                    Required = [Field]
                }
            }, cancellationToken);

            return result.IsAccepted
                && result.Content?.TryGetValue(Field, out var answer) == true
                && answer.ValueKind == System.Text.Json.JsonValueKind.String
                    ? answer.GetString()
                    : null;
        }
    }

    /// <summary>
    /// Stands in for stderr: keeps what the command writes there, and sends each line as an MCP
    /// progress notification when the client asked for progress.
    /// </summary>
    private sealed class ProgressWriter(McpServer server, ProgressToken? token) : TextWriter
    {
        private readonly StringBuilder _all = new();
        private readonly StringBuilder _line = new();
        private readonly Lock _lock = new();
        private Task _sent = Task.CompletedTask;
        private int _count;

        public override Encoding Encoding => Encoding.UTF8;

        public string Text
        {
            get
            {
                lock (_lock)
                {
                    return _all.ToString();
                }
            }
        }

        public override void Write(char value)
        {
            lock (_lock)
            {
                _all.Append(value);

                if (value == '\n')
                {
                    Send(_line.ToString().TrimEnd('\r'));
                    _line.Clear();
                }
                else
                {
                    _line.Append(value);
                }
            }
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            Task sent;
            lock (_lock)
            {
                sent = _sent;
            }

            await sent;
        }

        public override async ValueTask DisposeAsync()
        {
            await FlushAsync(CancellationToken.None);
            await base.DisposeAsync();
        }

        // Chained, so the notifications arrive in the order the lines were written
        private void Send(string line)
        {
            if (token is not { } progressToken || line.Length == 0)
                return;

            var value = new ProgressNotificationValue { Progress = ++_count, Message = line };
            _sent = _sent.ContinueWith(
                async _ =>
                {
                    try
                    {
                        await server.NotifyProgressAsync(progressToken, value);
                    }
                    catch (Exception)
                    {
                        // Progress is a courtesy; a client that has gone does not fail the command
                    }
                },
                TaskScheduler.Default).Unwrap();
        }
    }
}
