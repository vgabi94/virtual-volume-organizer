using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.CommandLine.Help;
using ModelContextProtocol.Protocol;
using Argument = System.CommandLine.Argument;
using VVO.Cli.Output;

namespace VVO.Cli.Mcp;

/// <summary>
/// A command of the CLI offered as an MCP tool: its arguments and options become the tool's
/// parameters, and a call becomes the command line that runs it.
/// </summary>
public sealed class CommandTool
{
    private readonly IReadOnlyList<string> _path;
    private readonly IReadOnlyList<Parameter> _parameters;
    private readonly string? _defaultDb;

    private CommandTool(IReadOnlyList<string> path, Command command, string? defaultDb)
    {
        _path = path;
        _defaultDb = defaultDb;
        _parameters = ParametersOf(command);

        Tool = new Tool
        {
            Name = string.Join('_', path),
            Description = command.Description,
            InputSchema = JsonSerializer.SerializeToElement(InputSchema())
        };
    }

    public Tool Tool { get; }

    /// <param name="defaultDb">What --db is when a call leaves it out, which makes it optional.</param>
    public static IReadOnlyList<CommandTool> From(RootCommand root, string? defaultDb)
    {
        var tools = new List<CommandTool>();
        Collect(root, [], defaultDb, tools);
        return tools;
    }

    /// <summary>
    /// The command line a call stands for. Throws a usage <see cref="CliException"/> for a
    /// parameter the tool does not have.
    /// </summary>
    /// <param name="quiet">Leaves progress out, for a caller with nowhere to show it.</param>
    public IReadOnlyList<string> CommandLine(IDictionary<string, JsonElement>? arguments, bool quiet)
    {
        arguments ??= new Dictionary<string, JsonElement>();

        var unknown = arguments.Keys.Where(name => _parameters.All(parameter => parameter.Name != name)).ToList();
        if (unknown.Count > 0)
            throw CliException.Usage($"'{Tool.Name}' has no parameter {string.Join(", ", unknown.Select(name => $"'{name}'"))}.");

        var args = new List<string>(_path);
        var positional = new List<string>();

        foreach (var parameter in _parameters)
        {
            if (!arguments.TryGetValue(parameter.Name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                if (parameter.IsDb && _defaultDb != null)
                {
                    args.AddRange([parameter.Symbol.Name, _defaultDb]);
                }

                continue;
            }

            var values = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(Text).ToList() : [Text(value)];

            if (parameter.Symbol is Argument)
            {
                positional.AddRange(values);
            }
            else if (parameter.IsFlag && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                if (value.ValueKind == JsonValueKind.True)
                {
                    args.Add(parameter.Symbol.Name);
                }
            }
            else
            {
                foreach (var text in values)
                {
                    // Joined, so a value starting with '-' is not read as an option of its own
                    args.AddRange(text.StartsWith('-') ? [$"{parameter.Symbol.Name}={text}"] : [parameter.Symbol.Name, text]);
                }
            }
        }

        if (quiet)
        {
            args.Add(CliApp.QuietOption.Name);
        }

        if (positional.Any(text => text.StartsWith('-')))
        {
            args.Add("--");
        }

        args.AddRange(positional);
        return args;
    }

    private static void Collect(Command command, List<string> path, string? defaultDb, List<CommandTool> tools)
    {
        foreach (var subcommand in command.Subcommands.Where(subcommand => !subcommand.Hidden && !subcommand.IsCliOnly()))
        {
            List<string> subpath = [.. path, subcommand.Name];

            if (subcommand.Subcommands.Count == 0)
            {
                tools.Add(new CommandTool(subpath, subcommand, defaultDb));
            }
            else
            {
                Collect(subcommand, subpath, defaultDb, tools);
            }
        }
    }

    private static List<Parameter> ParametersOf(Command command)
    {
        IEnumerable<Symbol> symbols = command.Arguments.Concat<Symbol>(command.Options
            .Where(option => option is not (HelpOption or VersionOption)));

        var parameters = symbols
            .Where(symbol => !symbol.Hidden && !symbol.IsCliOnly())
            .Select(symbol => new Parameter(symbol))
            .ToList();

        var duplicate = parameters.GroupBy(parameter => parameter.Name).FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"'{command.Name}' has two parameters named '{duplicate.Key}'.");

        return parameters;
    }

    private JsonObject InputSchema()
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var parameter in _parameters)
        {
            var schema = SchemaOf(parameter.ValueType);
            var description = parameter.Symbol.Description;

            if (parameter.IsDb && _defaultDb != null)
            {
                description = $"{description} {_defaultDb} when left out.";
            }
            else if (parameter.IsRequired)
            {
                required.Add(parameter.Name);
            }

            if (!string.IsNullOrEmpty(description))
            {
                schema["description"] = description;
            }

            if (parameter.Default is { } value)
            {
                schema["default"] = value;
            }

            properties[parameter.Name] = schema;
        }

        var inputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false
        };

        if (required.Count > 0)
        {
            inputSchema["required"] = required;
        }

        return inputSchema;
    }

    private static JsonObject SchemaOf(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type != typeof(string) && type.IsArray)
            return new JsonObject { ["type"] = "array", ["items"] = SchemaOf(type.GetElementType()!) };

        if (type == typeof(bool))
            return new JsonObject { ["type"] = "boolean" };

        if (type == typeof(int) || type == typeof(long))
            return new JsonObject { ["type"] = "integer" };

        if (type == typeof(Guid))
            return new JsonObject { ["type"] = "string", ["format"] = "uuid" };

        if (type.IsEnum)
        {
            var names = new JsonArray([.. Enum.GetNames(type).Select(name => JsonValue.Create(JsonNamingPolicy.CamelCase.ConvertName(name)))]);
            return new JsonObject { ["type"] = "string", ["enum"] = names };
        }

        return new JsonObject { ["type"] = "string" };
    }

    private static string Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    private sealed class Parameter(Symbol symbol)
    {
        public Symbol Symbol { get; } = symbol;

        // --include-unchanged is include_unchanged: what tool parameters are usually called
        public string Name { get; } = symbol.Name.TrimStart('-').Replace('-', '_');

        public bool IsDb => Symbol is Option { Name: "--db" };

        public Type ValueType => Symbol switch
        {
            Argument argument => argument.ValueType,
            Option option => option.ValueType,
            _ => typeof(string)
        };

        public bool IsFlag => Symbol is Option && ValueType == typeof(bool);

        public bool IsRequired => Symbol switch
        {
            Argument argument => argument.Arity.MinimumNumberOfValues > 0,
            Option option => option.Required,
            _ => false
        };

        public JsonNode? Default
        {
            get
            {
                var value = Symbol switch
                {
                    Argument { HasDefaultValue: true } argument => argument.GetDefaultValue(),
                    Option { HasDefaultValue: true } option => option.GetDefaultValue(),
                    _ => null
                };

                // A flag that is off by default says nothing a boolean does not
                return value is null or false ? null : JsonSerializer.SerializeToNode(value, Json.Options);
            }
        }
    }
}
