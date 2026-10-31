using System.Text.Json;
using System.Text.RegularExpressions;

namespace HackerOs.MarketPlace;

/// <summary>A category of the closed list, with its label in each language.</summary>
/// <param name="Id">Stable identifier used by <c>store.json</c>, e.g. <c>productivity</c>.</param>
/// <param name="Labels">Label by language code; English and French are required.</param>
public sealed record CatalogCategory(string Id, IReadOnlyDictionary<string, string> Labels);

/// <summary>
/// The closed list of categories (<c>categories.json</c>, ADR 0188 §4): a JSON array of
/// <c>{ "id": "...", "labels": { "en": "...", "fr": "..." } }</c>. A new category is added by a PR
/// to this file, reviewed like an app.
/// </summary>
public sealed partial class CategoryList
{
    private static readonly string[] RequiredLabelLanguages = ["en", "fr"];

    private CategoryList(IReadOnlyList<CatalogCategory> categories) => Categories = categories;

    /// <summary>Gets the categories in file order.</summary>
    public IReadOnlyList<CatalogCategory> Categories { get; }

    /// <summary>Gets whether <paramref name="id"/> names a category of the list.</summary>
    public bool Contains(string id) => Categories.Any(category => category.Id == id);

    /// <summary>Reads and checks <c>categories.json</c>.</summary>
    /// <param name="path">Path of the file.</param>
    /// <param name="displayPath">Path shown in the errors.</param>
    /// <param name="errors">Receives every problem found.</param>
    /// <returns>The list, empty when the file cannot be read.</returns>
    public static CategoryList Load(string path, string displayPath, List<CatalogError> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add(new CatalogError(displayPath, "The categories file is missing."));
            return new CategoryList([]);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            return Parse(document.RootElement, displayPath, errors);
        }
        catch (JsonException exception)
        {
            errors.Add(new CatalogError(displayPath, $"Invalid JSON: {exception.Message}"));
            return new CategoryList([]);
        }
    }

    private static CategoryList Parse(JsonElement root, string displayPath, List<CatalogError> errors)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new CatalogError(displayPath, "The categories file must be a JSON array."));
            return new CategoryList([]);
        }

        List<CatalogCategory> categories = [];
        foreach (JsonElement item in root.EnumerateArray())
        {
            CatalogCategory? category = ParseCategory(item, displayPath, errors);
            if (category is null)
            {
                continue;
            }

            if (categories.Any(existing => existing.Id == category.Id))
            {
                errors.Add(new CatalogError(displayPath, $"The category '{category.Id}' is listed twice."));
                continue;
            }

            categories.Add(category);
        }

        return new CategoryList(categories);
    }

    private static CatalogCategory? ParseCategory(JsonElement item, string displayPath, List<CatalogError> errors)
    {
        if (!StrictJson.HasOnly(item, ["id", "labels"], displayPath, "category", errors))
        {
            return null;
        }

        string? id = StrictJson.GetString(item, "id");
        if (id is null || !IdPattern().IsMatch(id))
        {
            errors.Add(new CatalogError(displayPath, $"A category id must match {IdPattern()}, got '{id}'."));
            return null;
        }

        if (!item.TryGetProperty("labels", out JsonElement labelsElement) || labelsElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new CatalogError(displayPath, $"The category '{id}' needs a 'labels' object."));
            return null;
        }

        Dictionary<string, string> labels = new(StringComparer.Ordinal);
        foreach (JsonProperty label in labelsElement.EnumerateObject())
        {
            if (label.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(label.Value.GetString()))
            {
                errors.Add(new CatalogError(displayPath, $"The label '{label.Name}' of category '{id}' must be a non-empty string."));
                return null;
            }

            labels[label.Name] = label.Value.GetString()!;
        }

        foreach (string language in RequiredLabelLanguages.Where(language => !labels.ContainsKey(language)))
        {
            errors.Add(new CatalogError(displayPath, $"The category '{id}' has no '{language}' label."));
            return null;
        }

        return new CatalogCategory(id, labels);
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
