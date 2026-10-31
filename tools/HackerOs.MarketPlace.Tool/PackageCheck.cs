using System.Text.Json.Nodes;
using HackerOs.Platform.Core.Apps.Packages;

namespace HackerOs.MarketPlace;

/// <summary>
/// Checks the package our CI built from the app's commit against its catalog folder
/// (ADR 0188 §5 step 4): the package must read as the OS reads it, and its manifest must be the
/// one the author put in the folder, so what the Store shows is what gets installed.
/// </summary>
public static class PackageCheck
{
    /// <summary>Reads a package and compares it with its folder.</summary>
    /// <param name="folder">The checked app folder.</param>
    /// <param name="packageBytes">The <c>.hackapp</c> built by the CI.</param>
    /// <param name="errors">Receives every problem found.</param>
    /// <returns>The verified package, or <see langword="null"/> when it is refused.</returns>
    public static AppPackage? Check(AppFolder folder, byte[] packageBytes, List<CatalogError> errors)
    {
        string display = $"{CatalogPaths.AppsFolder}/{folder.Name}";
        AppPackage package;
        try
        {
            package = AppPackage.Read(packageBytes);
        }
        catch (AppPackageException exception)
        {
            errors.Add(new CatalogError(display, $"The package built from {folder.Store.Commit} is refused ({exception.Code}): {exception.Message}"));
            return null;
        }

        JsonNode? built = JsonNode.Parse(package.Files[AppPackage.ManifestFileName]);
        JsonNode? submitted = JsonNode.Parse(File.ReadAllBytes(Path.Combine(folder.Path, CatalogPaths.ManifestFile)));
        if (!JsonNode.DeepEquals(built, submitted))
        {
            errors.Add(new CatalogError(
                $"{display}/{CatalogPaths.ManifestFile}",
                $"The manifest generated at commit {folder.Store.Commit} differs from the one in the folder: copy the manifest.json of that build."));
            return null;
        }

        return package;
    }
}
