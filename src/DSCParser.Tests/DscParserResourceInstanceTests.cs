using DSCParser.CSharp;
using DSCParser.PSDSC;
using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;

namespace DSCParser.Tests;

/// <summary>
/// Resources are supplied without a module so that conversion never has to resolve installed
/// module versions, which would need a runspace the test host cannot open.
/// </summary>
public class DscParserResourceInstanceTests
{
    private static List<DscResourceInstance> Convert(string content, DscParseOptions? options = null, params string[] resourceNames)
    {
        List<object> resources = [.. resourceNames.Select(object (n) => new DscResourceInfo { Name = n })];

        return DscParser.ConvertToDscObject(content: content, options: options, dscResources: resources);
    }

    private static (List<DscResourceInstance> Instances, List<string> Warnings) ConvertCapturingWarnings(
        string content, params string[] resourceNames)
    {
        var warnings = new List<string>();
        DscParser.WarningSink = warnings.Add;
        try
        {
            return (Convert(content, null, resourceNames), warnings);
        }
        finally
        {
            DscParser.WarningSink = null;
        }
    }

    private static void Cleanup()
    {
        DscParser.ClearCaches();
        DscKeywordRegistry.Reset();
    }

    private static void SkipIfArchiveKeywordMissing()
    {
        _ = EngineKeywordFixture.Require(EngineKeywordFixture.SchemaBackedKeyword);
        DscKeywordRegistry.Reset();
    }

    [Fact]
    public void ConvertToDscObject_WithUnresolvedResourceFollowedByADetachedBody_ShouldWarnAndSkipBothStatements()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    DscParserGoneResource "OldInstance"
                    {
                        Path = 'a'
                    }

