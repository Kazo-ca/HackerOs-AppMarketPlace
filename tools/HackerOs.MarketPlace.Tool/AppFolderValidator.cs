using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HackerOs.App.Abstractions;

namespace HackerOs.MarketPlace;

/// <summary>
/// Checks one app version folder against the catalog format (ADR 0188 §4, §5 steps 1 and 2):
/// names, sizes, icon dimensions, descriptions, categories, and the manifest with the same reader
/// and validator as the OS. Any file the format does not name is refused, so no binary can be
/// slipped into <c>main</c>.
/// </summary>
public static partial class AppFolderValidator
{
    /// <summary>Checks a folder.</summary>
    /// <param name="folder">Absolute path of the folder.</param>
    /// <param name="categories">The closed list of categories.</param>
    /// <param name="errors">Receives every problem found.</param>
    /// <returns>The folder when it is valid, otherwise <see langword="null"/>.</returns>
    public static AppFolder? Validate(string folder, CategoryList categories, List<CatalogError> errors)
    {
        int before = errors.Count;
        string name = System.IO.Path.GetFileName(folder.TrimEnd('/', '\\'));
        string display = $"{CatalogPaths.AppsFolder}/{name}";

        if (!TrySplitName(name, out string folderAppId, out SemanticVersion folderVersion))
        {
            errors.Add(new CatalogError(display, "The folder must be named '<appId>_<version>', e.g. 'org.example.notes_1.2.0'."));
            return null;
        }

        CheckUnknownEntries(folder, display, errors);
        CheckIcon(folder, display, errors);
        Dictionary<string, string> shortDescriptions = ReadShortDescriptions(folder, display, errors);
        Dictionary<string, string> descriptionFiles = CheckDescriptions(folder, display, errors);
        List<string> screenshots = CheckScreenshots(folder, display, errors);
        AppManifest? manifest = ReadManifest(folder, display, folderAppId, folderVersion, errors);
        StoreEntry? store = ReadStore(folder, display, categories, errors);

        return errors.Count == before
            ? new AppFolder(folder, name, manifest!, folderVersion, store!, shortDescriptions, descriptionFiles, screenshots)
            : null;
    }

    /// <summary>Splits a folder name into app id and version.</summary>
    public static bool TrySplitName(string name, out string appId, out SemanticVersion version)
    {
        // An app id never contains '_' (AppManifestValidator), and a SemVer never does either.
        int separator = name.IndexOf('_', StringComparison.Ordinal);
        appId = separator > 0 ? name[..separator] : string.Empty;
        version = default;
        return separator > 0
            && AppIdPattern().IsMatch(appId)
            && SemanticVersion.TryParse(name[(separator + 1)..], out version)
            && version.ToString() == name[(separator + 1)..];
    }

