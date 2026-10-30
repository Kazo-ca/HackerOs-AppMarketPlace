using System.Text.Json.Nodes;

namespace HackerOs.MarketPlace.Tests;

public sealed class AppFolderValidatorTests : IDisposable
{
    private readonly TestCatalog catalog = new();

    public void Dispose() => catalog.Dispose();

    [Fact]
    public void A_complete_folder_is_valid()
    {
        string folder = catalog.AddApp();

        List<CatalogError> errors = [];
        AppFolder? result = AppFolderValidator.Validate(folder, catalog.Categories(), errors);

        Assert.Empty(errors);
        Assert.NotNull(result);
        Assert.Equal("org.example.notes", result.AppId);
        Assert.Equal("1.2.0", result.Version.ToString());
        Assert.Equal("Take notes in Markdown.", result.ShortDescriptions["en"]);
        Assert.Equal("description.en.md", result.DescriptionFiles["en"]);
        Assert.Equal(["notes", "markdown"], result.Store.Tags);
    }

    [Theory]
    [InlineData("notes_1.0.0")]
    [InlineData("org.example.notes-1.0.0")]
    [InlineData("org.example.notes_1.0")]
    [InlineData("org.example.notes_v1.0.0")]
    public void A_folder_name_that_is_not_appId_underscore_version_is_refused(string name)
    {
        string folder = Path.Combine(catalog.Apps, name);
        Directory.CreateDirectory(folder);

        Assert.Contains(catalog.Validate(folder), error => error.Message.Contains("<appId>_<version>", StringComparison.Ordinal));
    }

    [Fact]
    public void An_icon_of_the_wrong_size_is_refused()
    {
        string folder = catalog.AddApp();
        File.WriteAllBytes(Path.Combine(folder, "icon.png"), TestCatalog.Png(128, 128));

        Assert.Contains(catalog.Validate(folder), error => error.Message.Contains("128×128", StringComparison.Ordinal));
    }

    [Fact]
    public void An_icon_over_200_KB_is_refused()
    {
        string folder = catalog.AddApp();
        File.WriteAllBytes(Path.Combine(folder, "icon.png"), TestCatalog.Png(256, 256, padding: 200 * 1024));

        Assert.Contains(catalog.Validate(folder), error => error.Message.Contains("limit is 204800", StringComparison.Ordinal));
    }

    [Fact]
    public void An_icon_that_is_not_a_png_is_refused()
    {
        string folder = catalog.AddApp();
        File.WriteAllText(Path.Combine(folder, "icon.png"), "<svg/>");

        Assert.Contains(catalog.Validate(folder), error => error.Message == "The icon is not a PNG image.");
    }

