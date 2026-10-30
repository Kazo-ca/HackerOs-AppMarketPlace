namespace HackerOs.MarketPlace;

/// <summary>One problem found in the catalog, reported against a file or folder.</summary>
/// <param name="Path">The file or folder, relative to the catalog root when possible.</param>
/// <param name="Message">What is wrong, in English, as the PR author reads it.</param>
public sealed record CatalogError(string Path, string Message)
{
    /// <summary>
    /// Formats the error for the console. Inside GitHub Actions it becomes an <c>::error</c>
    /// annotation, so it shows on the PR's file view; elsewhere it is a plain line.
    /// </summary>
    public string Format(bool githubActions) =>
        githubActions
            ? $"::error file={Path}::{Message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)}"
            : $"error {Path}: {Message}";
}
