using System.CommandLine;
using System.Reflection;

namespace VVO.Cli.Commands;

/// <summary>
/// The command line's About and License dialogs.
/// </summary>
public static class AboutCommand
{
    public const string LicenseResource = "LICENSE";
    public const string NoticesResource = "THIRD-PARTY-NOTICES.txt";

    public static Command Create(IServiceProvider services)
    {
        var command = new Command("about", "Show the version, the license, and the notices of the projects vvo uses.");

        command.SetJsonAction(services, _ =>
        {
            var assembly = typeof(AboutCommand).Assembly;

            return Task.FromResult<object?>(new
            {
                Product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product,
                Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                License = Read(assembly, LicenseResource),
                ThirdPartyNotices = Read(assembly, NoticesResource)
            });
        });

        return command;
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"'{resource}' was not built into vvo.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
