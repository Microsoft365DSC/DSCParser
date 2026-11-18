using System.Collections.Concurrent;
using DSCParser.CSharp;
using DSCParser.PSDSC;
using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;

namespace DSCParser.Tests;

public class DscParserKeywordSetTests : IDisposable
{
    private static readonly DscKeywordSet ContosoKeywords = BuildKeywords("ContosoPolicy");

    private static readonly DscKeywordSet FabrikamKeywords = BuildKeywords("FabrikamPolicy");

    public void Dispose()
    {
        DscKeywordRegistry.ClearKeywordTable();
        DscParser.ClearCaches();
        DscParser.WarningSink = null;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ParseConfiguration_WithKeywordSetsOnParallelThreads_ShouldKeepEachParseToItsOwnKeywordsAndWarnings()
    {
        List<string> globalWarnings = [];
        DscParser.WarningSink = globalWarnings.Add;
        ConcurrentQueue<string> failures = new();

        _ = Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 8 }, iteration =>
        {
            bool isContoso = iteration % 2 == 0;
            string resource = isContoso ? "ContosoPolicy" : "FabrikamPolicy";
            string other = isContoso ? "FabrikamPolicy" : "ContosoPolicy";
            List<string> warnings = [];

            DscParseResult result = DscParser.ParseConfiguration(
                $$"""
                Configuration TenantConfig
                {
                    Node localhost
                    {
                        {{resource}} "Own{{iteration}}"
                        {
                            DisplayName = 'Own'
                        }
                        {{other}} "Other{{iteration}}"
                        {
                            DisplayName = 'Other'
                        }
                    }
                }
                """,
                new DscParseOptions { Keywords = isContoso ? ContosoKeywords : FabrikamKeywords, WarningSink = warnings.Add });

            string read = string.Join(",", result.Resources.Select(instance => $"{instance.ResourceName}/{instance.ResourceInstanceName}"));
            string skipped = string.Join(",", result.SkippedStatements.Select(statement => $"{statement.ResourceName}/{statement.InstanceName}"));

            if (read != $"{resource}/Own{iteration}" ||
                skipped != $"{other}/Other{iteration}" ||
                warnings.Count != 1 ||
                !warnings[0].Contains($"Other{iteration}", StringComparison.Ordinal))
            {
                failures.Enqueue($"{iteration}: read {read}, skipped {skipped}, warnings {string.Join(" | ", warnings)}");
            }
        });

        Assert.Empty(failures);
        Assert.Empty(globalWarnings);
        Assert.False(DscKeywordRegistry.HasSchemaCacheKeywords);
    }

    [Fact]
    public void ConvertToDscObject_WithAKeywordSet_ShouldRegisterNoResourceForLaterParses()
    {
        _ = DscParser.ConvertToDscObject(
            content: """
                Configuration TenantConfig
                {
                    Node localhost
                    {
                        ContosoPolicy "Corp"
                        {
                            DisplayName = 'Corp'
                        }
                    }
                }
                """,
            options: new DscParseOptions { Keywords = ContosoKeywords },
            dscResources: [new DscResourceInfo { Name = "ContosoPolicy", ResourceType = "ContosoPolicy" }]);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            DscParser.ConvertToDscObject(content: "Configuration Empty { }", options: new DscParseOptions { UseRegisteredKeywords = true }));

        Assert.Contains("No DSC resources loaded", refused.Message, StringComparison.Ordinal);
    }

    private static DscKeywordSet BuildKeywords(string resource) => DscKeywordSet.FromSchemaCache(
    [
        SchemaCacheEntry.Build(resource, "NameRequired", new Dictionary<string, string[]> { ["DisplayName"] = ["String"] }),
    ]);
}
