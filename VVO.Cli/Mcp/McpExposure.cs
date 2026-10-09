using System.CommandLine;
using System.Runtime.CompilerServices;

namespace VVO.Cli.Mcp;

public static class McpExposure
{
    private static readonly ConditionalWeakTable<Symbol, object> CliOnlySymbols = new();

    /// <summary>
    /// Keeps a command or option out of the MCP tools, for what only means something at a shell.
    /// </summary>
    public static T CliOnly<T>(this T symbol) where T : Symbol
    {
        CliOnlySymbols.AddOrUpdate(symbol, symbol);
        return symbol;
    }

    public static bool IsCliOnly(this Symbol symbol) => CliOnlySymbols.TryGetValue(symbol, out _);
}
