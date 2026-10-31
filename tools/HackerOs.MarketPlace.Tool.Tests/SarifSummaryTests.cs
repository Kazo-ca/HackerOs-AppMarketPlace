namespace HackerOs.MarketPlace.Tests;

public sealed class SarifSummaryTests : IDisposable
{
    private readonly TestCatalog catalog = new();

    public void Dispose() => catalog.Dispose();

    [Fact]
    public void Results_are_read_with_errors_first_and_warning_as_the_default_level()
    {
        string folder = Path.Combine(catalog.Root, "sarif");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "csharp.sarif"), """
            { "runs": [ { "results": [
              { "ruleId": "cs/weak-crypto", "message": { "text": "MD5." },
                "locations": [ { "physicalLocation": { "artifactLocation": { "uri": "src/Hash.cs" }, "region": { "startLine": 7 } } } ] },
              { "ruleId": "cs/path-injection", "level": "error", "message": { "text": "Path from input." } }
            ] } ] }
            """);

        IReadOnlyList<SarifFinding> findings = SarifSummary.Read(folder);

        Assert.Equal(2, findings.Count);
        Assert.Equal(new SarifFinding("error", "cs/path-injection", "Path from input.", string.Empty), findings[0]);
        Assert.Equal(new SarifFinding("warning", "cs/weak-crypto", "MD5.", "src/Hash.cs:7"), findings[1]);
    }

    [Fact]
    public void A_missing_folder_has_no_finding() =>
        Assert.Empty(SarifSummary.Read(Path.Combine(catalog.Root, "none")));
}
