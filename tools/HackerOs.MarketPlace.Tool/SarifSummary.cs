using System.Text.Json;

namespace HackerOs.MarketPlace;

/// <summary>One CodeQL result, reduced to what the review comment shows.</summary>
public sealed record SarifFinding(string Level, string RuleId, string Message, string Location);

/// <summary>
/// Reads the SARIF files CodeQL writes (ADR 0188 §5 step 6). The analysis runs on a pull request
/// from a fork, whose token cannot upload to code scanning, so the results are summed up in the
/// review comment instead.
/// </summary>
public static class SarifSummary
{
    /// <summary>Reads every <c>*.sarif</c> file under <paramref name="folder"/>.</summary>
    /// <returns>The findings, errors first.</returns>
    public static IReadOnlyList<SarifFinding> Read(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        List<SarifFinding> findings = [];
        foreach (string file in Directory.EnumerateFiles(folder, "*.sarif", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
            if (!document.RootElement.TryGetProperty("runs", out JsonElement runs))
            {
                continue;
            }

            foreach (JsonElement run in runs.EnumerateArray())
            {
                if (run.TryGetProperty("results", out JsonElement results))
                {
                    findings.AddRange(results.EnumerateArray().Select(ReadResult));
                }
            }
        }

        return [.. findings.OrderBy(finding => Rank(finding.Level)).ThenBy(finding => finding.Location, StringComparer.Ordinal)];
    }

    private static SarifFinding ReadResult(JsonElement result)
    {
        // SARIF's default level is "warning" when the result does not say.
        string level = StrictJson.GetString(result, "level") ?? "warning";
        string rule = StrictJson.GetString(result, "ruleId") ?? "unknown";
        string message = result.TryGetProperty("message", out JsonElement messageElement)
            ? StrictJson.GetString(messageElement, "text") ?? string.Empty
            : string.Empty;
        return new SarifFinding(level, rule, message, ReadLocation(result));
    }

    private static string ReadLocation(JsonElement result)
    {
        if (!result.TryGetProperty("locations", out JsonElement locations) || locations.GetArrayLength() == 0
            || !locations[0].TryGetProperty("physicalLocation", out JsonElement physical))
        {
            return string.Empty;
        }

        string uri = physical.TryGetProperty("artifactLocation", out JsonElement artifact) ? StrictJson.GetString(artifact, "uri") ?? string.Empty : string.Empty;
        return physical.TryGetProperty("region", out JsonElement region) && region.TryGetProperty("startLine", out JsonElement line)
            ? $"{uri}:{line.GetInt32()}"
            : uri;
    }

    private static int Rank(string level) => level switch
    {
        "error" => 0,
        "warning" => 1,
        "note" => 2,
        _ => 3,
    };
}
