using System.Text;
using HackerOs.App.Abstractions;
using HackerOs.Platform.Core.Apps.Packages;

namespace HackerOs.MarketPlace;

/// <summary>
/// Writes the review comment of one app version (ADR 0188 §5 step 7): what the reviewer must look
/// at before approving. Dangerous capabilities and embedded JavaScript come first, because they are
/// what can harm the user; the rest is context.
/// </summary>
public static class ReviewReport
{
    private const int MaximumListedFindings = 25;

    /// <summary>Builds the Markdown section of one app version.</summary>
    /// <param name="folder">The checked folder.</param>
    /// <param name="package">The package our CI built, when the build succeeded.</param>
    /// <param name="packageBytes">The package bytes, for its size and SHA-256.</param>
    /// <param name="findings">CodeQL findings.</param>
    /// <param name="errors">Problems found while checking the package.</param>
    public static string Write(AppFolder folder, AppPackage? package, byte[]? packageBytes, IReadOnlyList<SarifFinding> findings, IReadOnlyList<CatalogError> errors)
    {
        StringBuilder text = new();
        text.AppendLine($"### `{folder.AppId}` {folder.Version}");
        text.AppendLine();
        text.AppendLine($"- Source: {folder.Store.Repository}/tree/{folder.Store.Commit}");
        text.AppendLine($"- Category: `{folder.Store.Category}` · License: `{folder.Store.License}` · SDK: `{DescribeSdk(folder.Manifest.SdkCompatibility)}`");
        if (packageBytes is not null)
        {
            text.AppendLine($"- Package: {packageBytes.Length:N0} bytes, SHA-256 `{AppPackage.Sha256(packageBytes)}`");
        }

        text.AppendLine();
        AppendErrors(text, errors);
        AppendCapabilities(text, folder.Manifest.Capabilities);
        if (package is not null)
        {
            AppendFiles(text, package);
        }

        AppendFindings(text, findings);
        return text.ToString();
    }

    private static string DescribeSdk(AppSdkCompatibilityManifest sdk) =>
        sdk.MaximumVersion is null ? $">= {sdk.MinimumVersion}" : $"{sdk.MinimumVersion} – {sdk.MaximumVersion}";

    private static void AppendErrors(StringBuilder text, IReadOnlyList<CatalogError> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }

        text.AppendLine("**❌ Problems**");
        text.AppendLine();
        foreach (CatalogError error in errors)
        {
            text.AppendLine($"- `{error.Path}`: {error.Message}");
        }

        text.AppendLine();
    }

    private static void AppendCapabilities(StringBuilder text, IReadOnlyList<string> capabilities)
    {
        text.AppendLine("**Capabilities**");
        text.AppendLine();
        if (capabilities.Count == 0)
        {
            text.AppendLine("None.");
            text.AppendLine();
            return;
        }

        // Dangerous first: they are the ones the reviewer must justify against the app's purpose.
        foreach (string capability in capabilities.OrderBy(c => AppCapabilities.IsKnown(c) && AppCapabilities.IsDangerous(c) ? 0 : 1).ThenBy(c => c, StringComparer.Ordinal))
        {
            if (!AppCapabilities.IsKnown(capability))
            {
                text.AppendLine($"- ❓ `{capability}`: unknown to the SDK the catalog checks against");
                continue;
            }

            CapabilityDescription description = AppCapabilities.Describe(capability);
            text.AppendLine(AppCapabilities.IsDangerous(capability)
                ? $"- ⚠️ **`{capability}`** (dangerous): {description.Title}. {description.RiskDescription}"
                : $"- `{capability}`: {description.Title}");
        }

        text.AppendLine();
    }

    private static void AppendFiles(StringBuilder text, AppPackage package)
    {
        string[] scripts = [.. package.Files.Keys.Where(IsScript).Order(StringComparer.Ordinal)];
        text.AppendLine($"**Embedded JavaScript** ({scripts.Length})");
        text.AppendLine();
        text.AppendLine(scripts.Length == 0 ? "None." : string.Join(Environment.NewLine, scripts.Select(path => $"- `{path}` ({package.Files[path].Length:N0} bytes)")));
        text.AppendLine();
        text.AppendLine($"**Assemblies**: {string.Join(", ", package.Assemblies.Keys.Order(StringComparer.Ordinal).Select(name => $"`{name}`"))}");
        text.AppendLine();
    }

    private static bool IsScript(string path) =>
        path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase);

    private static void AppendFindings(StringBuilder text, IReadOnlyList<SarifFinding> findings)
    {
        string counts = findings.Count == 0
            ? "no result"
            : string.Join(", ", findings.GroupBy(finding => finding.Level).Select(group => $"{group.Count()} {group.Key}"));
        text.AppendLine($"**CodeQL**: {counts}");
        text.AppendLine();
        foreach (SarifFinding finding in findings.Take(MaximumListedFindings))
        {
            text.AppendLine($"- {finding.Level} `{finding.RuleId}` at `{finding.Location}`: {finding.Message.ReplaceLineEndings(" ")}");
        }

        if (findings.Count > MaximumListedFindings)
        {
            text.AppendLine($"- … {findings.Count - MaximumListedFindings} more in the workflow's SARIF artifact.");
        }

        if (findings.Count > 0)
        {
            text.AppendLine();
        }
    }
}
