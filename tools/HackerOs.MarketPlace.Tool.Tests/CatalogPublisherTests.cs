using System.Text.Json.Nodes;

namespace HackerOs.MarketPlace.Tests;

public sealed class CatalogPublisherTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private readonly TestCatalog catalog = new();

    public void Dispose() => catalog.Dispose();

    private string Built => Path.Combine(catalog.Root, "built");

    private string Output => Path.Combine(catalog.Root, "packages-branch");

    [Fact]
    public void A_first_version_writes_the_package_its_assets_and_catalog_json()
    {
        Build(catalog.AddApp(capabilities: ["clipboard.read"]));

        List<CatalogError> errors = [];
        IReadOnlyList<string> published = CatalogPublisher.Publish(catalog.Root, Built, Output, 3, Now, errors);

        Assert.Empty(errors);
        Assert.Equal(["org.example.notes_1.2.0"], published);
        Assert.True(File.Exists(Path.Combine(Output, "packages/org.example.notes/1.2.0/org.example.notes-1.2.0.hackapp")));
        Assert.True(File.Exists(Path.Combine(Output, "assets/org.example.notes/1.2.0/icon.png")));
        Assert.True(File.Exists(Path.Combine(Output, "assets/org.example.notes/1.2.0/description.en.md")));

        JsonNode document = ReadCatalog();
        Assert.Equal(1, (int?)document["schemaVersion"]);
        Assert.Equal("productivity", (string?)document["categories"]![0]!["id"]);
        JsonNode app = document["apps"]![0]!;
        Assert.Equal("org.example.notes", (string?)app["appId"]);
        Assert.Equal("app", (string?)app["kind"]);
        Assert.Equal("Notes", (string?)app["name"]);
        Assert.Equal("productivity", (string?)app["category"]);
        Assert.Equal("Prendre des notes en Markdown.", (string?)app["shortDescriptions"]!["fr"]);
        Assert.Equal("1.2.0", (string?)app["latestVersion"]);
        JsonNode version = app["versions"]![0]!;
        Assert.Equal(TestCatalog.Commit, (string?)version["commit"]);
        Assert.Equal(64, ((string?)version["sha256"])!.Length);
        Assert.Equal("clipboard.read", (string?)version["capabilities"]![0]);
        Assert.Equal("2.1.0", (string?)version["sdkCompatibility"]!["minimumVersion"]);
        Assert.Equal("2026-10-30T12:00:00+00:00", (string?)version["publishedAt"]);
    }

    [Fact]
    public void Only_the_three_newest_versions_stay_downloadable()
    {
        foreach (string version in new[] { "1.0.0", "1.1.0", "1.2.0", "2.0.0" })
        {
            if (Directory.Exists(Built))
            {
                Directory.Delete(Built, recursive: true);
            }

            Build(catalog.AddApp(version: version));
            List<CatalogError> errors = [];
            CatalogPublisher.Publish(catalog.Root, Built, Output, 3, Now, errors);
            Assert.Empty(errors);
        }

        JsonNode app = ReadCatalog()["apps"]![0]!;
        Assert.Equal("2.0.0", (string?)app["latestVersion"]);
        Assert.Equal(["2.0.0", "1.2.0", "1.1.0"], app["versions"]!.AsArray().Select(v => (string?)v!["version"]));
        Assert.False(Directory.Exists(Path.Combine(Output, "packages/org.example.notes/1.0.0")));
        Assert.False(Directory.Exists(Path.Combine(Output, "assets/org.example.notes/1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(Output, "packages/org.example.notes/1.1.0")));
    }

    [Fact]
    public void An_older_version_published_late_does_not_change_the_listing()
    {
        Build(catalog.AddApp(version: "2.0.0"));
        List<CatalogError> errors = [];
        CatalogPublisher.Publish(catalog.Root, Built, Output, 3, Now, errors);
        Directory.Delete(Built, recursive: true);
        string older = catalog.AddApp(version: "1.5.0");
        File.WriteAllText(Path.Combine(older, "short_description.en.txt"), "Old text.");
        Build(older);

        CatalogPublisher.Publish(catalog.Root, Built, Output, 3, Now, errors);

        JsonNode app = ReadCatalog()["apps"]![0]!;
        Assert.Empty(errors);
        Assert.Equal("2.0.0", (string?)app["latestVersion"]);
        Assert.Equal("Take notes in Markdown.", (string?)app["shortDescriptions"]!["en"]);
        Assert.Equal(["2.0.0", "1.5.0"], app["versions"]!.AsArray().Select(v => (string?)v!["version"]));
    }

    [Fact]
    public void Nothing_is_written_when_one_folder_fails()
    {
        Build(catalog.AddApp());
        string broken = catalog.AddApp(appId: "org.example.broken");
        File.Delete(Path.Combine(broken, "icon.png"));
        Build(broken);

        List<CatalogError> errors = [];
        IReadOnlyList<string> published = CatalogPublisher.Publish(catalog.Root, Built, Output, 3, Now, errors);

        Assert.Empty(published);
        Assert.NotEmpty(errors);
        Assert.False(Directory.Exists(Output));
    }

    [Fact]
    public void A_change_of_categories_alone_rewrites_their_labels()
    {
        Build(catalog.AddApp());
        List<CatalogError> errors = [];
        CatalogPublisher.Publish(catalog.Root, Built, Output, 3, Now, errors);
        Directory.Delete(Built, recursive: true);
        File.WriteAllText(Path.Combine(catalog.Root, "categories.json"), """
            [{ "id": "productivity", "labels": { "en": "Get things done", "fr": "Efficacité" } },
             { "id": "utilities", "labels": { "en": "Utilities", "fr": "Utilitaires" } }]
            """);

        CatalogPublisher.Publish(catalog.Root, Built, Output, 3, Now, errors);

        JsonNode document = ReadCatalog();
        Assert.Empty(errors);
        Assert.Equal("Get things done", (string?)document["categories"]![0]!["labels"]!["en"]);
        Assert.Single(document["apps"]!.AsArray());
    }

    [Fact]
    public void A_built_folder_without_its_catalog_folder_is_refused()
    {
        Directory.CreateDirectory(Path.Combine(Built, "org.example.ghost_1.0.0"));
        List<CatalogError> errors = [];

        CatalogPublisher.Publish(catalog.Root, Built, Output, 3, Now, errors);

        Assert.Contains(errors, error => error.Message.Contains("does not exist on main", StringComparison.Ordinal));
    }

    private void Build(string folder)
    {
        string target = Path.Combine(Built, Path.GetFileName(folder), "1.0.0");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "app.hackapp"), TestCatalog.PackageOf(folder));
    }

    private JsonNode ReadCatalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Output, "catalog.json")))!;
}
