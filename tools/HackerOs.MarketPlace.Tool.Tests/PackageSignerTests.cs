using System.Text.Json.Nodes;

namespace HackerOs.MarketPlace.Tests;

/// <summary>ADR 0214 §2 of Kazo-ca/HackerOs: the catalog signs its packages.</summary>
public sealed class PackageSignerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private readonly TestCatalog catalog = new();

    public void Dispose() => catalog.Dispose();

    [Fact]
    public void A_signature_verifies_with_the_public_key_of_its_key_only()
    {
        (string privateKey, string publicKey) = PackageSigner.CreateKeyPair();
        (_, string otherPublicKey) = PackageSigner.CreateKeyPair();
        byte[] package = [1, 2, 3];

        string signature = PackageSigner.Sign(package, privateKey);

        Assert.Equal(64, Convert.FromBase64String(signature).Length);
        Assert.Equal(publicKey, PackageSigner.GetPublicKey(privateKey));
        Assert.True(PackageSigner.Verify(package, signature, publicKey));
        Assert.False(PackageSigner.Verify(package, signature, otherPublicKey));
        Assert.False(PackageSigner.Verify([1, 2, 4], signature, publicKey));
        Assert.False(PackageSigner.Verify(package, "not base64!", publicKey));
    }

    [Fact]
    public void A_key_that_is_not_a_pem_private_key_is_refused() =>
        Assert.Throws<ArgumentException>(() => PackageSigner.Sign([1], "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----"));

    [Fact]
    public void Publish_signs_each_new_package_when_a_key_is_given()
    {
        (string privateKey, string publicKey) = PackageSigner.CreateKeyPair();
        string built = Path.Combine(catalog.Root, "built");
        string output = Path.Combine(catalog.Root, "packages-branch");
        string folder = catalog.AddApp();
        string target = Path.Combine(built, Path.GetFileName(folder), "1.0.0");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "app.hackapp"), TestCatalog.PackageOf(folder));
        List<CatalogError> errors = [];

        CatalogPublisher.Publish(catalog.Root, built, output, 3, Now, errors, privateKey);

        Assert.Empty(errors);
        JsonNode version = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "catalog.json")))!["apps"]![0]!["versions"]![0]!;
        byte[] package = File.ReadAllBytes(Path.Combine(output, (string)version["package"]!));
        Assert.True(PackageSigner.Verify(package, (string)version["signature"]!, publicKey));
    }

    [Fact]
    public void Keygen_writes_a_new_private_key_file_and_never_replaces_one()
    {
        string path = Path.Combine(catalog.Root, "catalog-signing.pem");

        int first = Program.Main(["keygen", "--private-key", path]);
        string written = File.ReadAllText(path);
        int second = Program.Main(["keygen", "--private-key", path]);

        Assert.Equal((0, 1), (first, second));
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", written, StringComparison.Ordinal);
        Assert.Equal(written, File.ReadAllText(path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    [Fact]
    public void Sign_prints_a_signature_that_verifies()
    {
        string key = Path.Combine(catalog.Root, "key.pem");
        string package = Path.Combine(catalog.Root, "app.hackapp");
        Program.Main(["keygen", "--private-key", key]);
        File.WriteAllBytes(package, [9, 8, 7]);
        StringWriter output = new();
        TextWriter original = Console.Out;
        Console.SetOut(output);
        int code;
        try
        {
            code = Program.Main(["sign", "--package", package, "--private-key", key]);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal(0, code);
        Assert.True(PackageSigner.Verify([9, 8, 7], output.ToString().Trim(), PackageSigner.GetPublicKey(File.ReadAllText(key))));
    }
}
