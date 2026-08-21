using System.IO;

namespace VVO.UI.ViewModels;

/// <summary>
/// A document the application ships with, read from the copy embedded in the assembly. Keeping
/// them inside the assembly is what lets a published single file carry the licence and the
/// attributions it is obliged to distribute, with no second file to lose on the way to a release.
/// </summary>
public class DocumentDialogViewModel
{
    public const string NoticesResource = "THIRD-PARTY-NOTICES.txt";
    public const string LicenseResource = "LICENSE";

    public string Title { get; }
    public string Text { get; }

    public static DocumentDialogViewModel Notices() => new("Third-Party Notices", NoticesResource);

    public static DocumentDialogViewModel License() => new("License", LicenseResource);

    public DocumentDialogViewModel(string title, string resourceName)
    {
        Title = title;

        using var stream = typeof(DocumentDialogViewModel).Assembly
            .GetManifestResourceStream(resourceName);

        Text = stream == null
            ? $"{resourceName} is missing from this build. "
              + "It is published with the source at the project repository."
            : new StreamReader(stream).ReadToEnd();
    }
}
