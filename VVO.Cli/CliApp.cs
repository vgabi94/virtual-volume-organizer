using System.CommandLine;

namespace VVO.Cli;

/// <summary>
/// The whole command tree, built in one place so the tests can run it in-process.
/// </summary>
public static class CliApp
{
    public static RootCommand BuildRoot(IServiceProvider services)
    {
        return new RootCommand(
            "Virtual Volume Organizer command line. Reads and edits .vvo catalogues; "
            + "output is JSON on stdout.");
    }
}