                    Archive Keep
                    {
                        Path = 'a'
                        Destination = 'b'
                    }
                }
            }
            """;

        try
        {
            var (instances, warnings) = ConvertCapturingWarnings(content, "Archive");

            Assert.Single(instances);
            Assert.Equal("Keep", instances[0].ResourceInstanceName);
            Assert.Contains(warnings, w =>
                w.Contains("DscParserGoneResource", StringComparison.Ordinal) &&
                w.Contains("OldInstance", StringComparison.Ordinal) &&
                w.Contains("omitted from the converted configuration", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithUnresolvedResourceOnASingleLine_ShouldWarnOnceAndKeepTheFollowingResource()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    DscParserGoneResource "OldInstance" { Path = 'a' }

                    Archive Keep
                    {
                        Path = 'a'
                        Destination = 'b'
                    }
                }
            }
            """;

        try
        {
            var (instances, warnings) = ConvertCapturingWarnings(content, "Archive");

            Assert.Single(instances);
            Assert.Single(warnings);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithResourceMissingFromTheSuppliedSet_ShouldWarnAndOmitIt()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    Archive Skipped
                    {
                        Path = 'a'
                        Destination = 'b'
                    }
                }
            }
            """;

        try
        {
            var (instances, warnings) = ConvertCapturingWarnings(content, "Log");

            Assert.Empty(instances);
            Assert.Contains(warnings, w => w.Contains("was not found among the loaded DSC resources", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithoutANodeStatement_ShouldThrow()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Archive Orphan
                {
                    Path = 'a'
                    Destination = 'b'
                }
            }
            """;

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => Convert(content, null, "Archive"));

            Assert.Contains("No Node statement found", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithCimInstanceArray_ShouldReturnOneDictionaryPerItem()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    Archive WithCredentials
                    {
                        Path = 'a'
                        Destination = 'b'
                        Credential = @(
                            MSFT_Credential {
                                UserName = 'user'
                                Password = 'secret'
                            }
                        )
                    }
                }
            }
            """;

        try
        {
            var instances = Convert(content, null, "Archive");

            var credentials = Assert.IsType<List<object>>(instances[0].GetProperty("Credential"));
            var first = Assert.IsType<Dictionary<string, object?>>(credentials[0]);

            Assert.Equal("MSFT_Credential", first["CIMInstance"]);
            Assert.Equal("user", first["UserName"]);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithIncludeCimInstanceInfoDisabled_ShouldOmitTheCimInstanceKey()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    Archive WithCredentials
                    {
                        Path = 'a'
                        Destination = 'b'
                        PsDscRunAsCredential = MSFT_Credential {
                            UserName = 'user'
                            Password = 'secret'
                        }
                    }
                }
            }
            """;

        try
        {
            var instances = Convert(content, new DscParseOptions { IncludeCIMInstanceInfo = false }, "Archive");

            var credential = Assert.IsType<Dictionary<string, object?>>(instances[0].GetProperty("PsDscRunAsCredential"));

            Assert.DoesNotContain("CIMInstance", credential.Keys);
            Assert.Equal("user", credential["UserName"]);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithNewObjectCredential_ShouldKeepTheCommandText()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    Archive WithCredentials
                    {
                        Path = 'a'
                        Destination = 'b'
                        Credential = New-Object System.Management.Automation.PSCredential('user', $secure)
                    }
                }
            }
            """;

        try
        {
            var instances = Convert(content, null, "Archive");

            string credential = Assert.IsType<string>(instances[0].GetProperty("Credential"));

            Assert.StartsWith("New-Object ", credential, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithExpandableInstanceName_ShouldUseItsLiteralText()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    Archive "Instance$suffix"
                    {
                        Path = 'a'
                        Destination = 'b'
                    }
                }
            }
            """;

        try
        {
            var instances = Convert(content, null, "Archive");

            Assert.Equal("Instance$suffix", instances[0].ResourceInstanceName);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithComments_ShouldAttachTheExactCommentText()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    Archive "Commented"
                    {
                        Path = 'a'
                        Destination = 'b' # keep it
                    }
                }
            }
            """;

        try
        {
            var instances = Convert(content, new DscParseOptions { IncludeComments = true }, "Archive");

            Assert.Equal("# keep it", instances[0].GetProperty("_metadata_Destination"));
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithACommentNamingAnAbsentProperty_ShouldNotAttachMetadata()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    Archive "Commented"
                    {
                        Path = 'a'
                        Destination = 'b'
                        # a standalone comment
                    }
                }
            }
            """;

        try
        {
            var instances = Convert(content, new DscParseOptions { IncludeComments = true }, "Archive");

            Assert.DoesNotContain(instances[0].Properties.Keys, k => k.StartsWith("_metadata_", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithTwoInstancesOfTheSameResource_ShouldAttachEachCommentToItsOwnInstance()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    Archive "First"
                    {
                        Path = 'a'
                        Destination = 'b' # first comment
                    }

                    Archive "Second"
                    {
                        Path = 'c'
                        Destination = 'd' # second comment
                    }
                }
            }
            """;

        try
        {
            var instances = Convert(content, new DscParseOptions { IncludeComments = true }, "Archive");

            Assert.Equal("# first comment", instances[0].GetProperty("_metadata_Destination"));
            Assert.Equal("# second comment", instances[1].GetProperty("_metadata_Destination"));
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithStringBeforeCimInstanceInArray_ShouldReadBoth()
    {
        const string schema = """
            [ClassVersion("1.0.0.0")]
            class MSFT_DscParserArrayItem
            {
                [Write] String Target;
            };

            [ClassVersion("1.0.0.0"), FriendlyName("DscParserArrayResource")]
            class MSFT_DscParserArrayResource : OMI_BaseResource
            {
                [Key] String Identity;
                [Write, EmbeddedInstance("MSFT_DscParserArrayItem")] String Items[];
            };
            """;

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    DscParserArrayResource Corp
                    {
                        Identity = 'Corp'
                        Items = @(
                            'plain'
                            MSFT_DscParserArrayItem {
                                Target = 'AllUsers'
                            }
                        )
                    }
                }
            }
            """;

        using var module = TestDscModule.CreateMofResource("DscParserArrayModule", "MSFT_DscParserArrayResource", schema);
        try
        {
            DscKeywordRegistry.Reset();
            DscKeywordRegistry.ImportModules([module.ModuleInfo]);

            var items = Assert.IsType<List<object>>(Convert(content, null, "DscParserArrayResource")[0].GetProperty("Items"));

            Assert.Equal("plain", items[0]);
            Assert.Equal("AllUsers", Assert.IsType<Dictionary<string, object?>>(items[1])["Target"]);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithMissingMandatoryProperty_ShouldWarnAndKeepTheResource()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    Archive Partial
                    {
                        Path = 'a'
                    }
                    Archive Empty
                    {
                        Ensure = 'Present'
                    }
                }
            }
            """;

        try
        {
            var (instances, warnings) = ConvertCapturingWarnings(content, "Archive");

            Assert.Equal("a", instances[0].GetProperty("Path"));
            Assert.Equal("Present", instances[1].GetProperty("Ensure"));
            Assert.Equal(
                [
                    "Resource 'Archive' (instance 'Partial', line 5) is missing the mandatory property 'Destination'.",
                    "Resource 'Archive' (instance 'Empty', line 9) is missing the mandatory properties 'Destination', 'Path'.",
                ],
                warnings);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithTwoNodeBlocksAndAResourceBesideThem_ShouldReadEveryResourceInDocumentOrder()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    Archive First
                    {
                        Path = 'a'
                        Destination = 'b'
                    }
                }
                Archive Beside
                {
                    Path = 'c'
                    Destination = 'd'
                }
                Node other
                {
                    Archive Second
                    {
                        Path = 'e'
                        Destination = 'f'
                    }
                }
            }
            """;

        try
        {
            var (instances, warnings) = ConvertCapturingWarnings(content, "Archive");

            Assert.Equal(["First", "Beside", "Second"], instances.Select(instance => instance.ResourceInstanceName));
            Assert.Equal(
                ["Read the resources of the Node blocks 'localhost', 'other' into one configuration."],
                warnings);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithCommandFormValueOnTheNextLine_ShouldReadIt()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    Archive WithCredentials
                    {
                        Path = 'a'
                        Destination = 'b'
                        PsDscRunAsCredential = MSFT_Credential {
                            UserName =
                                'user'
                            Password = 'secret'
                        }
                    }
                }
            }
            """;

        try
        {
            var credential = Assert.IsType<Dictionary<string, object?>>(Convert(content, null, "Archive")[0].GetProperty("PsDscRunAsCredential"));

            Assert.Equal("user", credential["UserName"]);
            Assert.Equal("secret", credential["Password"]);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithCommandInArray_ShouldKeepItsTextAndWarn()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    Archive WithCredentials
                    {
                        Path = 'a'
                        Destination = 'b'
                        Credential = @(
                            Get-Item $path
                        )
                    }
                }
            }
            """;

        try
        {
            var (instances, warnings) = ConvertCapturingWarnings(content, "Archive");

            Assert.Equal(new object[] { "Get-Item $path" }, Assert.IsType<List<object>>(instances[0].GetProperty("Credential")));
            Assert.Equal(["The value 'Get-Item $path' (line 10) is not a constant expression and was kept as text."], warnings);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithNodeBlockInsideAnIfStatement_ShouldReadIt()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                if ($true)
                {
                    Node localhost
                    {
                        Archive InIf
                        {
                            Path = 'a'
                            Destination = 'b'
                        }
                    }
                }
            }
            """;

        try
        {
            Assert.Equal("InIf", Assert.Single(Convert(content, null, "Archive")).ResourceInstanceName);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithConcatenationInSingleCimInstance_ShouldFoldTheValue()
    {
        SkipIfArchiveKeywordMissing();

        const string content = """
            Configuration Test
            {
                Node localhost
                {
                    Archive WithCredentials
                    {
                        Path = 'a'
                        Destination = 'b'
                        PsDscRunAsCredential = MSFT_Credential {
                            UserName = 'contoso' + '\user'
                            Password = 'secret'
                        }
                    }
                }
            }
            """;

        try
        {
            var credential = Assert.IsType<Dictionary<string, object?>>(Convert(content, null, "Archive")[0].GetProperty("PsDscRunAsCredential"));

            Assert.Equal(@"contoso\user", credential["UserName"]);
        }
        finally
        {
            Cleanup();
        }
    }
}
