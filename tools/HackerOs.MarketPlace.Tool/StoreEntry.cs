using System.Text.Json;
using System.Text.RegularExpressions;

namespace HackerOs.MarketPlace;

/// <summary>
/// The content of <c>store.json</c> (ADR 0188 §4): where the code of this version is and how the
/// Store files it.
/// </summary>
/// <param name="Repository">Public GitHub repository of the app, <c>https://github.com/owner/repo</c>.</param>
/// <param name="Commit">Full 40-character SHA-1 of the commit our CI builds.</param>
/// <param name="Category">Category id from <c>categories.json</c>.</param>
/// <param name="Tags">Free search tags, lower case.</param>
/// <param name="License">SPDX license expression, e.g. <c>MIT</c> or <c>GPL-3.0-only</c>.</param>
/// <param name="Project">
/// Optional path of the app's <c>.csproj</c> inside its repository. Without it, the CI builds the
/// repository root, which must then hold a single project or solution.
/// </param>
public sealed partial record StoreEntry(string Repository, string Commit, string Category, IReadOnlyList<string> Tags, string License, string? Project)
{
    private static readonly string[] AllowedProperties = ["repository", "commit", "category", "tags", "license", "project", "kind"];

    /// <summary>Reads and checks a <c>store.json</c> file.</summary>
    /// <param name="json">The file content.</param>
    /// <param name="displayPath">Path shown in the errors.</param>
    /// <param name="categories">The closed list of categories.</param>
    /// <param name="errors">Receives every problem found.</param>
    /// <returns>The entry, or <see langword="null"/> when it is not valid.</returns>
    public static StoreEntry? Parse(string json, string displayPath, CategoryList categories, List<CatalogError> errors)
    {
        int before = errors.Count;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            errors.Add(new CatalogError(displayPath, $"Invalid JSON: {exception.Message}"));
            return null;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (!StrictJson.HasOnly(root, AllowedProperties, displayPath, "store.json", errors))
            {
                return null;
            }

            string? kind = StrictJson.GetString(root, "kind");
            if (root.TryGetProperty("kind", out _) && kind != "app")
            {
                // Theme packs (ADR 0190) get their own checks with their work item; until then only apps.
                errors.Add(new CatalogError(displayPath, $"'kind' may only be \"app\" for now (theme packs come with ADR 0190), got '{kind}'."));
            }

            string? repository = Require(root, "repository", RepositoryPattern(), "https://github.com/owner/repo", displayPath, errors);
            string? commit = Require(root, "commit", CommitPattern(), "the full 40-character commit SHA in lower case", displayPath, errors);
            string? license = Require(root, "license", LicensePattern(), "an SPDX license expression such as MIT", displayPath, errors);
            string? category = StrictJson.GetString(root, "category");
            if (category is null || !categories.Contains(category))
            {
                errors.Add(new CatalogError(displayPath, $"'category' must be one of: {string.Join(", ", categories.Categories.Select(c => c.Id))}; got '{category}'."));
            }

            List<string> tags = ReadTags(root, displayPath, errors);
            string? project = ReadProject(root, displayPath, errors);

            return errors.Count == before
                ? new StoreEntry(repository!, commit!, category!, tags, license!, project)
                : null;
        }
    }

    /// <summary>Gets the <c>owner/repo</c> part of <see cref="Repository"/>.</summary>
    public string RepositorySlug => Repository["https://github.com/".Length..];

    private static string? Require(JsonElement root, string name, Regex pattern, string expected, string displayPath, List<CatalogError> errors)
    {
        string? value = StrictJson.GetString(root, name);
        if (value is null || !pattern.IsMatch(value))
        {
            errors.Add(new CatalogError(displayPath, $"'{name}' must be {expected}; got '{value}'."));
            return null;
        }

        return value;
    }

    private static List<string> ReadTags(JsonElement root, string displayPath, List<CatalogError> errors)
    {
        List<string> tags = [];
        if (!root.TryGetProperty("tags", out JsonElement element))
        {
            return tags;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new CatalogError(displayPath, "'tags' must be an array of strings."));
            return tags;
        }

        foreach (JsonElement tag in element.EnumerateArray())
        {
            string? value = tag.ValueKind == JsonValueKind.String ? tag.GetString() : null;
            if (value is null || !TagPattern().IsMatch(value))
            {
                errors.Add(new CatalogError(displayPath, $"Each tag must match {TagPattern()}; got '{value ?? tag.GetRawText()}'."));
            }
            else if (tags.Contains(value))
            {
                errors.Add(new CatalogError(displayPath, $"The tag '{value}' is listed twice."));
            }
            else
            {
                tags.Add(value);
            }
        }

        if (tags.Count > CatalogPaths.MaximumTags)
        {
            errors.Add(new CatalogError(displayPath, $"At most {CatalogPaths.MaximumTags} tags are allowed; got {tags.Count}."));
        }

        return tags;
    }

    private static string? ReadProject(JsonElement root, string displayPath, List<CatalogError> errors)
    {
        if (!root.TryGetProperty("project", out _))
        {
            return null;
        }

        // The path reaches a shell command of the CI: a conservative character set and no "..".
        string? project = StrictJson.GetString(root, "project");
        if (project is null || !ProjectPattern().IsMatch(project) || project.Split('/').Contains(".."))
        {
            errors.Add(new CatalogError(displayPath, $"'project' must be a relative path to a .csproj (letters, digits, '.', '_', '-', '/'); got '{project}'."));
            return null;
        }

        return project;
    }

    [GeneratedRegex("^https://github\\.com/[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^[A-Za-z0-9.+-]+(?: (?:AND|OR|WITH) [A-Za-z0-9.+-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex LicensePattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,29}$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9._/-]*\\.csproj$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectPattern();
}
