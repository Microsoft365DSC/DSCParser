using DSCParser.CSharp;
using DSCParser.PSDSC;
using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;

namespace DSCParser.Tests;

public class DscParserExtentTests : IDisposable
{
    private const string PolicyBlock = """
            ContosoPolicy "Corp"
            {
                # The display name
                DisplayName = "Corp } policy"
                Assignments = @(
                    MSFT_ContosoAssignment
                    {
                        Target = 'All'
                    }
                )
                Description = @"
        first }
        second {
        "@
            }
        """;

    private const string SecondBlock = """
            ContosoPolicy "Second"
            {
                DisplayName = 'Second'
            }
        """;

    public void Dispose()
    {
        DscKeywordRegistry.ResetSchemaCache();
        DscKeywordRegistry.ClearKeywordTable();
        DscParser.ClearCaches();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ParseConfiguration_ShouldReportTheExtentOfEachBlockInTheContent()
    {
        string content = $$"""
            # Exported configuration
            Configuration TenantConfig
            {
                Import-DscResource -ModuleName 'Contoso' -ModuleVersion '2.0.0'

                Node localhost
                {
            {{PolicyBlock}}

                    # Between the blocks
            {{SecondBlock}}
                }
            }
            TenantConfig -ConfigurationData .\ConfigurationData.psd1
            """;

        DscParseResult result = Parse(content);

        Assert.Equal(2, result.Resources.Count);
        AssertExtent(content, PolicyBlock, result.Resources[0].Extent);
        AssertExtent(content, SecondBlock, result.Resources[1].Extent);
        Assert.Empty(result.SkippedStatements);
    }

    [Fact]
    public void ParseConfiguration_WithAQuotedRepair_ShouldReportExtentsInTheOriginalContent()
    {
        const string repairedBlock = """
                    ContosoPolicy "Repaired"
                    {
                        DisplayName = 12345678-1234-1234-ad9c-123456789abc
                    }
            """;

        string content = $$"""
            Configuration TenantConfig
            {
                Node localhost
                {
            {{repairedBlock}}
            {{SecondBlock}}
                }
            }
            """;

        DscParseResult result = Parse(content);

        Assert.Equal("12345678-1234-1234-ad9c-123456789abc", result.Resources[0].Properties["DisplayName"]);
        AssertExtent(content, repairedBlock, result.Resources[0].Extent);
        AssertExtent(content, SecondBlock, result.Resources[1].Extent);
    }

    [Fact]
    public void ParseConfiguration_ShouldListEverySkippedNodeStatementWithItsExtent()
    {
        const string assignment = "$Unused = 1";
        const string undefined = """
                    ContosoGone "Old"
                    {
                        DisplayName = 'Old'
                    }
            """;
        const string unloaded = """
                    ContosoExtra "Extra"
                    {
                        DisplayName = 'Extra'
                    }
            """;

        string content = $$"""
            Configuration TenantConfig
            {
                $Header = 'kept out of the list'
                Node localhost
                {
                    {{assignment}}
            {{undefined}}
            {{unloaded}}
            {{SecondBlock}}
                }
            }
            """;

        DscParseResult result = Parse(content);

        Assert.Equal("Second", Assert.Single(result.Resources).ResourceInstanceName);
        AssertExtent(content, undefined, result.SkippedStatements[1].Extent);
        AssertExtent(content, unloaded, result.SkippedStatements[2].Extent);
        Assert.Equal(
            [
                (DscSkipReason.UnrecognizedStatement, assignment, (string?)null, (string?)null),
                (DscSkipReason.UndefinedResource, undefined.Trim(), "ContosoGone", "Old"),
                (DscSkipReason.UnloadedResource, unloaded.Trim(), "ContosoExtra", "Extra"),
            ],
            result.SkippedStatements.Select(skipped => (skipped.Reason, skipped.Text, skipped.ResourceName, skipped.InstanceName)));
    }

    private static void AssertExtent(string content, string block, DscResourceExtent? extent)
    {
        string text = block.Trim();
        int start = content.IndexOf(text, StringComparison.Ordinal);
        int end = start + text.Length;
        int Line(int offset) => content.Take(offset).Count(character => character == '\n') + 1;

        Assert.NotNull(extent);
        Assert.Equal(text, content[extent.StartOffset..extent.EndOffset]);
        Assert.Equal((start, end, Line(start), Line(end - 1)), (extent.StartOffset, extent.EndOffset, extent.StartLine, extent.EndLine));
    }

    private static DscParseResult Parse(string content)
    {
        _ = DscKeywordRegistry.RegisterFromSchemaCache(
        [
            SchemaCacheEntry.Build("ContosoPolicy", "NameRequired", new Dictionary<string, string[]>
            {
                ["DisplayName"] = ["String"],
                ["Description"] = ["String"],
                ["Assignments"] = ["ContosoAssignment[]"],
            }),
            SchemaCacheEntry.Build("ContosoExtra", "NameRequired", new Dictionary<string, string[]>
            {
                ["DisplayName"] = ["String"],
            }),
            SchemaCacheEntry.Build("MSFT_ContosoAssignment", "NoName", new Dictionary<string, string[]>
            {
                ["Target"] = ["String"],
            }),
        ]);

        return DscParser.ParseConfiguration(
            content,
            new DscParseOptions { UseRegisteredKeywords = true },
            [
                new DscResourceInfo { Name = "ContosoPolicy", ResourceType = "ContosoPolicy" },
                new DscResourceInfo { Name = "MSFT_ContosoAssignment", ResourceType = "MSFT_ContosoAssignment" },
            ]);
    }
}