    [Fact]
    public void English_descriptions_are_required()
    {
        string folder = catalog.AddApp();
        File.Delete(Path.Combine(folder, "short_description.en.txt"));
        File.Delete(Path.Combine(folder, "description.en.md"));

        List<CatalogError> errors = catalog.Validate(folder);

        Assert.Contains(errors, error => error.Message.Contains("short_description.en.txt", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("description.en.md", StringComparison.Ordinal));
    }

    [Fact]
    public void A_short_description_over_120_characters_is_refused()
    {
        string folder = catalog.AddApp();
        File.WriteAllText(Path.Combine(folder, "short_description.fr.txt"), new string('a', 121));

        Assert.Contains(catalog.Validate(folder), error => error.Message.Contains("121 characters", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_outside_the_format_is_refused()
    {
        string folder = catalog.AddApp();
        File.WriteAllBytes(Path.Combine(folder, "Example.Notes.dll"), [0x4D, 0x5A]);

        Assert.Contains(catalog.Validate(folder), error => error.Path.EndsWith("Example.Notes.dll", StringComparison.Ordinal));
    }

    [Fact]
    public void Screenshots_must_be_numbered_from_one_without_gaps()
    {
        string folder = catalog.AddApp();
        Directory.CreateDirectory(Path.Combine(folder, "screenshots"));
        File.WriteAllBytes(Path.Combine(folder, "screenshots", "1.png"), TestCatalog.Png(1280, 720));
        File.WriteAllBytes(Path.Combine(folder, "screenshots", "3.png"), TestCatalog.Png(1280, 720));

        Assert.Contains(catalog.Validate(folder), error => error.Message.Contains("1.png to N.png", StringComparison.Ordinal));
    }

    [Fact]
    public void Numbered_png_screenshots_are_listed_in_order()
    {
        string folder = catalog.AddApp();
        Directory.CreateDirectory(Path.Combine(folder, "screenshots"));
        File.WriteAllBytes(Path.Combine(folder, "screenshots", "2.png"), TestCatalog.Png(1280, 720));
        File.WriteAllBytes(Path.Combine(folder, "screenshots", "1.png"), TestCatalog.Png(1280, 720));

        List<CatalogError> errors = [];
        AppFolder? result = AppFolderValidator.Validate(folder, catalog.Categories(), errors);

        Assert.Empty(errors);
        Assert.Equal(["1.png", "2.png"], result!.Screenshots);
    }

    [Fact]
    public void The_manifest_must_match_the_folder_name()
    {
        string folder = catalog.AddApp();
        File.WriteAllText(Path.Combine(folder, "manifest.json"), TestCatalog.Manifest("org.example.other", "1.3.0", []));

        List<CatalogError> errors = catalog.Validate(folder);

        Assert.Contains(errors, error => error.Message.Contains("'org.example.other' does not match", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("'1.3.0' does not match", StringComparison.Ordinal));
    }

    [Fact]
    public void The_manifest_is_read_strictly_like_the_os_does()
    {
        string folder = catalog.AddApp();
        string manifest = TestCatalog.Manifest("org.example.notes", "1.2.0", []).Replace("\"autoStart\":false", "\"autoStart\":false,\"typo\":1", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(folder, "manifest.json"), manifest);

        Assert.Contains(catalog.Validate(folder), error => error.Message.StartsWith("The manifest cannot be read", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_capability_fails_the_manifest_validator()
    {
        string folder = catalog.AddApp(capabilities: ["filesystem.everything"]);

        Assert.Contains(catalog.Validate(folder), error => error.Path.EndsWith("manifest.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"commit\": \"3f2a9c1\"", "'commit' must be")]
    [InlineData("\"category\": \"games-and-fun\"", "'category' must be one of")]
    [InlineData("\"repository\": \"https://gitlab.com/example/notes\"", "'repository' must be")]
    [InlineData("\"license\": \"\"", "'license' must be")]
    [InlineData("\"tags\": [\"Notes\"]", "Each tag must match")]
    [InlineData("\"project\": \"../evil/x.csproj\"", "'project' must be")]
    [InlineData("\"project\": \"src/a.csproj; rm -rf /\"", "'project' must be")]
    [InlineData("\"kind\": \"theme-pack\"", "'kind' may only be")]
    [InlineData("\"binary\": \"https://example.com/app.hackapp\"", "Unknown property 'binary'")]
    public void Store_json_is_checked_strictly(string property, string expected)
    {
        string folder = catalog.AddApp();
        string path = Path.Combine(folder, "store.json");
        JsonObject store = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        foreach ((string name, JsonNode? value) in JsonNode.Parse("{" + property + "}")!.AsObject())
        {
            store[name] = value?.DeepClone();
        }

        File.WriteAllText(path, store.ToJsonString());

        Assert.Contains(catalog.Validate(folder), error => error.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Store_json_accepts_a_project_path_and_the_app_kind()
    {
        string folder = catalog.AddApp();
        File.WriteAllText(Path.Combine(folder, "store.json"), $$"""
            { "repository": "https://github.com/example/HackerOs-App-Notes", "commit": "{{TestCatalog.Commit}}",
              "category": "utilities", "license": "GPL-3.0-only", "project": "src/Example.Notes/Example.Notes.csproj", "kind": "app" }
            """);

        List<CatalogError> errors = [];
        AppFolder? result = AppFolderValidator.Validate(folder, catalog.Categories(), errors);

        Assert.Empty(errors);
        Assert.Equal("src/Example.Notes/Example.Notes.csproj", result!.Store.Project);
        Assert.Equal("example/HackerOs-App-Notes", result.Store.RepositorySlug);
        Assert.Empty(result.Store.Tags);
    }
}
