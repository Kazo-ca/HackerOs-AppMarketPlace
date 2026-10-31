using System.Text.Json;
using System.Text.Json.Serialization;

namespace HackerOs.MarketPlace;

/// <summary>
/// <c>catalog.json</c> at the root of the <c>packages</c> branch (ADR 0188 §6): what the Hack Store
/// and <c>hackpkg</c> read, through <c>raw.githubusercontent.com</c>. Every path is relative to the
/// branch root.
/// </summary>
/// <param name="SchemaVersion">Format version of this file; readers refuse a higher one.</param>
/// <param name="Categories">The closed list of categories with their labels, copied from <c>categories.json</c>.</param>
/// <param name="Apps">Apps sorted by id.</param>
public sealed record CatalogDocument(int SchemaVersion, IReadOnlyList<CatalogCategory> Categories, IReadOnlyList<CatalogApp> Apps)
{
    /// <summary>Version of the format written by this tool.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>File name at the root of the <c>packages</c> branch.</summary>
    public const string FileName = "catalog.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Gets an empty catalog.</summary>
    public static CatalogDocument Empty { get; } = new(CurrentSchemaVersion, [], []);

    /// <summary>Reads a catalog written by this tool.</summary>
    /// <exception cref="InvalidDataException">The file is not a catalog this tool can update.</exception>
    public static CatalogDocument Read(string json)
    {
        CatalogDocument document = JsonSerializer.Deserialize<CatalogDocument>(json, Options)
            ?? throw new InvalidDataException("catalog.json is empty.");
        return document.SchemaVersion == CurrentSchemaVersion
            ? document
            : throw new InvalidDataException($"catalog.json has schema version {document.SchemaVersion}; this tool writes {CurrentSchemaVersion}.");
    }

    /// <summary>Writes the catalog as indented JSON with a final new line.</summary>
    public string Write() => JsonSerializer.Serialize(this, Options) + "\n";
}

/// <summary>One app of the catalog. Its listing fields come from its newest published version.</summary>
/// <param name="AppId">App identifier.</param>
/// <param name="Kind"><c>app</c>, or <c>theme-pack</c> later (ADR 0190).</param>
/// <param name="Name">Display name from the manifest.</param>
/// <param name="Category">Category id.</param>
/// <param name="Tags">Search tags.</param>
/// <param name="ShortDescriptions">Short description by language code.</param>
/// <param name="License">SPDX license expression.</param>
/// <param name="Repository">Source repository.</param>
/// <param name="LatestVersion">Newest published version.</param>
/// <param name="Versions">Published versions, newest first, at most the kept count.</param>
public sealed record CatalogApp(
    string AppId,
    string Kind,
    string Name,
    string Category,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> ShortDescriptions,
    string License,
    string Repository,
    string LatestVersion,
    IReadOnlyList<CatalogVersion> Versions);

/// <summary>One published version of an app.</summary>
/// <param name="Version">SemVer of the version.</param>
/// <param name="Commit">Commit our CI built.</param>
/// <param name="Package">Path of the <c>.hackapp</c>.</param>
/// <param name="Sha256">SHA-256 of the package, lower-case hex.</param>
/// <param name="Size">Size of the package in bytes.</param>
/// <param name="PublishedAt">When the package was published (UTC, ISO 8601).</param>
/// <param name="Capabilities">Capabilities the version asks for, so the Store can show what an update adds.</param>
/// <param name="SdkCompatibility">SDK range of the version.</param>
/// <param name="Icon">Path of the icon.</param>
/// <param name="Screenshots">Paths of the screenshots, in order.</param>
/// <param name="Descriptions">Path of the long description by language code.</param>
/// <param name="Signature">
/// ECDSA P-256 signature of the package, base64 (ADR 0214 §2 of Kazo-ca/HackerOs); absent when the
/// catalog had no signing key at publication.
/// </param>
public sealed record CatalogVersion(
    string Version,
    string Commit,
    string Package,
    string Sha256,
    long Size,
    DateTimeOffset PublishedAt,
    IReadOnlyList<string> Capabilities,
    CatalogSdkRange SdkCompatibility,
    string Icon,
    IReadOnlyList<string> Screenshots,
    IReadOnlyDictionary<string, string> Descriptions,
    string? Signature = null);

/// <summary>SDK range of a version, as in its manifest.</summary>
/// <param name="MinimumVersion">Lowest SDK version.</param>
/// <param name="MaximumVersion">Highest SDK version, when the app sets one.</param>
public sealed record CatalogSdkRange(string MinimumVersion, string? MaximumVersion);
