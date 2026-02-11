using DSCParser.PSDSC;
using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using DscResourcePropertyInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourcePropertyInfo;

namespace DSCParser.Tests;

public class DscResourceHelperTests
{
    #region IsHiddenResource

    [Theory]
    [InlineData("OMI_BaseResource", true)]
    [InlineData("MSFT_KeyValuePair", true)]
    [InlineData("MSFT_Credential", true)]
    [InlineData("MSFT_DSCMetaConfiguration", true)]
    [InlineData("MSFT_DSCMetaConfigurationV2", true)]
    [InlineData("OMI_ConfigurationDocument", true)]
    [InlineData("MSFT_BaseConfigurationProviderRegistration", true)]
    [InlineData("MSFT_CimConfigurationProviderRegistration", true)]
    [InlineData("MSFT_PSConfigurationProviderRegistration", true)]
    [InlineData("MSFT_FileDownloadManager", true)]
    [InlineData("MSFT_WebDownloadManager", true)]
    [InlineData("MSFT_FileResourceManager", true)]
    [InlineData("MSFT_WebResourceManager", true)]
    [InlineData("MSFT_WebReportManager", true)]
    [InlineData("OMI_MetaConfigurationResource", true)]
    [InlineData("MSFT_PartialConfiguration", true)]
    [InlineData("OMI_ConfigurationDownloadManager", true)]
    [InlineData("OMI_ResourceModuleManager", true)]
    [InlineData("OMI_ReportManager", true)]
    public void IsHiddenResource_ShouldReturnTrue_ForHiddenResources(string resourceName, bool expected)
    {
        Assert.Equal(expected, DscResourceHelpers.IsHiddenResource(resourceName));
    }

    [Theory]
    [InlineData("MSFT_xWebsite")]
    [InlineData("MSFT_SPWeb")]
    [InlineData("MyCustomResource")]
    [InlineData("")]
    public void IsHiddenResource_ShouldReturnFalse_ForNonHiddenResources(string resourceName)
    {
        Assert.False(DscResourceHelpers.IsHiddenResource(resourceName));
    }

    [Fact]
    public void IsHiddenResource_ShouldBeCaseInsensitive()
    {
        Assert.True(DscResourceHelpers.IsHiddenResource("omi_baseresource"));
        Assert.True(DscResourceHelpers.IsHiddenResource("MSFT_CREDENTIAL"));
        Assert.True(DscResourceHelpers.IsHiddenResource("msft_keyvaluepair"));
    }

    #endregion

    #region IsPatternMatched

    [Fact]
    public void IsPatternMatched_WithNullPatterns_ShouldReturnTrue()
    {
        var result = DscResourceHelpers.IsPatternMatched(null!, "AnyName");

        Assert.True(result);
    }

    [Fact]
    public void IsPatternMatched_WithEmptyPatterns_ShouldReturnTrue()
    {
        var result = DscResourceHelpers.IsPatternMatched([], "AnyName");

        Assert.True(result);
    }

    [Fact]
    public void IsPatternMatched_WithMatchingExactPattern_ShouldReturnTrue()
    {
        Assert.True(DscResourceHelpers.IsPatternMatched(["MSFT_xWebsite"], "MSFT_xWebsite"));
    }

    [Fact]
    public void IsPatternMatched_WithWildcardPattern_ShouldReturnTrue()
    {
        Assert.True(DscResourceHelpers.IsPatternMatched(["MSFT_*"], "MSFT_xWebsite"));
    }

    [Fact]
    public void IsPatternMatched_WithNonMatchingPattern_ShouldReturnFalse()
    {
        Assert.False(DscResourceHelpers.IsPatternMatched(["MSFT_xWebsite"], "MSFT_SPWeb"));
    }

    [Fact]
    public void IsPatternMatched_ShouldBeCaseInsensitive()
    {
        Assert.True(DscResourceHelpers.IsPatternMatched(["msft_xwebsite"], "MSFT_xWebsite"));
    }

    [Fact]
    public void IsPatternMatched_WithMultiplePatterns_ShouldMatchAny()
    {
        string[] patterns = ["Resource1", "Resource2"];

        Assert.True(DscResourceHelpers.IsPatternMatched(patterns, "Resource1"));
        Assert.True(DscResourceHelpers.IsPatternMatched(patterns, "Resource2"));
        Assert.False(DscResourceHelpers.IsPatternMatched(patterns, "Resource3"));
    }