    private static void CheckUnknownEntries(string folder, string display, List<CatalogError> errors)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
        {
            string entryName = System.IO.Path.GetFileName(entry);
            bool known = Directory.Exists(entry)
                ? entryName == CatalogPaths.ScreenshotsFolder
                : entryName is CatalogPaths.IconFile or CatalogPaths.ManifestFile or CatalogPaths.StoreFile
                    || ShortDescriptionPattern().IsMatch(entryName)
                    || DescriptionPattern().IsMatch(entryName);
            if (!known)
            {
                errors.Add(new CatalogError($"{display}/{entryName}", "This file is not part of the catalog format: remove it (binaries are built by our CI, never submitted)."));
            }
        }
    }

    private static void CheckIcon(string folder, string display, List<CatalogError> errors)
    {
        string path = System.IO.Path.Combine(folder, CatalogPaths.IconFile);
        string iconDisplay = $"{display}/{CatalogPaths.IconFile}";
        if (!File.Exists(path))
        {
            errors.Add(new CatalogError(iconDisplay, $"The icon is missing: a {CatalogPaths.IconSize}×{CatalogPaths.IconSize} PNG is required."));
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > CatalogPaths.MaximumIconBytes)
        {
            errors.Add(new CatalogError(iconDisplay, $"The icon is {bytes.Length} bytes; the limit is {CatalogPaths.MaximumIconBytes}."));
        }

        if (!PngImage.TryReadSize(bytes, out int width, out int height))
        {
            errors.Add(new CatalogError(iconDisplay, "The icon is not a PNG image."));
        }
        else if (width != CatalogPaths.IconSize || height != CatalogPaths.IconSize)
        {
            errors.Add(new CatalogError(iconDisplay, $"The icon is {width}×{height}; it must be {CatalogPaths.IconSize}×{CatalogPaths.IconSize}."));
        }
    }

    private static Dictionary<string, string> ReadShortDescriptions(string folder, string display, List<CatalogError> errors)
    {
        Dictionary<string, string> texts = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(folder).Where(path => ShortDescriptionPattern().IsMatch(System.IO.Path.GetFileName(path))))
        {
            string fileName = System.IO.Path.GetFileName(path);
            string language = ShortDescriptionPattern().Match(fileName).Groups["lang"].Value;
            string? text = ReadUtf8(path, $"{display}/{fileName}", errors)?.Trim();
            if (text is null)
            {
                continue;
            }

            if (text.Length == 0 || text.Contains('\n', StringComparison.Ordinal))
            {
                errors.Add(new CatalogError($"{display}/{fileName}", "The short description must be one non-empty line."));
            }
            else if (text.Length > CatalogPaths.MaximumShortDescriptionLength)
            {
                errors.Add(new CatalogError($"{display}/{fileName}", $"The short description has {text.Length} characters; the limit is {CatalogPaths.MaximumShortDescriptionLength}."));
            }
            else
            {
                texts[language] = text;
            }
        }

        if (!File.Exists(System.IO.Path.Combine(folder, $"{CatalogPaths.ShortDescriptionPrefix}{CatalogPaths.RequiredLanguage}.txt")))
        {
            errors.Add(new CatalogError(display, $"'{CatalogPaths.ShortDescriptionPrefix}{CatalogPaths.RequiredLanguage}.txt' is required (English)."));
        }

        return texts;
    }

    private static Dictionary<string, string> CheckDescriptions(string folder, string display, List<CatalogError> errors)
    {
        Dictionary<string, string> files = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(folder).Where(path => DescriptionPattern().IsMatch(System.IO.Path.GetFileName(path))))
        {
            string fileName = System.IO.Path.GetFileName(path);
            string fileDisplay = $"{display}/{fileName}";
            if (new FileInfo(path).Length > CatalogPaths.MaximumDescriptionBytes)
            {
                errors.Add(new CatalogError(fileDisplay, $"The description is larger than {CatalogPaths.MaximumDescriptionBytes} bytes."));
                continue;
            }

            string? text = ReadUtf8(path, fileDisplay, errors);
            if (text is not null && string.IsNullOrWhiteSpace(text))
            {
                errors.Add(new CatalogError(fileDisplay, "The description is empty."));
            }
            else if (text is not null)
            {
                files[DescriptionPattern().Match(fileName).Groups["lang"].Value] = fileName;
            }
        }

        if (!File.Exists(System.IO.Path.Combine(folder, $"{CatalogPaths.DescriptionPrefix}{CatalogPaths.RequiredLanguage}.md")))
        {
            errors.Add(new CatalogError(display, $"'{CatalogPaths.DescriptionPrefix}{CatalogPaths.RequiredLanguage}.md' is required (English)."));
        }

        return files;
    }

    private static List<string> CheckScreenshots(string folder, string display, List<CatalogError> errors)
    {
        string screenshotsFolder = System.IO.Path.Combine(folder, CatalogPaths.ScreenshotsFolder);
        if (!Directory.Exists(screenshotsFolder))
        {
            return [];
        }

        string folderDisplay = $"{display}/{CatalogPaths.ScreenshotsFolder}";
        if (Directory.EnumerateDirectories(screenshotsFolder).Any())
        {
            errors.Add(new CatalogError(folderDisplay, "The screenshots folder may not contain folders."));
        }

        string[] names = [.. Directory.EnumerateFiles(screenshotsFolder).Select(System.IO.Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];
        string[] expected = [.. Enumerable.Range(1, names.Length).Select(index => $"{index}.png")];
        if (names.Length is 0 or > CatalogPaths.MaximumScreenshots || !names.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)))
        {
            errors.Add(new CatalogError(folderDisplay, $"Screenshots must be named 1.png to N.png, with N between 1 and {CatalogPaths.MaximumScreenshots}; found: {string.Join(", ", names)}."));
            return [];
        }

        foreach (string fileName in expected)
        {
            byte[] bytes = File.ReadAllBytes(System.IO.Path.Combine(screenshotsFolder, fileName));
            string fileDisplay = $"{folderDisplay}/{fileName}";
            if (bytes.Length > CatalogPaths.MaximumScreenshotBytes)
            {
                errors.Add(new CatalogError(fileDisplay, $"The screenshot is {bytes.Length} bytes; the limit is {CatalogPaths.MaximumScreenshotBytes}."));
            }

            if (!PngImage.TryReadSize(bytes, out int width, out int height))
            {
                errors.Add(new CatalogError(fileDisplay, "The screenshot is not a PNG image."));
            }
            else if (width > CatalogPaths.MaximumScreenshotSide || height > CatalogPaths.MaximumScreenshotSide)
            {
                errors.Add(new CatalogError(fileDisplay, $"The screenshot is {width}×{height}; each side must be at most {CatalogPaths.MaximumScreenshotSide}."));
            }
        }

        return [.. expected];
    }

    private static AppManifest? ReadManifest(string folder, string display, string folderAppId, SemanticVersion folderVersion, List<CatalogError> errors)
    {
        string path = System.IO.Path.Combine(folder, CatalogPaths.ManifestFile);
        string manifestDisplay = $"{display}/{CatalogPaths.ManifestFile}";
        if (!File.Exists(path))
        {
            errors.Add(new CatalogError(manifestDisplay, "The manifest is missing: copy the manifest.json your build generates."));
            return null;
        }

        AppManifest manifest;
        try
        {
            manifest = AppManifestJsonSerializer.DeserializeStrict(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            errors.Add(new CatalogError(manifestDisplay, $"The manifest cannot be read: {exception.Message}"));
            return null;
        }

        foreach (ManifestValidationError error in AppManifestValidator.Validate(manifest).Errors)
        {
            errors.Add(new CatalogError(manifestDisplay, $"{error.Code} at {error.Path}: {error.Message}"));
        }

        if (manifest.Id != folderAppId)
        {
            errors.Add(new CatalogError(manifestDisplay, $"The manifest id '{manifest.Id}' does not match the folder name ('{folderAppId}')."));
        }

        if (!SemanticVersion.TryParse(manifest.Version, out SemanticVersion version) || version != folderVersion)
        {
            errors.Add(new CatalogError(manifestDisplay, $"The manifest version '{manifest.Version}' does not match the folder name ('{folderVersion}')."));
        }

        return manifest;
    }

    private static StoreEntry? ReadStore(string folder, string display, CategoryList categories, List<CatalogError> errors)
    {
        string path = System.IO.Path.Combine(folder, CatalogPaths.StoreFile);
        string storeDisplay = $"{display}/{CatalogPaths.StoreFile}";
        if (!File.Exists(path))
        {
            errors.Add(new CatalogError(storeDisplay, "store.json is missing (repository, commit, category, tags, license)."));
            return null;
        }

        return StoreEntry.Parse(File.ReadAllText(path), storeDisplay, categories, errors);
    }

    private static string? ReadUtf8(string path, string display, List<CatalogError> errors)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(File.ReadAllBytes(path)).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            errors.Add(new CatalogError(display, "The file is not valid UTF-8."));
            return null;
        }
    }

    // Same rule as AppManifestValidator: at least three dot-separated lower-case segments.
    [GeneratedRegex("^[a-z][a-z0-9-]*(?:\\.[a-z][a-z0-9-]*){2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex AppIdPattern();

    [GeneratedRegex("^short_description\\.(?<lang>[a-z]{2}(?:-[A-Z]{2})?)\\.txt$", RegexOptions.CultureInvariant)]
    private static partial Regex ShortDescriptionPattern();

    [GeneratedRegex("^description\\.(?<lang>[a-z]{2}(?:-[A-Z]{2})?)\\.md$", RegexOptions.CultureInvariant)]
    private static partial Regex DescriptionPattern();
}
