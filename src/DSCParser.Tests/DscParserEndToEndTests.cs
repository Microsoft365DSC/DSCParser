using System.Collections;
using System.Reflection;
using DSCParser.CSharp;
using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;

namespace DSCParser.Tests;

/// <summary>
/// End-to-end coverage of <see cref="DscParser.ConvertToDscObject"/> against the default CIM
/// keywords the PowerShell engine loads. In this test host the engine class cache is available, so
/// the keyword table can be materialized and a real Configuration can be parsed into resource
/// instances.
/// </summary>
public class DscParserEndToEndTests
{
    private static readonly FieldInfo _dscResourcesField =
        typeof(DscParser).GetField("_dscResources", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("_dscResources field not found");

    private static void SeedResources(params string[] names)
    {
        var dict = (IDictionary)_dscResourcesField.GetValue(null)!;
        foreach (string name in names)
        {
            if (!dict.Contains(name))
            {
                dict[name] = new DscResourceInfo { Name = name };
            }
        }
    }

    [Fact]
    public void ConvertToDscObject_WithRegisteredDefaults_ShouldParseResourceInstances()
    {
        SeedResources("File", "Log");
        try
        {
            const string config =
                "Configuration TestConfig { Node localhost { " +
                "File Sample { DestinationPath = 'C:\\x'; Ensure = 'Present' } " +
                "Log SampleLog { Message = 'hello' } } }";

            var result = DscParser.ConvertToDscObject(content: config);

            Assert.Equal(2, result.Count);

            DscResourceInstance file = result[0];
            Assert.Equal("File", file.ResourceName);
            Assert.Equal("Sample", file.ResourceInstanceName);
            Assert.Equal(@"C:\x", file.Properties["DestinationPath"]);
            Assert.Equal("Present", file.Properties["Ensure"]);

            DscResourceInstance log = result[1];
            Assert.Equal("Log", log.ResourceName);
            Assert.Equal("SampleLog", log.ResourceInstanceName);
            Assert.Equal("hello", log.Properties["Message"]);
        }
        finally
        {
            DscParser.ClearCaches();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithUnregisteredResource_ShouldWarnAndOmit()
    {
        SeedResources("File");
        try
        {
            const string config =
                "Configuration TestConfig { Node localhost { " +
                "Environment Foo { Name = 'PATH' } " +
                "File Sample { DestinationPath = 'C:\\x'; Ensure = 'Present' } } }";

            var warnings = new List<string>();
            DscParser.WarningSink = warnings.Add;
            try
            {
                var result = DscParser.ConvertToDscObject(content: config);

                Assert.Single(result);
                Assert.Equal("Sample", result[0].ResourceInstanceName);
            }
            finally
            {
                DscParser.WarningSink = null;
            }

            Assert.Contains(warnings, w => w.Contains("Environment"));
        }
        finally
        {
            DscParser.ClearCaches();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithComments_ShouldAttachPropertyMetadata()
    {
        SeedResources("File");
        try
        {
            const string config =
                "Configuration TestConfig { Node localhost { " +
                "File \"Sample\" { DestinationPath = 'C:\\x'\nEnsure = 'Present' # comment about Ensure\n} } }";

            var options = new DscParseOptions { IncludeComments = true };
            var result = DscParser.ConvertToDscObject(content: config, options: options);

            Assert.Single(result);
            DscResourceInstance file = result[0];
            Assert.NotNull(file.GetProperty("_metadata_Ensure"));
        }
        finally
        {
            DscParser.ClearCaches();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithExpandableInstanceName_ShouldParse()
    {
        SeedResources("File");
        try
        {
            const string config =
                "Configuration TestConfig { Node localhost { " +
                "File \"Sam$ple\" { DestinationPath = 'C:\\x'; Ensure = 'Present' } } }";

            var result = DscParser.ConvertToDscObject(content: config);

            Assert.Single(result);
            Assert.Equal("File", result[0].ResourceName);
            Assert.Equal("Sam$ple", result[0].ResourceInstanceName);
        }
        finally
        {
            DscParser.ClearCaches();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithImportDscResourceModuleVersion_ShouldParse()
    {
        SeedResources("File");
        try
        {
            const string config =
                "Import-DscResource -ModuleName File -ModuleVersion 1.0.0.0\n" +
                "Configuration TestConfig { Node localhost { " +
                "File Sample { DestinationPath = 'C:\\x'; Ensure = 'Present' } } }";

            var warnings = new List<string>();
            DscParser.WarningSink = warnings.Add;
            try
            {
                var result = DscParser.ConvertToDscObject(content: config);

                Assert.Single(result);
                Assert.Equal("Sample", result[0].ResourceInstanceName);
            }
            finally
            {
                DscParser.WarningSink = null;
            }
        }
        finally
        {
            DscParser.ClearCaches();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithCimInstance_ShouldParseCredential()
    {
        SeedResources("File");
        try
        {
            const string config =
                "Configuration TestConfig { Node localhost { " +
                "File Sample { DestinationPath = 'C:\\x'; " +
                "PsDscRunAsCredential = MSFT_Credential{ UserName = 'u'; Password = 'p' } } } }";

            var result = DscParser.ConvertToDscObject(content: config);

            Assert.Single(result);
            var credential = (IDictionary)result[0].Properties["PsDscRunAsCredential"]!;
            Assert.Equal("MSFT_Credential", credential["CIMInstance"]);
            Assert.Equal("u", credential["UserName"]);
            Assert.Equal("p", credential["Password"]);
        }
        finally
        {
            DscParser.ClearCaches();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithPathAndNoContent_ShouldReadFile()
    {
        SeedResources("File");
        string dir = Path.Combine(Path.GetTempPath(), $"dscparser_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "config.ps1");
        try
        {
            File.WriteAllText(
                file,
                "Configuration TestConfig { Node localhost { " +
                "File Sample { DestinationPath = 'C:\\x'; Ensure = 'Present' } } }");

            var result = DscParser.ConvertToDscObject(path: file);

            Assert.Single(result);
            Assert.Equal("Sample", result[0].ResourceInstanceName);
        }
        finally
        {
            DscParser.ClearCaches();
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ConvertToDscObject_WithNoConfigurationBlock_ShouldThrow()
    {
        SeedResources("File");
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => DscParser.ConvertToDscObject(content: "File Sample { DestinationPath = 'C:\\x' }"));
        }
        finally
        {
            DscParser.ClearCaches();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithNullPathAndContent_ShouldThrow()
    {
        SeedResources("File");
        try
        {
            Assert.Throws<ArgumentException>(
                () => DscParser.ConvertToDscObject(path: null, content: null!));
        }
        finally
        {
            DscParser.ClearCaches();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithUnquotedGuidValue_ShouldQuoteItAndParse()
    {
        SeedResources("File");
        try
        {
            const string config = """
                Configuration TestConfig
                {
                    Node localhost
                    {
                        File Sample
                        {
                            DestinationPath = 'C:\x';
                            OrganizationId  = 12345678-1234-1234-ad9c-123456789abc;
                            Ensure          = 'Present';
                        }
                    }
                }
                """;

            var warnings = new List<string>();
            DscParser.WarningSink = warnings.Add;
            try
            {
                var result = DscParser.ConvertToDscObject(content: config);

                Assert.Single(result);
                Assert.Equal("12345678-1234-1234-ad9c-123456789abc", result[0].Properties["OrganizationId"]);
                Assert.Equal("Present", result[0].Properties["Ensure"]);
            }
            finally
            {
                DscParser.WarningSink = null;
            }

            Assert.Contains(warnings, w => w.Contains("Quoted 1 unquoted value"));
        }
        finally
        {
            DscParser.ClearCaches();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithUnquotedGuidInsideCimInstance_ShouldQuoteIt()
    {
        SeedResources("File");
        try
        {
            const string config = """
                Configuration TestConfig
                {
                    Node localhost
                    {
                        File Sample
                        {
                            DestinationPath = 'C:\x';
                            PsDscRunAsCredential = MSFT_Credential{
                                UserName = 12345678-1234-1234-ad9c-123456789abc
                                Password = 'p'
                            };
                        }
                    }
                }
                """;

            var result = DscParser.ConvertToDscObject(content: config);

            Assert.Single(result);
            var credential = (IDictionary)result[0].Properties["PsDscRunAsCredential"]!;
            Assert.Equal("12345678-1234-1234-ad9c-123456789abc", credential["UserName"]);
            Assert.Equal("p", credential["Password"]);
        }
        finally
        {
            DscParser.ClearCaches();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithUnrepairableError_ShouldStillThrow()
    {
        SeedResources("File");
        try
        {
            const string config = """
                Configuration TestConfig
                {
                    Node localhost
                    {
                        File Sample
                        {
                            DestinationPath = 'C:\x'; Ensure = @{
                        }
                    }
                }
                """;

            Assert.Throws<InvalidOperationException>(() => DscParser.ConvertToDscObject(content: config));
        }
        finally
        {
            DscParser.ClearCaches();
        }
    }
}
