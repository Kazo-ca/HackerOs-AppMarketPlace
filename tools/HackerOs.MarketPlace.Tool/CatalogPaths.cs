namespace HackerOs.MarketPlace;

/// <summary>
/// Names and limits of the catalog format (ADR 0188 §4 of Kazo-ca/HackerOs). Every rule the
/// validator applies is written here once, so the README can quote it.
/// </summary>
public static class CatalogPaths
{
    /// <summary>Folder that holds one sub-folder per app version on <c>main</c>.</summary>
    public const string AppsFolder = "apps";

    /// <summary>Closed list of categories at the catalog root.</summary>
    public const string CategoriesFile = "categories.json";

    /// <summary>Square icon of an app version.</summary>
    public const string IconFile = "icon.png";

    /// <summary>The app's generated manifest (ADR 0047).</summary>
    public const string ManifestFile = "manifest.json";

    /// <summary>Repository, commit, category, tags and license of a version.</summary>
    public const string StoreFile = "store.json";

    /// <summary>Optional folder of screenshots <c>1.png</c> to <c>5.png</c>.</summary>
    public const string ScreenshotsFolder = "screenshots";

    /// <summary>Prefix of a short description file: <c>short_description.&lt;lang&gt;.txt</c>.</summary>
    public const string ShortDescriptionPrefix = "short_description.";

    /// <summary>Prefix of a long description file: <c>description.&lt;lang&gt;.md</c>.</summary>
    public const string DescriptionPrefix = "description.";

    /// <summary>Language every app must describe itself in.</summary>
    public const string RequiredLanguage = "en";

    /// <summary>Side of the square icon, in pixels.</summary>
    public const int IconSize = 256;

    /// <summary>Largest icon file, in bytes (200 KB).</summary>
    public const long MaximumIconBytes = 200 * 1024;

    /// <summary>Longest short description, in characters.</summary>
    public const int MaximumShortDescriptionLength = 120;

    /// <summary>Largest long description file, in bytes (20 KB).</summary>
    public const long MaximumDescriptionBytes = 20 * 1024;

    /// <summary>Most screenshots of a version.</summary>
    public const int MaximumScreenshots = 5;

    /// <summary>Largest screenshot file, in bytes (1 MB).</summary>
    public const long MaximumScreenshotBytes = 1024 * 1024;

    /// <summary>Largest screenshot side, in pixels.</summary>
    public const int MaximumScreenshotSide = 3840;

    /// <summary>Most tags of a version.</summary>
    public const int MaximumTags = 10;

    /// <summary>Versions of each app kept downloadable on the <c>packages</c> branch (ADR 0188 §6).</summary>
    public const int DefaultKeptVersions = 3;
}
