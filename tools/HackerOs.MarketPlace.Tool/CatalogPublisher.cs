using HackerOs.App.Abstractions;
using HackerOs.Platform.Core.Apps.Packages;

namespace HackerOs.MarketPlace;

/// <summary>
/// Updates the content of the <c>packages</c> branch after a merge (ADR 0188 §6): copies each
/// newly built package and its assets, rewrites <c>catalog.json</c>, and keeps only the newest
/// versions of each app. Nothing is written when one folder fails its checks.
/// </summary>
public static class CatalogPublisher
{
    /// <summary>Publishes built folders into a checkout of the <c>packages</c> branch.</summary>
    /// <param name="catalogRoot">Checkout of <c>main</c> (folders and <c>categories.json</c>).</param>
    /// <param name="builtRoot">
    /// One sub-folder per app folder name, holding the <c>.hackapp</c> our CI built. Missing or empty
    /// when only <c>categories.json</c> changed: the catalog is then rewritten with the new labels.
    /// </param>
    /// <param name="outputRoot">Checkout of the <c>packages</c> branch, updated in place.</param>
    /// <param name="keptVersions">Versions of each app kept downloadable.</param>
    /// <param name="publishedAt">Publication time written for the new versions.</param>
    /// <param name="errors">Receives every problem found.</param>
    /// <returns>The names of the folders published.</returns>
    public static IReadOnlyList<string> Publish(string catalogRoot, string builtRoot, string outputRoot, int keptVersions, DateTimeOffset publishedAt, List<CatalogError> errors)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keptVersions, 1);
        CategoryList categories = CategoryList.Load(Path.Combine(catalogRoot, CatalogPaths.CategoriesFile), CatalogPaths.CategoriesFile, errors);
        List<(AppFolder Folder, byte[] Bytes, AppPackage Package)> ready = [];
        IEnumerable<string> builtFolders = Directory.Exists(builtRoot) ? Directory.EnumerateDirectories(builtRoot) : [];
        foreach (string built in builtFolders.Order(StringComparer.Ordinal))
        {
            (AppFolder Folder, byte[] Bytes, AppPackage Package)? item = Prepare(catalogRoot, built, categories, errors);
            if (item is not null)
            {
                ready.Add(item.Value);
            }
        }

        if (errors.Count > 0)
        {
            return [];
        }

        string catalogPath = Path.Combine(outputRoot, CatalogDocument.FileName);
        CatalogDocument document = File.Exists(catalogPath) ? CatalogDocument.Read(File.ReadAllText(catalogPath)) : CatalogDocument.Empty;
        Dictionary<string, CatalogApp> apps = document.Apps.ToDictionary(app => app.AppId, StringComparer.Ordinal);
        foreach ((AppFolder folder, byte[] bytes, AppPackage _) in ready)
        {
            CatalogVersion version = CopyFiles(folder, bytes, outputRoot, publishedAt);
            apps[folder.AppId] = Merge(apps.GetValueOrDefault(folder.AppId), folder, version);
        }

        List<CatalogApp> pruned = [.. apps.Values.OrderBy(app => app.AppId, StringComparer.Ordinal).Select(app => Prune(app, outputRoot, keptVersions))];
        Directory.CreateDirectory(outputRoot);
        File.WriteAllText(catalogPath, new CatalogDocument(CatalogDocument.CurrentSchemaVersion, categories.Categories, pruned).Write());
        return [.. ready.Select(item => item.Folder.Name)];
    }

    private static (AppFolder, byte[], AppPackage)? Prepare(string catalogRoot, string built, CategoryList categories, List<CatalogError> errors)
    {
        string name = Path.GetFileName(built);
        string folderPath = Path.Combine(catalogRoot, CatalogPaths.AppsFolder, name);
        if (!Directory.Exists(folderPath))
        {
            errors.Add(new CatalogError($"{CatalogPaths.AppsFolder}/{name}", "A package was built for a folder that does not exist on main."));
            return null;
        }

        AppFolder? folder = AppFolderValidator.Validate(folderPath, categories, errors);
        string[] packages = Directory.GetFiles(built, "*" + AppPackage.Extension, SearchOption.AllDirectories);
        if (packages.Length != 1)
        {
            errors.Add(new CatalogError($"{CatalogPaths.AppsFolder}/{name}", $"Expected one built {AppPackage.Extension} package, found {packages.Length}."));
            return null;
        }

        if (folder is null)
        {
            return null;
        }

        byte[] bytes = File.ReadAllBytes(packages[0]);
        AppPackage? package = PackageCheck.Check(folder, bytes, errors);
        return package is null ? null : (folder, bytes, package);
    }

    private static CatalogVersion CopyFiles(AppFolder folder, byte[] packageBytes, string outputRoot, DateTimeOffset publishedAt)
    {
        string version = folder.Version.ToString();
        string packagePath = $"packages/{folder.AppId}/{version}/{folder.AppId}-{version}{AppPackage.Extension}";
        string assetsPath = $"assets/{folder.AppId}/{version}";
        WriteFile(outputRoot, packagePath, packageBytes);

        string icon = $"{assetsPath}/{CatalogPaths.IconFile}";
        WriteFile(outputRoot, icon, File.ReadAllBytes(Path.Combine(folder.Path, CatalogPaths.IconFile)));
        List<string> screenshots = [];
        foreach (string screenshot in folder.Screenshots)
        {
            string target = $"{assetsPath}/{CatalogPaths.ScreenshotsFolder}/{screenshot}";
            WriteFile(outputRoot, target, File.ReadAllBytes(Path.Combine(folder.Path, CatalogPaths.ScreenshotsFolder, screenshot)));
            screenshots.Add(target);
        }

        Dictionary<string, string> descriptions = new(StringComparer.Ordinal);
        foreach ((string language, string fileName) in folder.DescriptionFiles.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string target = $"{assetsPath}/{fileName}";
            WriteFile(outputRoot, target, File.ReadAllBytes(Path.Combine(folder.Path, fileName)));
            descriptions[language] = target;
        }

        AppSdkCompatibilityManifest sdk = folder.Manifest.SdkCompatibility;
        return new CatalogVersion(
            version,
            folder.Store.Commit,
            packagePath,
            AppPackage.Sha256(packageBytes),
            packageBytes.LongLength,
            publishedAt,
            [.. folder.Manifest.Capabilities.Order(StringComparer.Ordinal)],
            new CatalogSdkRange(sdk.MinimumVersion, sdk.MaximumVersion),
            icon,
            screenshots,
            descriptions);
    }

    private static CatalogApp Merge(CatalogApp? existing, AppFolder folder, CatalogVersion version)
    {
        List<CatalogVersion> versions = [.. (existing?.Versions ?? []).Where(v => v.Version != version.Version), version];
        versions.Sort((left, right) => SemanticVersion.Parse(right.Version).CompareTo(SemanticVersion.Parse(left.Version)));
        bool newest = versions[0] == version;
        if (existing is not null && !newest)
        {
            // An older version published late does not change how the app is listed.
            return existing with { Versions = versions };
        }

        return new CatalogApp(
            folder.AppId,
            "app",
            folder.Manifest.Name,
            folder.Store.Category,
            folder.Store.Tags,
            new SortedDictionary<string, string>(folder.ShortDescriptions.ToDictionary(), StringComparer.Ordinal),
            folder.Store.License,
            folder.Store.Repository,
            version.Version,
            versions);
    }

    private static CatalogApp Prune(CatalogApp app, string outputRoot, int keptVersions)
    {
        foreach (CatalogVersion removed in app.Versions.Skip(keptVersions))
        {
            // Older versions leave the packages branch; their folder stays in the history of main.
            DeleteDirectory(Path.Combine(outputRoot, "packages", app.AppId, removed.Version));
            DeleteDirectory(Path.Combine(outputRoot, "assets", app.AppId, removed.Version));
        }

        return app with { Versions = [.. app.Versions.Take(keptVersions)] };
    }

    private static void WriteFile(string root, string relativePath, byte[] bytes)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
