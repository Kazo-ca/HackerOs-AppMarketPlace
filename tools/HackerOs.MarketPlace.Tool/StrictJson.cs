using System.Text.Json;

namespace HackerOs.MarketPlace;

/// <summary>
/// Small helpers to read hand-written JSON strictly: an unknown or duplicate property is an
/// error, as in the HackerOS manifest reader, so a typo never passes silently.
/// </summary>
internal static class StrictJson
{
    /// <summary>Checks that <paramref name="element"/> is an object with only allowed, unique properties.</summary>
    public static bool HasOnly(JsonElement element, IReadOnlyCollection<string> allowed, string displayPath, string what, List<CatalogError> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new CatalogError(displayPath, $"Each {what} must be a JSON object."));
            return false;
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        bool valid = true;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                errors.Add(new CatalogError(displayPath, $"The property '{property.Name}' of a {what} appears twice."));
                valid = false;
            }
            else if (!allowed.Contains(property.Name))
            {
                errors.Add(new CatalogError(displayPath, $"Unknown property '{property.Name}' in a {what}; allowed: {string.Join(", ", allowed)}."));
                valid = false;
            }
        }

        return valid;
    }

    /// <summary>Gets a string property, or <see langword="null"/> when it is missing or not a string.</summary>
    public static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
