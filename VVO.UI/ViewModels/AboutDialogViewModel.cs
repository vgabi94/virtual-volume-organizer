using System.Reflection;

namespace VVO.UI.ViewModels;

public class AboutDialogViewModel
{
    public string AppName => "Virtual Volume Organizer";

    public string Version { get; }

    public string IconsCredit =>
        "Icons come from MahApps.Metro.IconPacks (MIT) and the icon sets it bundles.";

    public AboutDialogViewModel()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        // The informational version carries whatever the build stamped on it; the assembly
        // version is the four-part fallback that is always there
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // The SDK appends the source revision after a '+', which is noise on screen
        var plus = informational?.IndexOf('+') ?? -1;
        if (plus >= 0)
        {
            informational = informational![..plus];
        }

        Version = informational ?? assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
