using HackerOs.App.Abstractions;

namespace HackerOs.MarketPlace;

/// <summary>A checked app version folder <c>apps/&lt;appId&gt;_&lt;version&gt;/</c>.</summary>
/// <param name="Path">Absolute path of the folder.</param>
/// <param name="Name">Folder name, <c>&lt;appId&gt;_&lt;version&gt;</c>.</param>
/// <param name="Manifest">The app manifest, read and validated as the OS does.</param>
/// <param name="Version">The version, from the folder name and the manifest.</param>
/// <param name="Store">The content of <c>store.json</c>.</param>
/// <param name="ShortDescriptions">Short description by language code.</param>
/// <param name="DescriptionFiles">Long description file name by language code.</param>
/// <param name="Screenshots">Screenshot file names, in order.</param>
public sealed record AppFolder(
    string Path,
    string Name,
    AppManifest Manifest,
    SemanticVersion Version,
    StoreEntry Store,
    IReadOnlyDictionary<string, string> ShortDescriptions,
    IReadOnlyDictionary<string, string> DescriptionFiles,
    IReadOnlyList<string> Screenshots)
{
    /// <summary>Gets the app identifier.</summary>
    public string AppId => Manifest.Id;
}
