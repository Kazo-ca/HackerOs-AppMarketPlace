using System.Text;
using HackerOs.Platform.Core.Apps.Packages;

namespace HackerOs.MarketPlace.Tests;

public sealed class PackageCheckTests : IDisposable
{
    private readonly TestCatalog catalog = new();

    public void Dispose() => catalog.Dispose();

    [Fact]
    public void A_package_built_with_the_folder_manifest_is_accepted()
    {
        AppFolder folder = Load(catalog.AddApp());
        List<CatalogError> errors = [];

        AppPackage? package = PackageCheck.Check(folder, TestCatalog.PackageOf(folder.Path), errors);

        Assert.Empty(errors);
        Assert.Equal("org.example.notes", package!.AppId);
    }

    [Fact]
    public void A_package_whose_manifest_differs_from_the_folder_is_refused()
    {
        AppFolder folder = Load(catalog.AddApp());
        byte[] other = TestCatalog.Package(TestCatalog.Manifest("org.example.notes", "1.2.0", ["clipboard.read"]));
        List<CatalogError> errors = [];

        Assert.Null(PackageCheck.Check(folder, other, errors));
        Assert.Contains(errors, error => error.Message.Contains("differs from the one in the folder", StringComparison.Ordinal));
    }

    [Fact]
    public void Bytes_that_are_not_a_package_are_refused()
    {
        AppFolder folder = Load(catalog.AddApp());
        List<CatalogError> errors = [];

        Assert.Null(PackageCheck.Check(folder, Encoding.UTF8.GetBytes("not a zip"), errors));
        Assert.Contains(errors, error => error.Message.Contains("is refused", StringComparison.Ordinal));
    }

    private AppFolder Load(string path)
    {
        List<CatalogError> errors = [];
        AppFolder? folder = AppFolderValidator.Validate(path, catalog.Categories(), errors);
        Assert.Empty(errors);
        return folder!;
    }
}
