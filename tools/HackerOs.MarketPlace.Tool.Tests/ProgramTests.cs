namespace HackerOs.MarketPlace.Tests;

public sealed class ProgramTests : IDisposable
{
    private readonly TestCatalog catalog = new();

    public void Dispose() => catalog.Dispose();

    [Fact]
    public void Validate_returns_zero_for_a_valid_catalog_and_one_for_a_broken_folder()
    {
        string folder = catalog.AddApp();
        Assert.Equal(0, Program.Main(["validate", "--root", catalog.Root]));

        File.Delete(Path.Combine(folder, "store.json"));
        Assert.Equal(1, Program.Main(["validate", "--root", catalog.Root, Path.GetFileName(folder)]));
    }

    [Fact]
    public void Validate_refuses_a_folder_name_that_leaves_apps() =>
        Assert.Equal(1, Program.Main(["validate", "--root", catalog.Root, "../tools"]));

    [Fact]
    public void Check_package_writes_the_report_even_when_the_build_failed()
    {
        string folder = catalog.AddApp();
        string report = Path.Combine(catalog.Root, "report.md");

        int code = Program.Main(["check-package", "--root", catalog.Root, "--folder", Path.GetFileName(folder), "--report", report]);

        Assert.Equal(1, code);
        Assert.Contains("No package was built", File.ReadAllText(report), StringComparison.Ordinal);
    }

    [Fact]
    public void Check_package_accepts_the_matching_package()
    {
        string folder = catalog.AddApp();
        string package = Path.Combine(catalog.Root, "notes.hackapp");
        File.WriteAllBytes(package, TestCatalog.PackageOf(folder));
        string report = Path.Combine(catalog.Root, "report.md");

        int code = Program.Main(["check-package", "--root", catalog.Root, "--folder", Path.GetFileName(folder), "--package", package, "--report", report]);

        Assert.Equal(0, code);
        Assert.DoesNotContain("Problems", File.ReadAllText(report), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_command_is_a_usage_error() =>
        Assert.Equal(2, Program.Main(["frobnicate"]));
}
