using System.Globalization;
using HackerOs.Platform.Core.Apps.Packages;

namespace HackerOs.MarketPlace;

/// <summary>
/// Command line of the catalog tool, run by the workflows of this repository:
/// <list type="bullet">
/// <item><c>validate --root &lt;catalog&gt; [folder...]</c>: checks <c>categories.json</c> and the
/// named app folders (every folder when none is named).</item>
/// <item><c>check-package --root &lt;catalog&gt; --folder &lt;name&gt; --report &lt;file.md&gt; [--package &lt;file.hackapp&gt;] [--sarif &lt;dir&gt;]</c>:
/// checks the package built from a folder and writes its review comment section.</item>
/// <item><c>publish --root &lt;catalog&gt; --built &lt;dir&gt; --output &lt;packages branch&gt; [--keep 3] [--published-at &lt;ISO 8601&gt;]</c>:
/// updates the <c>packages</c> branch, signing each new package with the key of the
/// <c>HACK_STORE_SIGNING_KEY</c> variable when it is set (ADR 0214 §2 of Kazo-ca/HackerOs).</item>
/// <item><c>keygen --private-key &lt;file.pem&gt;</c>: creates the catalog's key pair, writes the private
/// key to a new file and prints the public key to compile into the OS.</item>
/// <item><c>sign --package &lt;file.hackapp&gt; [--private-key &lt;file.pem&gt;]</c>: prints the signature of a package.</item>
/// </list>
/// Exit code 0 on success, 1 when anything is refused, 2 on a usage error.
/// </summary>
public static class Program
{
    private static readonly bool GitHubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    /// <summary>Runs one command.</summary>
    public static int Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "validate" => Validate(CommandLine.Parse(args.AsSpan(1))),
                "check-package" => CheckPackage(CommandLine.Parse(args.AsSpan(1))),
                "publish" => Publish(CommandLine.Parse(args.AsSpan(1))),
                "keygen" => KeyGen(CommandLine.Parse(args.AsSpan(1))),
                "sign" => Sign(CommandLine.Parse(args.AsSpan(1))),
                _ => Usage(),
            };
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return Usage();
        }
    }

    private static int Validate(CommandLine line)
    {
        string root = Path.GetFullPath(line.Required("root"));
        List<CatalogError> errors = [];
        CategoryList categories = CategoryList.Load(Path.Combine(root, CatalogPaths.CategoriesFile), CatalogPaths.CategoriesFile, errors);
        string appsRoot = Path.Combine(root, CatalogPaths.AppsFolder);
        IEnumerable<string> folders = line.Positional.Count > 0
            ? line.Positional
            : Directory.Exists(appsRoot) ? Directory.EnumerateDirectories(appsRoot).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal) : [];
        int checkedFolders = 0;
        foreach (string name in folders)
        {
            string folder = Path.Combine(appsRoot, name);
            if (!Directory.Exists(folder) || name.Contains('/', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal))
            {
                errors.Add(new CatalogError($"{CatalogPaths.AppsFolder}/{name}", "No such app folder."));
                continue;
            }

            AppFolderValidator.Validate(folder, categories, errors);
            checkedFolders++;
        }

        return Report(errors, $"{checkedFolders} app folder(s) and {categories.Categories.Count} categories are valid.");
    }

    private static int CheckPackage(CommandLine line)
    {
        string root = Path.GetFullPath(line.Required("root"));
        string name = line.Required("folder");
        string reportPath = Path.GetFullPath(line.Required("report"));
        List<CatalogError> errors = [];
        CategoryList categories = CategoryList.Load(Path.Combine(root, CatalogPaths.CategoriesFile), CatalogPaths.CategoriesFile, errors);
        AppFolder? folder = AppFolderValidator.Validate(Path.Combine(root, CatalogPaths.AppsFolder, name), categories, errors);
        if (folder is null)
        {
            File.WriteAllText(reportPath, $"### `{name}`\n\n**❌ Problems**\n\n{string.Join("\n", errors.Select(e => $"- `{e.Path}`: {e.Message}"))}\n");
            return Report(errors, string.Empty);
        }

        string? packagePath = line.Optional("package");
        byte[]? bytes = packagePath is not null && File.Exists(packagePath) ? File.ReadAllBytes(packagePath) : null;
        AppPackage? package = null;
        if (bytes is null)
        {
            errors.Add(new CatalogError($"{CatalogPaths.AppsFolder}/{name}", $"No package was built from commit {folder.Store.Commit}: see the build log of this workflow."));
        }
        else
        {
            package = PackageCheck.Check(folder, bytes, errors);
        }

        IReadOnlyList<SarifFinding> findings = line.Optional("sarif") is { } sarif ? SarifSummary.Read(sarif) : [];
        File.WriteAllText(reportPath, ReviewReport.Write(folder, package, bytes, findings, errors));
        return Report(errors, $"{folder.AppId} {folder.Version}: the package matches its folder.");
    }

    private static int Publish(CommandLine line)
    {
        string root = Path.GetFullPath(line.Required("root"));
        string built = Path.GetFullPath(line.Required("built"));
        string output = Path.GetFullPath(line.Required("output"));
        int keep = line.Optional("keep") is { } keepText ? int.Parse(keepText, CultureInfo.InvariantCulture) : CatalogPaths.DefaultKeptVersions;
        DateTimeOffset publishedAt = line.Optional("published-at") is { } at
            ? DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
            : DateTimeOffset.UtcNow;
        List<CatalogError> errors = [];
        string? signingKey = ReadSigningKey(null);
        IReadOnlyList<string> published = CatalogPublisher.Publish(
            root, built, output, keep, DateTimeOffset.FromUnixTimeSeconds(publishedAt.ToUnixTimeSeconds()), errors, signingKey);
        string signed = signingKey is null ? " (unsigned: no signing key)" : $" (signed by {PackageSigner.GetPublicKey(signingKey)})";
        return Report(errors, $"Published: {(published.Count == 0 ? "nothing" : string.Join(", ", published))}{signed}.");
    }

    // The private key never goes through the command line: a file the maintainer keeps, or the secret.
    private static int KeyGen(CommandLine line)
    {
        string path = Path.GetFullPath(line.Required("private-key"));
        if (File.Exists(path))
        {
            Console.Error.WriteLine($"{path} already exists: keygen never replaces a key.");
            return 1;
        }

        (string privateKey, string publicKey) = PackageSigner.CreateKeyPair();
        using (FileStream file = new(path, FileMode.CreateNew, FileAccess.Write))
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(file.SafeFileHandle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            using StreamWriter writer = new(file);
            writer.Write(privateKey);
        }

        Console.WriteLine($"Private key written to {path}: store it as the {PackageSigner.KeyVariable} secret of this repository, then keep it offline.");
        Console.WriteLine("Public key (HackerOsAppSigningKeys, catalog:official=...):");
        Console.WriteLine(publicKey);
        return 0;
    }

    private static int Sign(CommandLine line)
    {
        byte[] package = File.ReadAllBytes(Path.GetFullPath(line.Required("package")));
        string? key = ReadSigningKey(line.Optional("private-key"));
        if (key is null)
        {
            Console.Error.WriteLine($"No signing key: pass --private-key <file.pem> or set {PackageSigner.KeyVariable}.");
            return 1;
        }

        Console.WriteLine(PackageSigner.Sign(package, key));
        return 0;
    }

    private static string? ReadSigningKey(string? path) =>
        path is not null ? File.ReadAllText(Path.GetFullPath(path))
        : Environment.GetEnvironmentVariable(PackageSigner.KeyVariable) is { Length: > 0 } pem ? pem
        : null;

    private static int Report(IReadOnlyList<CatalogError> errors, string success)
    {
        foreach (CatalogError error in errors)
        {
            Console.Error.WriteLine(error.Format(GitHubActions));
        }

        if (errors.Count > 0)
        {
            return 1;
        }

        Console.WriteLine(success);
        return 0;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("HackerOS catalog tool (ADR 0188)");
        Console.Error.WriteLine("  validate --root <catalog> [folder...]");
        Console.Error.WriteLine("  check-package --root <catalog> --folder <name> --report <file.md> [--package <file.hackapp>] [--sarif <dir>]");
        Console.Error.WriteLine("  publish --root <catalog> --built <dir> --output <packages branch> [--keep 3] [--published-at <ISO 8601>]");
        Console.Error.WriteLine("  keygen --private-key <file.pem>");
        Console.Error.WriteLine("  sign --package <file.hackapp> [--private-key <file.pem>]");
        return 2;
    }
}
