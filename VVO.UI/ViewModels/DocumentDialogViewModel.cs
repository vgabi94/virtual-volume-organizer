using System.IO;

namespace VVO.UI.ViewModels;

/// <summary>
/// The third-party notices, read from the copy embedded in the assembly. Keeping them inside the
/// assembly is what lets a published single file carry the attributions it is obliged to
/// distribute, with no second file to lose on the way to a release.
/// </summary>
public class NoticesDialogViewModel
{
    public const string ResourceName = "THIRD-PARTY-NOTICES.txt";

    public string Notices { get; }

    public NoticesDialogViewModel()
    {
        using var stream = typeof(NoticesDialogViewModel).Assembly
            .GetManifestResourceStream(ResourceName);

        Notices = stream == null
            ? $"{ResourceName} is missing from this build. "
              + "It is published with the source at the project repository."
            : new StreamReader(stream).ReadToEnd();
    }
}