    [Fact]
    public void IsPatternMatched_WithQuestionMarkWildcard_ShouldMatchSingleChar()
    {
        string[] patterns = ["Resource?"];

        Assert.True(DscResourceHelpers.IsPatternMatched(patterns, "Resource1"));
        Assert.True(DscResourceHelpers.IsPatternMatched(patterns, "ResourceA"));
        Assert.False(DscResourceHelpers.IsPatternMatched(patterns, "Resource12"));
    }

    #endregion

    #region GetImplementingModulePath

    [Fact]
    public void GetImplementingModulePath_WithNullInput_ShouldReturnNull()
    {
        var result = DscResourceHelpers.GetImplementingModulePath(null!);

        Assert.Null(result);
    }

    [Fact]
    public void GetImplementingModulePath_WithEmptyInput_ShouldReturnNull()
    {
        var result = DscResourceHelpers.GetImplementingModulePath(string.Empty);

        Assert.Null(result);
    }

    #endregion

    #region GetSyntax

    [Fact]
    public void GetSyntax_ShouldFormatResourceWithMandatoryProperties()
    {
        var resource = new DscResourceInfo { Name = "TestResource" };
        resource.UpdateProperties(
        [
            new() { Name = "Ensure", PropertyType = "[String]", IsMandatory = true, Values = ["Present", "Absent"] },
            new() { Name = "Path", PropertyType = "[String]", IsMandatory = false }
        ]);

        var result = DscResourceHelpers.GetSyntax(resource);

        Assert.Contains("TestResource [String] #ResourceName", result);
        Assert.Contains("{", result);
        Assert.Contains("}", result);
        // Mandatory properties should NOT have brackets
        Assert.Contains("Ensure = [String]{ Present | Absent }", result);
        // Optional properties SHOULD have brackets
        Assert.Contains("[Path = [String]]", result);
    }

    [Fact]
    public void GetSyntax_WithNoProperties_ShouldReturnEmptyBlock()
    {
        var resource = new DscResourceInfo { Name = "EmptyResource" };

        var result = DscResourceHelpers.GetSyntax(resource);

        Assert.Contains("EmptyResource [String] #ResourceName", result);
        Assert.Contains("{", result);
        Assert.Contains("}", result);
    }

    [Fact]
    public void GetSyntax_ShouldShowValueMapOptions()
    {
        var resource = new DscResourceInfo { Name = "TestResource" };
        resource.UpdateProperties(
        [
            new()
            {
                Name = "State",
                PropertyType = "[String]",
                IsMandatory = true,
                Values = ["Started", "Stopped"]
            }
        ]);

        var result = DscResourceHelpers.GetSyntax(resource);

        Assert.Contains("Started | Stopped", result);
    }

    #endregion

    #region ConvertTypeConstraintToTypeName

    [Fact]
    public void ConvertTypeConstraintToTypeName_MsftCredential_ShouldReturnPSCredential()
    {
        var result = DscResourceHelpers.ConvertTypeConstraintToTypeName("MSFT_Credential", []);

        Assert.Equal("[PSCredential]", result);
    }

    [Fact]
    public void ConvertTypeConstraintToTypeName_MsftKeyValuePair_ShouldReturnHashTable()
    {
        var result = DscResourceHelpers.ConvertTypeConstraintToTypeName("MSFT_KeyValuePair", []);

        Assert.Equal("[HashTable]", result);
    }

    [Fact]
    public void ConvertTypeConstraintToTypeName_MsftKeyValuePairArray_ShouldReturnHashTable()
    {
        var result = DscResourceHelpers.ConvertTypeConstraintToTypeName("MSFT_KeyValuePair[]", []);

        Assert.Equal("[HashTable]", result);
    }

    [Fact]
    public void ConvertTypeConstraintToTypeName_DscResourceName_ShouldReturnBracketed()
    {
        var dscResourceNames = new[] { "MSFT_TestResource" };

        var result = DscResourceHelpers.ConvertTypeConstraintToTypeName("MSFT_TestResource", dscResourceNames);

        Assert.Equal("[MSFT_TestResource]", result);
    }

    [Fact]
    public void ConvertTypeConstraintToTypeName_DscResourceNameArray_ShouldReturnBracketed()
    {
        var dscResourceNames = new[] { "MSFT_TestResource" };

        var result = DscResourceHelpers.ConvertTypeConstraintToTypeName("MSFT_TestResource[]", dscResourceNames);

        Assert.Equal("[MSFT_TestResource[]]", result);
    }

    [Fact]
    public void ConvertTypeConstraintToTypeName_UnknownType_ShouldReturnBracketed()
    {
        var result = DscResourceHelpers.ConvertTypeConstraintToTypeName("SomeCustomType", []);

        Assert.Equal("[SomeCustomType]", result);
    }

    #endregion
}
