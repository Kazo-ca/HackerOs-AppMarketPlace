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
/// updates the <c>packages</c> branch.</item>
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
        IReadOnlyList<string> published = CatalogPublisher.Publish(root, built, output, keep, DateTimeOffset.FromUnixTimeSeconds(publishedAt.ToUnixTimeSeconds()), errors);
        return Report(errors, $"Published: {(published.Count == 0 ? "nothing" : string.Join(", ", published))}.");
    }

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
        return 2;
    }
}
