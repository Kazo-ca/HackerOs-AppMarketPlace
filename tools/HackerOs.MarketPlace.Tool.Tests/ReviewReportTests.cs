namespace HackerOs.MarketPlace.Tests;

public sealed class ReviewReportTests : IDisposable
{
    private readonly TestCatalog catalog = new();

    public void Dispose() => catalog.Dispose();

    [Fact]
    public void Dangerous_capabilities_come_first_and_are_highlighted()
    {
        AppFolder folder = Load(catalog.AddApp(capabilities: ["clipboard.read", "filesystem.system.write"]));
        byte[] bytes = TestCatalog.PackageOf(folder.Path);
        List<CatalogError> errors = [];

        string report = ReviewReport.Write(folder, PackageCheck.Check(folder, bytes, errors), bytes, [], errors);

        int dangerous = report.IndexOf("⚠️ **`filesystem.system.write`** (dangerous)", StringComparison.Ordinal);
        int normal = report.IndexOf("- `clipboard.read`", StringComparison.Ordinal);
        Assert.True(dangerous >= 0 && normal > dangerous, report);
        Assert.Contains("**Embedded JavaScript** (0)", report, StringComparison.Ordinal);
        Assert.Contains("`Example.Notes.dll`", report, StringComparison.Ordinal);
        Assert.Contains($"tree/{TestCatalog.Commit}", report, StringComparison.Ordinal);
        Assert.Contains("**CodeQL**: no result", report, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeQL_findings_and_problems_are_listed()
    {
        AppFolder folder = Load(catalog.AddApp());
        SarifFinding[] findings =
        [
            new("error", "cs/sql-injection", "Query built from user input.", "src/Db.cs:12"),
            new("warning", "js/xss", "Unsafe HTML.", "wwwroot/app.js:3"),
        ];
        CatalogError[] errors = [new("apps/org.example.notes_1.2.0", "No package was built.")];

        string report = ReviewReport.Write(folder, null, null, findings, errors);

        Assert.Contains("**❌ Problems**", report, StringComparison.Ordinal);
        Assert.Contains("**CodeQL**: 1 error, 1 warning", report, StringComparison.Ordinal);
        Assert.Contains("- error `cs/sql-injection` at `src/Db.cs:12`", report, StringComparison.Ordinal);
    }

    private AppFolder Load(string path)
    {
        List<CatalogError> errors = [];
        AppFolder? folder = AppFolderValidator.Validate(path, catalog.Categories(), errors);
        Assert.Empty(errors);
        return folder!;
    }
}
