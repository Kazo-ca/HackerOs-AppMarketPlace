namespace HackerOs.MarketPlace.Tests;

public sealed class CategoryListTests : IDisposable
{
    private readonly TestCatalog catalog = new();

    public void Dispose() => catalog.Dispose();

    [Fact]
    public void The_categories_file_of_this_repository_is_valid()
    {
        string path = Path.Combine(RepositoryRoot(), "categories.json");
        List<CatalogError> errors = [];

        CategoryList list = CategoryList.Load(path, "categories.json", errors);

        Assert.Empty(errors);
        Assert.NotEmpty(list.Categories);
    }

    [Theory]
    [InlineData("""[{ "id": "tools", "labels": { "en": "Tools" } }]""", "no 'fr' label")]
    [InlineData("""[{ "id": "tools", "labels": { "en": "Tools", "fr": "Outils" } }, { "id": "tools", "labels": { "en": "Tools", "fr": "Outils" } }]""", "listed twice")]
    [InlineData("""[{ "id": "Tools!", "labels": { "en": "Tools", "fr": "Outils" } }]""", "A category id must match")]
    [InlineData("""[{ "id": "tools", "label": { "en": "Tools", "fr": "Outils" } }]""", "Unknown property 'label'")]
    [InlineData("""{ "tools": "Tools" }""", "must be a JSON array")]
    public void Invalid_categories_are_reported(string json, string expected)
    {
        string path = Path.Combine(catalog.Root, "categories.json");
        File.WriteAllText(path, json);
        List<CatalogError> errors = [];

        CategoryList.Load(path, "categories.json", errors);

        Assert.Contains(errors, error => error.Message.Contains(expected, StringComparison.Ordinal));
    }

    internal static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "categories.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
